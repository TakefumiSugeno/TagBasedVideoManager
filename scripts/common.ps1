# =============================================================================
# scripts/common.ps1: コンテナ運用共通関数ライブラリ
# =============================================================================

# システム専用 WSL ディストロ（ユーザーディストロ自動検出対象から除外）
$script:SystemWslDistros = @("docker-desktop", "docker-desktop-data")

function Test-PathTraversalSegment {
    <#
    .SYNOPSIS
    パスセグメント境界での '..' トラバーサル有無を判定する。
    'v1..v2' のような正当なディレクトリ名は許可する。
    #>
    param(
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrEmpty($Path)) {
        return $false
    }

    return ($Path -match '(^|[\\/])\.\.($|[\\/])')
}

function ConvertTo-CollapsedLeadingSlashPath {
    <#
    .SYNOPSIS
    先頭が3本以上のスラッシュ（例: ///home/user/data）で始まる Linux パスを単一スラッシュ始まり（/home/user/data）へ正規化する。
    ルート単体（/, /// 等）や2本スラッシュ始まりの UNC（//server/share）は変換しない。
    #>
    param(
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrEmpty($Path)) {
        return $Path
    }
    if ($Path -match '^/{3,}([^/].*)$') {
        return "/" + $Matches[1]
    }
    return $Path
}

function Test-WslUncPrefix {
    <#
    .SYNOPSIS
    \\wsl.localhost\... または \\wsl$\...（// 区切り含む）のホスト境界を満たす WSL UNC パスか判定する。
    \\wsl.localhost.evil\... や //wsl$evil/... は $false を返す。
    #>
    param(
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrEmpty($Path)) {
        return $false
    }
    return ($Path -match '^(\\\\|//)(wsl\.localhost|wsl\$)([\\/]|$)')
}

function Test-NetworkUncPrefix {
    <#
    .SYNOPSIS
    2本のバックスラッシュまたは2本のスラッシュ（直後がスラッシュでない）で始まる UNC パスか判定する。
    #>
    param(
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrEmpty($Path)) {
        return $false
    }
    return ($Path -match '^(\\\\(?![\\/])|//(?!/))')
}

function Invoke-ExternalProcessWithTimeout {
    <#
    .SYNOPSIS
    タイムアウト付きで外部プロセスを実行し、標準出力・標準エラー・終了コードを安全に回収する。
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [string[]]$Arguments = @(),
        [int]$TimeoutMs = 5000,
        [hashtable]$EnvironmentVariables = $null
    )

    # Win32 CreateProcess は System32 を PATH より優先するため、非ルート指定時は Get-Command で PATH 先頭から解決する
    $resolvedFile = $FileName
    if (-not [System.IO.Path]::IsPathRooted($FileName)) {
        $cmdInfo = Get-Command -Name $FileName -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $cmdInfo -and -not [string]::IsNullOrWhiteSpace($cmdInfo.Source)) {
            $resolvedFile = $cmdInfo.Source
        }
    }

    $psi = [System.Diagnostics.ProcessStartInfo]::new($resolvedFile)
    foreach ($arg in $Arguments) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    if ($null -ne $EnvironmentVariables) {
        foreach ($key in $EnvironmentVariables.Keys) {
            $psi.Environment[[string]$key] = [string]$EnvironmentVariables[$key]
        }
    }

    $proc = $null
    try {
        $proc = [System.Diagnostics.Process]::new()
        $proc.StartInfo = $psi
        [void]$proc.Start()
        $proc.StandardInput.Close()

        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()

        if (-not $proc.WaitForExit($TimeoutMs)) {
            try { $proc.Kill($true) } catch {}
            return [PSCustomObject]@{
                StartFailed  = $false
                TimedOut     = $true
                ExitCode     = -1
                StdOut       = ""
                StdErr       = ""
                ErrorMessage = "${FileName} が ${TimeoutMs}ms 以内に終了しませんでした。"
            }
        }

        $proc.WaitForExit()
        return [PSCustomObject]@{
            StartFailed  = $false
            TimedOut     = $false
            ExitCode     = $proc.ExitCode
            StdOut       = $stdoutTask.GetAwaiter().GetResult()
            StdErr       = $stderrTask.GetAwaiter().GetResult()
            ErrorMessage = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            StartFailed  = $true
            TimedOut     = $false
            ExitCode     = -1
            StdOut       = ""
            StdErr       = ""
            ErrorMessage = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $proc) {
            $proc.Dispose()
        }
    }
}

