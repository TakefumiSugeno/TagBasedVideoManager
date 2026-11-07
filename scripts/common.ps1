# =============================================================================
# scripts/common.ps1: コンテナ運用共通関数ライブラリ
# =============================================================================

function ConvertTo-WslPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $trimmed = $Path.Trim()

    # すでに Linux / WSL 形式のパスの場合は末尾スラッシュを正規化して返却
    if ($trimmed.StartsWith("/")) {
        if ($trimmed.Length -gt 1 -and $trimmed.EndsWith("/")) {
            return $trimmed.TrimEnd('/')
        }
        return $trimmed
    }

    # ドライブレター形式 (例: C:\... または c:/...) の正規化
    if ($trimmed -match '^([a-zA-Z]):[\\/](.*)$') {
        $drive = $Matches[1].ToLowerInvariant()
        $rest = $Matches[2].Replace('\', '/').TrimEnd('/')
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
        [bool]$TargetIsWindows = $true
    )

    if ([string]::IsNullOrWhiteSpace($VideoDirs)) {
        if (-not [string]::IsNullOrWhiteSpace($OldVideoDir)) {
            Write-Error "エラー: 旧環境変数 VIDEO_DIR ('$OldVideoDir') が指定されています。VIDEO_DIRS へ移行してください。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
            exit 1
        }
        Write-Error "エラー: 環境変数 VIDEO_DIRS が設定されていません。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
        exit 1
    }

    $rawPaths = $VideoDirs -split ';' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($rawPaths.Count -eq 0) {
        if (-not [string]::IsNullOrWhiteSpace($OldVideoDir)) {
            Write-Error "エラー: 旧環境変数 VIDEO_DIR ('$OldVideoDir') が指定されています。VIDEO_DIRS へ移行してください。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
            exit 1
        }
        Write-Error "エラー: 環境変数 VIDEO_DIRS が設定されていません。設定例: VIDEO_DIRS=`"D:\Anime;F:\Movie`""
        exit 1
    }

    $seenLeaves = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $mounts = [System.Collections.Generic.List[PSCustomObject]]::new()
    $volArgs = [System.Collections.Generic.List[string]]::new()
    $containerDirs = [System.Collections.Generic.List[string]]::new()

    foreach ($rawPath in $rawPaths) {
        # ドライブ直下 / ルートチェック
        $trimmed = $rawPath.TrimEnd('\', '/')
        if ($rawPath -match '^[a-zA-Z]:[\\/]*$' -or $rawPath -match '^/+$' -or [string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -match '^[a-zA-Z]:$') {
            Write-Error "エラー: ドライブ直下 '$rawPath' はフォルダ名（マウント先）を決定できないため指定できません。サブフォルダを指定してください。"
            exit 1
        }

        # ホスト上存在チェック
        $exists = (Test-Path -LiteralPath $rawPath -ErrorAction SilentlyContinue) -or (Test-Path -Path $rawPath -ErrorAction SilentlyContinue)
        if (-not $exists) {
            Write-Error "エラー: 指定された動画フォルダが存在しません: $rawPath"
            exit 1
        }

        # フォルダ名 (Leaf) の取得
        $rawLeaf = ($trimmed.Replace('\', '/') -split '/')[-1]
        $leaf = Split-Path -Leaf $trimmed
        if ([string]::IsNullOrWhiteSpace($leaf) -or $leaf -eq "." -or $leaf -eq ".." -or $rawLeaf -eq "." -or $rawLeaf -eq "..") {
            Write-Error "エラー: フォルダ名（マウント先）を決定できないか、パストラバーサル（. や ..）が含まれています: $rawPath"
            exit 1
        }

        # 重複チェック
        if (-not $seenLeaves.Add($leaf)) {
            Write-Error "エラー: マウント先のフォルダ名 '$leaf' が重複しています ($rawPath)。異なるフォルダ名を指定してください。"
            exit 1
        }

        $containerPath = "/app/videos/$leaf"
        $hostMount = if ($TargetIsWindows) { ConvertTo-WslPath -Path $rawPath } else { $rawPath }
        $volumeArg = "${hostMount}:${containerPath}:ro"

        $mounts.Add([PSCustomObject]@{
            HostPath = $hostMount
            ContainerPath = $containerPath
            VolumeArg = $volumeArg
        })
        $volArgs.Add("-v")
        $volArgs.Add($volumeArg)
        $containerDirs.Add($containerPath)
    }

    return [PSCustomObject]@{
        ContainerVideoDirs = ($containerDirs -join ";")
        VolumeArgs = $volArgs.ToArray()
        Mounts = $mounts.ToArray()
    }
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
