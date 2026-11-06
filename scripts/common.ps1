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

function New-RunArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$VideoDir,
        [Parameter(Mandatory = $true)]
        [string]$DataDir,
        [bool]$HasNvidiaGpu = $false,
        [Alias("IsLinux")]
        [bool]$TargetIsLinux = $false,
        [bool]$HasDri = $false
    )

    $argsList = [System.Collections.Generic.List[string]]::new()
    $argsList.Add("run")
    $argsList.Add("-d")
    $argsList.Add("--name")
    $argsList.Add($Name)
    $argsList.Add("-p")
    $argsList.Add("${Port}:5620")
    $argsList.Add("-v")
    $argsList.Add("${VideoDir}:/app/videos:ro")
    $argsList.Add("-v")
    $argsList.Add("${DataDir}:/app/data")
    $argsList.Add("-e")
    $argsList.Add("PORT=5620")
    $argsList.Add("-e")
    $argsList.Add("VIDEO_DIR=/app/videos")
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

    $argsList.Add("--restart")
    $argsList.Add("unless-stopped")

    return $argsList.ToArray()
}