function ConvertTo-WslPath {
    <#
    .SYNOPSIS
    Windows / WSL UNC / Linux パスを WSL Linux 内部表現（/mnt/c/... または /home/...）へ正規化する。
    #>
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $trimmed = $Path.Trim()

    # WSL UNC パス (例: \\wsl.localhost\Ubuntu-24.04\home\user, //wsl$/Ubuntu/data) を Linux 内部パスへ変換
    if ($trimmed -match '^(\\\\|//)(wsl\.localhost|wsl\$)[\\/]([^\\/]+)(?:[\\/](.*))?$') {
        $sub = if ($Matches[4]) { ($Matches[4].Replace('\', '/') -replace '/+', '/').Trim('/') } else { "" }
        if ([string]::IsNullOrEmpty($sub)) {
            return "/"
        }
        return "/$sub"
    }

    # すでに Linux / WSL 形式のパスの場合は末尾スラッシュを正規化して返却
    if ($trimmed.StartsWith("/")) {
        $normalized = $trimmed -replace '/+', '/'
        if ($normalized.Length -gt 1) {
            return $normalized.TrimEnd('/')
        }
        return $normalized
    }

    # ドライブレター形式 (例: C:\... または c:/...) の正規化
    if ($trimmed -match '^([a-zA-Z]):[\\/](.*)$') {
        $drive = $Matches[1].ToLowerInvariant()
        $rest = ($Matches[2].Replace('\', '/') -replace '/+', '/').TrimEnd('/')
        if ([string]::IsNullOrEmpty($rest)) {
            return "/mnt/$drive"
        }
        return "/mnt/$drive/$rest"
    }

    # バックスラッシュをスラッシュに置換
    $converted = $trimmed.Replace('\', '/')
    if ($converted.Length -gt 1 -and $converted.EndsWith("/")) {
        $converted = $converted.TrimEnd('/')
    }
    return $converted
}

function Get-DefaultWslDistro {
    <#
    .SYNOPSIS
    システム専用ディストロ（docker-desktop 等）を除外し、既定のユーザー WSL ディストロ名を取得する。
    #>
    param(
        [scriptblock]$GetWslList = $null,
        [int]$TimeoutMs = 5000
    )

    $recoveryGuide = "回復方法: 'wsl --status' や 'wsl -l -v' で WSL ディストロの状態を確認するか、src/TagBasedVideoManager/appsettings.Production.json 等で '\\wsl.localhost\<Distro名>\<パス>' 形式を直接指定してください。"

    $rawLines = @()
    if ($null -ne $GetWslList) {
        $injected = & $GetWslList
        if ($null -ne $injected) {
            foreach ($item in @($injected)) {
                if ($null -ne $item) {
                    $rawLines += ([string]$item -split "`r?`n")
                }
            }
        }
    }
    else {
        $res = Invoke-ExternalProcessWithTimeout -FileName "wsl.exe" -Arguments @("-l", "-q") -TimeoutMs $TimeoutMs -EnvironmentVariables @{ WSL_UTF8 = "1" }
        if ($res.StartFailed) {
            throw "WSL コマンド (wsl.exe) を起動できませんでした: $($res.ErrorMessage)。$recoveryGuide"
        }
        if ($res.TimedOut) {
            throw "WSL ディストロ一覧の取得が ${TimeoutMs}ms でタイムアウトしました。$recoveryGuide"
        }
        if ($res.ExitCode -ne 0) {
            $diagOut = (($res.StdErr + " " + $res.StdOut) -replace "`0", "").Trim()
            $detail = if (-not [string]::IsNullOrWhiteSpace($diagOut)) { " (出力: $diagOut)" } else { "" }
            throw "wsl.exe -l -q が異常終了しました (ExitCode=$($res.ExitCode))$detail。$recoveryGuide"
        }
        $rawLines = $res.StdOut -split "`r?`n"
    }

    $distros = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $rawLines) {
        if ($null -eq $line) { continue }
        # UTF-16LE 由来の NULL 文字・BOM・置換文字を除去
        $cleaned = ($line -replace "`0", "").Trim().Trim([char]0xFEFF, [char]0xFFFD).Trim()
        if ([string]::IsNullOrWhiteSpace($cleaned)) { continue }
        if ($script:SystemWslDistros -contains $cleaned.ToLowerInvariant()) { continue }
        $distros.Add($cleaned)
    }

    if ($distros.Count -eq 0) {
        throw "利用可能なユーザー WSL ディストロが見つかりませんでした。$recoveryGuide"
    }

    return $distros[0]
}

function Resolve-WslUncPath {
    <#
    .SYNOPSIS
    WSL UNC パス（\\wsl.localhost\... / \\wsl$\...）および Linux 絶対パス（/...）を正規化し、
    DistroName / SubPath / UncPath / LinuxPath を返す。
    #>
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Path,
        [scriptblock]$GetDefaultDistro = $null
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "WSL パスが空です。'\\wsl.localhost\<Distro名>\<サブパス>' または '/<サブパス>' を指定してください。"
    }

    $trimmed = ConvertTo-CollapsedLeadingSlashPath -Path ($Path.Trim())

    if (Test-PathTraversalSegment -Path $trimmed) {
        throw "パストラバーサル（'..' セグメント）を含むパスは指定できません: $Path"
    }

    # WSL UNC 形式（\\wsl.localhost\... または \\wsl$\...、// 区切り含む）
    if (Test-WslUncPrefix -Path $trimmed) {
        $rest = if ($trimmed -match '^(\\\\|//)(wsl\.localhost|wsl\$)(?:[\\/](.*))?$' -and $Matches[3]) { $Matches[3] } else { "" }
        $parts = @($rest -split '[\\/]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($parts.Count -lt 1) {
            throw "WSL UNC パスにディストロ名が指定されていません: $Path。例: '\\wsl.localhost\Ubuntu-24.04\home\user\data'"
        }
        $distro = $parts[0]
        if ($parts.Count -lt 2) {
            throw "WSL UNC パスにディストロ名配下のサブパスが指定されていません: $Path。例: '\\wsl.localhost\$distro\home\user\data'"
        }
        $subSegments = @($parts[1..($parts.Count - 1)])
        if ($subSegments -contains '.' -or $subSegments -contains '..') {
            throw "WSL UNC パスに無効な相対セグメント（. または ..）が含まれています: $Path"
        }
        $subPath = ($subSegments -join '\')
        $linuxPath = "/" + ($subSegments -join '/')
        $uncPath = "\\wsl.localhost\$distro\$subPath"
        return [PSCustomObject]@{
            DistroName = $distro
            SubPath    = $subPath
            UncPath    = $uncPath
            LinuxPath  = $linuxPath
        }
    }

    # 非 WSL の一般的なネットワーク UNC は拒否
    if (Test-NetworkUncPrefix -Path $trimmed) {
        throw "非 WSL のネットワーク UNC パスはサポートされていません: $Path。WSL の場合は '\\wsl.localhost\<Distro名>\<パス>' を指定してください。"
    }

    # Linux 絶対パス（/ で始まる場合）は既定ユーザー WSL ディストロで UNC へ解決
    if ($trimmed.StartsWith("/")) {
        $segments = @($trimmed -split '/+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($segments.Count -eq 0) {
            throw "Linux ルート '/' 単体は指定できません。サブディレクトリを指定してください（例: '/home/user/tbvm/data'）。"
        }
        if ($segments -contains '.' -or $segments -contains '..') {
            throw "Linux パスに無効な相対セグメント（. または ..）が含まれています: $Path"
        }
        $distro = if ($null -ne $GetDefaultDistro) { & $GetDefaultDistro } else { Get-DefaultWslDistro }
        if ([string]::IsNullOrWhiteSpace($distro)) {
            throw "既定の WSL ディストロ名を取得できませんでした。'\\wsl.localhost\<Distro名>\<パス>' 形式で明示指定してください。"
        }
        $distro = $distro.Trim()
        $subPath = ($segments -join '\')
        $linuxPath = "/" + ($segments -join '/')
        $uncPath = "\\wsl.localhost\$distro\$subPath"
        return [PSCustomObject]@{
            DistroName = $distro
            SubPath    = $subPath
            UncPath    = $uncPath
            LinuxPath  = $linuxPath
        }
    }

    throw "WSL UNC パスまたは Linux 絶対パスではありません: $Path"
}

function ConvertTo-ContainerMountPath {
    <#
    .SYNOPSIS
    コンテナ CLI へ渡すホスト側マウント元パスを生成する。
    TargetIsWindows=$true の場合は Windows パスは 'D:/X'、WSL パスは '\\wsl.localhost\<Distro>\...' を返す。
    #>
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Path,
        [bool]$TargetIsWindows = $true,
        [scriptblock]$GetDefaultDistro = $null
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "マウントパスが空です。"
    }

    $trimmed = $Path.Trim()
    if (Test-PathTraversalSegment -Path $trimmed) {
        throw "パストラバーサル（'..' セグメント）を含むパスはマウントできません: $Path"
    }
    if ($trimmed -match '^/+$') {
        throw "Linux ルート '/' 単体はマウントできません: $Path"
    }
    $trimmed = ConvertTo-CollapsedLeadingSlashPath -Path $trimmed

    $isWslUnc = Test-WslUncPrefix -Path $trimmed
    $isAnyUnc = Test-NetworkUncPrefix -Path $trimmed
    $isLinuxAbs = $trimmed.StartsWith("/")

    if ($TargetIsWindows) {
        if ($isWslUnc -or $isLinuxAbs) {
            return (Resolve-WslUncPath -Path $trimmed -GetDefaultDistro $GetDefaultDistro).UncPath
        }
        if ($isAnyUnc) {
            throw "非 WSL のネットワーク UNC パスはサポートされていません: $Path"
        }

        $slash = $trimmed.Replace('\', '/') -replace '/+', '/'
        if ($slash -notmatch '^[a-zA-Z]:/$' -and $slash.Length -gt 1) {
            $slash = $slash.TrimEnd('/')
        }
        return $slash
    }

    if ($isAnyUnc -and -not $isWslUnc) {
        throw "非 WSL のネットワーク UNC パスはサポートされていません: $Path"
    }

    return (ConvertTo-WslPath -Path $trimmed)
}

function Resolve-HostDirectoryInfo {
    <#
    .SYNOPSIS
    DATA_DIR および VIDEO_DIRS で共通となるホストディレクトリパス検証・解決を行う内部ヘルパー。
    WSL UNC / Linux 絶対パス / Windows 絶対パス / 相対パス（RepoRoot が WSL UNC の場合を含む）を一元解決する。
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Target,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [bool]$TargetIsWindows = $true,
        [scriptblock]$GetDefaultDistro = $null,
        [string]$ContextLabel = "パス"
    )

    if (Test-PathTraversalSegment -Path $Target) {
        throw "エラー: ${ContextLabel} にパストラバーサル（'..' セグメント）は指定できません（無効なフォルダ名です）: $Target"
    }
    if ($Target -match '^[a-zA-Z]:(?![\\/])') {
        throw "エラー: ${ContextLabel} にドライブ相対パスは指定できません: $Target"
    }
    if ($Target -match '^[a-zA-Z]:[\\/]*$') {
        throw "エラー: ${ContextLabel} にドライブ直下 '$Target' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。"
    }
    if ($Target -match '^/+$') {
        throw "エラー: ${ContextLabel} にルート '/' 単体はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。"
    }

    $normalizedTarget = ConvertTo-CollapsedLeadingSlashPath -Path $Target

    if ((Test-WslUncPrefix -Path $normalizedTarget) -or $normalizedTarget.StartsWith("/")) {
        if ($TargetIsWindows -or (Test-WslUncPrefix -Path $normalizedTarget)) {
            $unc = Resolve-WslUncPath -Path $normalizedTarget -GetDefaultDistro $GetDefaultDistro
            $hostPath = if ($TargetIsWindows) { $unc.UncPath } else { $unc.LinuxPath }
            return [PSCustomObject]@{
                DistroName  = $unc.DistroName
                HostPath    = $hostPath
                MountSource = $hostPath
            }
        }
        $linuxClean = ($normalizedTarget -replace '/+', '/').TrimEnd('/')
        return [PSCustomObject]@{
            DistroName  = ""
            HostPath    = $linuxClean
            MountSource = $linuxClean
        }
    }

    if (Test-NetworkUncPrefix -Path $normalizedTarget) {
        throw "エラー: ${ContextLabel} に非 WSL のネットワーク UNC パスは指定できません: $Target。WSL の場合は '\\wsl.localhost\<Distro名>\<パス>' を指定してください。"
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($normalizedTarget)) {
        $normalizedTarget
    }
    else {
        Join-Path $RepoRoot $normalizedTarget
    }
    $full = [System.IO.Path]::GetFullPath($candidate)
    if ($full -match '^[a-zA-Z]:[\\/]*$') {
        throw "エラー: ${ContextLabel} にドライブ直下 '$Target' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。"
    }

    # RepoRoot が WSL UNC パスの場合、相対パス結合後の結果も WSL UNC として正規化する
    if (Test-WslUncPrefix -Path $full) {
        $unc = Resolve-WslUncPath -Path $full -GetDefaultDistro $GetDefaultDistro
        $hostPath = if ($TargetIsWindows) { $unc.UncPath } else { $unc.LinuxPath }
        return [PSCustomObject]@{
            DistroName  = $unc.DistroName
            HostPath    = $hostPath
            MountSource = $hostPath
        }
    }
    if (Test-NetworkUncPrefix -Path $full) {
        throw "エラー: ${ContextLabel} に非 WSL のネットワーク UNC パスは指定できません: $full"
    }

    $hostPath = $full.TrimEnd('\', '/')
    $mountSource = if ($TargetIsWindows) { $hostPath.Replace('\', '/') } else { ConvertTo-WslPath -Path $hostPath }
    return [PSCustomObject]@{
        DistroName  = ""
        HostPath    = $hostPath
        MountSource = $mountSource
    }
}

function Resolve-DataDir {
    <#
    .SYNOPSIS
    DATA_DIR を早期検証・解決し、必要に応じてディレクトリを事前作成する。
    #>
    param(
        [AllowEmptyString()]
        [string]$Path = "",
        [string]$RepoRoot = "",
        [switch]$CreateIfMissing,
        [scriptblock]$GetDefaultDistro = $null,
        [string]$ResolvedFrom = ""
    )

    $effectiveRepoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $PWD.Path } else { $RepoRoot }
    $rawInput = if ($null -eq $Path) { "" } else { $Path }
    $trimmed = $rawInput.Trim()
    $target = if ([string]::IsNullOrWhiteSpace($trimmed)) { "./data" } else { $trimmed }

    $resolved = Resolve-HostDirectoryInfo -Target $target -RepoRoot $effectiveRepoRoot -TargetIsWindows $true -GetDefaultDistro $GetDefaultDistro -ContextLabel "DATA_DIR"
    $hostPath = $resolved.HostPath

    # 既存通常ファイル誤指定の無条件拒否（-CreateIfMissing の有無に関わらずチェック）
    if (Test-Path -LiteralPath $hostPath -PathType Leaf -ErrorAction SilentlyContinue) {
        throw "DATA_DIR にディレクトリではなく既存の通常ファイルが指定されています: $hostPath。データ保存用ディレクトリのパスを指定してください（例: './data'）。"
    }

    if ($CreateIfMissing) {
        if (-not (Test-Path -LiteralPath $hostPath -PathType Container -ErrorAction SilentlyContinue)) {
            try {
                # New-Item -Path は角括弧 [ ] をワイルドカード解釈するため .NET API で安全に作成する
                [System.IO.Directory]::CreateDirectory($hostPath) | Out-Null
            }
            catch {
                throw "DATA_DIR ディレクトリを作成できませんでした: $hostPath（詳細: $($_.Exception.Message)）。WSL パスの場合は 'wsl --status' でディストロの稼働状況・ディストロ名・権限を確認してください。"
            }
        }
    }

    return [PSCustomObject]@{
        RawInput     = $rawInput
        ResolvedFrom = $ResolvedFrom
        DistroName   = $resolved.DistroName
        HostPath     = $hostPath
        MountSource  = $resolved.MountSource
    }
}

function Test-PortInUse {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    try {
        $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
        foreach ($endpoint in $listeners) {
            if ($endpoint.Port -eq $Port) {
                return $true
            }
        }
        return $false
    }
    catch {
        return $false
    }
}

function Resolve-WebPort {
    param(
        [string]$PortValue
    )

    if ([string]::IsNullOrWhiteSpace($PortValue)) {
        return 5620
    }

    $parsed = 0
    if ([int]::TryParse($PortValue.Trim(), [ref]$parsed) -and $parsed -gt 0 -and $parsed -le 65535) {
        return $parsed
    }

    return 5620
}

function Get-ContainerRuntimeName {
    [CmdletBinding()]
    param(
        [scriptblock]$CommandExists,
        [Alias("IsWindows")]
        [bool]$TargetIsWindows = $true
    )

    if ($null -eq $CommandExists) {
        $CommandExists = { param($cmd) [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }
    }

    if ($TargetIsWindows) {
        if (& $CommandExists "wslc") {
            return "wslc"
        }
        if (& $CommandExists "docker") {
            return "docker"
        }
        return $null
    }
    else {
        if (& $CommandExists "docker") {
            return "docker"
        }
        if (& $CommandExists "podman") {
            return "podman"
        }
        return $null
    }
}

function Resolve-VideoMounts {
    [CmdletBinding()]
    param(
        [string]$VideoDirs,
        [string]$OldVideoDir,
        [bool]$TargetIsWindows = $IsWindows,
        [string]$RepoRoot = "",
        [scriptblock]$GetDefaultDistro = $null
    )

    $effectiveRepoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $PWD.Path } else { $RepoRoot }

    $rawPaths = if ([string]::IsNullOrWhiteSpace($VideoDirs)) {
        @()
    }
    else {
        @($VideoDirs -split ';' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($rawPaths.Count -eq 0) {
        if (-not [string]::IsNullOrWhiteSpace($OldVideoDir)) {
            throw "エラー: 旧環境変数 VIDEO_DIR ('$OldVideoDir') が指定されています。VIDEO_DIRS へ移行してください。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
        }
        throw "エラー: 環境変数 VIDEO_DIRS が設定されていません。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
    }

    $seenLeaves = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $mounts = [System.Collections.Generic.List[PSCustomObject]]::new()
    $volArgs = [System.Collections.Generic.List[string]]::new()
    $containerDirs = [System.Collections.Generic.List[string]]::new()

    foreach ($rawPath in $rawPaths) {
        $rawTrimmed = $rawPath.TrimEnd('\', '/')
        $rawLeaf = ($rawTrimmed.Replace('\', '/') -split '/')[-1]
        if (-not [string]::IsNullOrWhiteSpace($rawLeaf) -and ($rawLeaf -eq "." -or $rawLeaf -eq "..")) {
            throw "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: $rawPath"
        }

        $resolved = Resolve-HostDirectoryInfo -Target $rawPath -RepoRoot $effectiveRepoRoot -TargetIsWindows:$TargetIsWindows -GetDefaultDistro $GetDefaultDistro -ContextLabel "VIDEO_DIRS"
        $resolvedHostPath = $resolved.HostPath
        $hostMount = $resolved.MountSource

        # フォルダ名 (Leaf) の抽出と重複チェック
        $leaf = ($resolvedHostPath.Replace('\', '/').TrimEnd('/') -split '/')[-1]
        if ([string]::IsNullOrWhiteSpace($leaf) -or $leaf -eq "." -or $leaf -eq "..") {
            throw "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: $rawPath"
        }

        # 通常ファイル誤指定チェック → ディレクトリ実在チェック
        if (Test-Path -LiteralPath $resolvedHostPath -PathType Leaf -ErrorAction SilentlyContinue) {
            throw "エラー: 指定された動画パスはディレクトリではなく通常ファイルです: $rawPath。動画フォルダのディレクトリパスを指定してください。設定例: VIDEO_DIRS=`"D:\Anime`""
        }
        if (-not (Test-Path -LiteralPath $resolvedHostPath -PathType Container -ErrorAction SilentlyContinue)) {
            throw "エラー: 指定された動画フォルダが存在しません: $rawPath。正しいフォルダを設定してください。設定例: VIDEO_DIRS=`"D:\Anime`""
        }

        if (-not $seenLeaves.Add($leaf)) {
            throw "エラー: マウント先のフォルダ名 '$leaf' が重複しています ($rawPath)。異なるフォルダ名を指定してください。"
        }

        $containerPath = "/app/videos/$leaf"
        $volumeArg = "${hostMount}:${containerPath}:ro"

        $mounts.Add([PSCustomObject]@{
            HostPath      = $hostMount
            ContainerPath = $containerPath
            VolumeArg     = $volumeArg
        })
        $volArgs.Add("-v")
        $volArgs.Add($volumeArg)
        $containerDirs.Add($containerPath)
    }

    return [PSCustomObject]@{
        ContainerVideoDirs = ($containerDirs -join ";")
        VolumeArgs         = $volArgs.ToArray()
        Mounts             = $mounts.ToArray()
    }
}

function Get-ExistingContainerDataMount {
    <#
    .SYNOPSIS
    既存コンテナの /app/data バインドマウント元（Source）を取得する。
    未存在時は警告・例外なしで $null を返し、タイムアウト・非空不正 JSON 時のみ Write-Warning を出して $null を返す。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Runtime,
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,
        [int]$TimeoutMs = 5000,
        [scriptblock]$InvokeInspect = $null
    )

    $rawOutput = ""
    if ($null -ne $InvokeInspect) {
        try {
            $res = & $InvokeInspect
            if ($null -ne $res) {
                $rawOutput = (@($res) -join "`n")
            }
        }
        catch {
            Write-Warning "コンテナ '$ContainerName' の inspect 呼び出しに失敗しました: $($_.Exception.Message)"
            return $null
        }
    }
    else {
        $proc = Invoke-ExternalProcessWithTimeout -FileName $Runtime -Arguments @("inspect", $ContainerName) -TimeoutMs $TimeoutMs
        if ($proc.TimedOut) {
            Write-Warning "既存コンテナ '$ContainerName' の '$Runtime inspect' が ${TimeoutMs}ms でタイムアウトしました。マウント元変更チェックをスキップします。"
            return $null
        }
        if ($proc.StartFailed -or $proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($proc.StdOut)) {
            return $null
        }
        $rawOutput = $proc.StdOut
    }

    if ([string]::IsNullOrWhiteSpace($rawOutput)) {
        return $null
    }

    try {
        $parsed = ConvertFrom-Json -InputObject $rawOutput -ErrorAction Stop
        $first = @($parsed)[0]
        if ($null -eq $first -or $null -eq $first.Mounts) {
            $snippet = if ($rawOutput.Length -gt 120) { $rawOutput.Substring(0, 120) + "..." } else { $rawOutput.Trim() }
            Write-Warning "コンテナ '$ContainerName' の inspect JSON に Mounts 配列が見つかりませんでした（抜粋: $snippet）。"
            return $null
        }

        $dataMount = @($first.Mounts | Where-Object { $_.Destination -eq "/app/data" }) | Select-Object -First 1
        if ($null -eq $dataMount -or [string]::IsNullOrWhiteSpace([string]$dataMount.Source)) {
            return $null
        }
        return [string]$dataMount.Source
    }
    catch {
        $snippet = if ($rawOutput.Length -gt 120) { $rawOutput.Substring(0, 120) + "..." } else { $rawOutput.Trim() }
        Write-Warning "コンテナ '$ContainerName' の inspect JSON を解析できませんでした（抜粋: $snippet）。詳細: $($_.Exception.Message)"
        return $null
    }
}

function Test-SameMountSource {
    <#
    .SYNOPSIS
    既存コンテナの /app/data マウント元と新 DATA_DIR が実質的に同一パスかを比較する。
    Windows ドライブパスと WSL UNC の主机/ディストロ名部分は大小文字無視とし、WSL 内サブパスおよび Linux パスは大小文字を区別する。
    #>
    param(
        [AllowEmptyString()]
        [string]$ExistingSource,
        [AllowEmptyString()]
        [string]$NewSource
    )

    if ([string]::IsNullOrWhiteSpace($ExistingSource) -or [string]::IsNullOrWhiteSpace($NewSource)) {
        return $false
    }

    $normalizeKey = {
        param([string]$p)
        $s = $p.Trim().Replace('\', '/')
        if ($s.StartsWith("//") -and -not $s.StartsWith("///")) {
            $s = "//" + (($s.Substring(2)) -replace '/+', '/')
        }
        else {
            $s = $s -replace '/+', '/'
        }
        if ($s -match '^//(wsl\.localhost|wsl\$)/([^/]+)(/.*)?$') {
            $distroLower = $Matches[2].ToLowerInvariant()
            $sub = if ($Matches[3]) { $Matches[3] } else { "" }
            if ($sub.Length -gt 1) {
                $sub = $sub.TrimEnd('/')
            }
            return "//wsl.localhost/$distroLower$sub"
        }
        if ($s.Length -gt 1 -and $s -notmatch '^[a-zA-Z]:/$') {
            $s = $s.TrimEnd('/')
        }
        if ($s -match '^[a-zA-Z]:') {
            return $s.ToLowerInvariant()
        }
        return $s
    }

    $leftKey = & $normalizeKey $ExistingSource
    $rightKey = & $normalizeKey $NewSource
    return [string]::Equals($leftKey, $rightKey, [System.StringComparison]::Ordinal)
}

function New-RunArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string[]]$VideoMountArgs,
        [Parameter(Mandatory = $true)]
        [string]$ContainerVideoDirs,
        [Parameter(Mandatory = $true)]
        [string]$DataDir,
        [bool]$HasNvidiaGpu = $false,
        [Alias("IsLinux")]
        [bool]$TargetIsLinux = $false,
        [bool]$HasDri = $false,
        [string]$Runtime = ""
    )

    $argsList = [System.Collections.Generic.List[string]]::new()
    $argsList.Add("run")
    $argsList.Add("-d")
    $argsList.Add("--name")
    $argsList.Add($Name)
    $argsList.Add("-p")
    $argsList.Add("${Port}:5620")
    if ($VideoMountArgs) {
        foreach ($arg in $VideoMountArgs) {
            $argsList.Add($arg)
        }
    }
    $argsList.Add("-v")
    $argsList.Add("${DataDir}:/app/data")
    $argsList.Add("-e")
    $argsList.Add("PORT=5620")
    $argsList.Add("-e")
    $argsList.Add("VIDEO_DIRS=${ContainerVideoDirs}")
    $argsList.Add("-e")
    $argsList.Add("THUMBNAIL_DIR=/app/data/thumbnails")
    $argsList.Add("-e")
    $argsList.Add("DATABASE_PATH=/app/data/metadata.db")

    if ($HasNvidiaGpu) {
        $argsList.Add("--gpus")
        $argsList.Add("all")
    }
    elseif ($TargetIsLinux -and $HasDri) {
        $argsList.Add("--device")
        $argsList.Add("/dev/dri:/dev/dri")
    }

    # wslc は --restart オプションをサポートしていないため除外する
    if ($Runtime -ne "wslc") {
        $argsList.Add("--restart")
        $argsList.Add("unless-stopped")
    }

    return $argsList.ToArray()
}

function Resolve-WebDeploySettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$Environment = "Production"
    )

    $srcDir = Join-Path $RepoRoot "src" | Join-Path -ChildPath "TagBasedVideoManager"
    $baseFile = Join-Path $srcDir "appsettings.json"
    $envFileName = if (-not [string]::IsNullOrWhiteSpace($Environment)) { "appsettings.$Environment.json" } else { $null }
    $envFile = if ($envFileName) { Join-Path $srcDir $envFileName } else { $null }

    $baseObj = $null
    if (Test-Path -LiteralPath $baseFile) {
        try {
            $raw = Get-Content -LiteralPath $baseFile -Raw -Encoding UTF8
            $baseObj = ConvertFrom-Json -InputObject $raw -ErrorAction Stop
        }
        catch {
            throw "設定ファイル '$baseFile' の JSON 解析に失敗しました: $($_.Exception.Message)"
        }
    }

    $envObj = $null
    if ($envFile -and (Test-Path -LiteralPath $envFile)) {
        try {
            $raw = Get-Content -LiteralPath $envFile -Raw -Encoding UTF8
            $envObj = ConvertFrom-Json -InputObject $raw -ErrorAction Stop
        }
        catch {
            throw "設定ファイル '$envFile' の JSON 解析に失敗しました: $($_.Exception.Message)"
        }
    }

    # 優先順: envObj -> baseObj -> 既定値
    $port = 5620
    if ($envObj -and $null -ne $envObj.PORT) {
        $port = Resolve-WebPort "$($envObj.PORT)"
    }
    elseif ($baseObj -and $null -ne $baseObj.PORT) {
        $port = Resolve-WebPort "$($baseObj.PORT)"
    }

    $videoDirs = ""
    if ($envObj -and $null -ne $envObj.VIDEO_DIRS) {
        $videoDirs = "$($envObj.VIDEO_DIRS)"
    }
    elseif ($baseObj -and $null -ne $baseObj.VIDEO_DIRS) {
        $videoDirs = "$($baseObj.VIDEO_DIRS)"
    }

    $dataDir = "./data"
    $dataDirSource = "既定値"
    if ($envObj -and $null -ne $envObj.DATA_DIR -and -not [string]::IsNullOrWhiteSpace([string]$envObj.DATA_DIR)) {
        $dataDir = "$($envObj.DATA_DIR)"
        $dataDirSource = $envFileName
    }
    elseif ($baseObj -and $null -ne $baseObj.DATA_DIR -and -not [string]::IsNullOrWhiteSpace([string]$baseObj.DATA_DIR)) {
        $dataDir = "$($baseObj.DATA_DIR)"
        $dataDirSource = "appsettings.json"
    }

    return [PSCustomObject]@{
        Port          = $port
        VideoDirs     = $videoDirs
        DataDir       = $dataDir
        DataDirSource = $dataDirSource
    }
}
