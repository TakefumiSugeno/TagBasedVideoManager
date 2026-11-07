# =============================================================================
# scripts/deploy.ps1: Windows / WSL3 向け自動デプロイスクリプト
# =============================================================================
[CmdletBinding()]
param(
    [string]$VideoDirs = $env:VIDEO_DIRS,
    [string]$DataDir = $env:DATA_DIR,
    [string]$Port = $env:PORT
)

$ErrorActionPreference = 'Stop'

# リポジトリルートを確定
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

# 共通ライブラリを dot-source
. (Join-Path $scriptDir "common.ps1")

# .env ファイルがあれば読み込み
$envFile = Join-Path $repoRoot ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.StartsWith("#")) {
            $parts = $line.Split('=', 2)
            if ($parts.Length -eq 2) {
                $key = $parts[0].Trim()
                $val = $parts[1].Trim().Trim('"', "'")
                if (-not (Get-Item "env:$key" -ErrorAction SilentlyContinue)) {
                    Set-Item "env:$key" $val
                }
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($VideoDirs)) {
    $VideoDirs = $env:VIDEO_DIRS
}

# 早期入力検証（イメージビルドおよび既存コンテナ停止より前に実行）
$resolvedMounts = Resolve-VideoMounts -VideoDirs $VideoDirs -OldVideoDir $env:VIDEO_DIR -TargetIsWindows $true

# デフォルト値の解決
$resolvedPort = Resolve-WebPort ($Port ?? $env:PORT)

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = $env:DATA_DIR
}
if ([string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = Join-Path $repoRoot "data"
}

# データディレクトリ作成
if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

$containerName = "tag-based-video-manager"
$imageName = "tag-based-video-manager:latest"

Write-Host "=== TagBasedVideoManager デプロイ開始 ===" -ForegroundColor Cyan
Write-Host "ポート: $resolvedPort"
Write-Host "動画フォルダ: $VideoDirs"
Write-Host "データフォルダ: $DataDir"

# 1. ポート事前衝突チェック
if (Test-PortInUse -Port $resolvedPort) {
    # 既存の本アプリコンテナが動いていてポートを掴んでいる可能性があるか確認する前に、
    # 別プロセスが掴んでいる場合の衝突を防ぐ
    Write-Warning "ポート $resolvedPort は既に他のプロセスで使用されています。"
}

# 2. コンテナランタイム自動検出
$runtime = Get-ContainerRuntimeName -IsWindows $true
if (-not $runtime) {
    Write-Error "利用可能なコンテナランタイムが見つかりません。Windows 11 の場合は 'wsl --update' を実行して WSL 3.0 を有効化するか、Docker Desktop をインストールしてください。"
    exit 1
}
Write-Host "検出されたランタイム: $runtime" -ForegroundColor Green

# 3. パス変換 (WSL 形式へ)
$wslDataDir = ConvertTo-WslPath -Path $DataDir

# 4. イメージビルド
Write-Host "--- コンテナイメージをビルド中 ---" -ForegroundColor Cyan
& $runtime build -t $imageName -f (Join-Path $repoRoot "Dockerfile") $repoRoot
if ($LASTEXITCODE -ne 0) {
    Write-Error "コンテナイメージのビルドに失敗しました。"
    exit 1
}

# 5. 既存コンテナの停止 & 削除
Write-Host "--- 既存コンテナの停止・削除 ---" -ForegroundColor Cyan
& $runtime stop $containerName 2>$null | Out-Null
& $runtime rm -f $containerName 2>$null | Out-Null

# 6. NVIDIA GPU 判定
$hasNvidia = $false
try {
    $smi = Get-Command "nvidia-smi" -ErrorAction SilentlyContinue
    if ($smi) { $hasNvidia = $true }
}
catch {}

# 7. 新コンテナ起動
Write-Host "--- 新コンテナを起動中 ---" -ForegroundColor Cyan
$runArgs = New-RunArguments -Name $containerName -Port $resolvedPort -VideoMountArgs $resolvedMounts.VolumeArgs -ContainerVideoDirs $resolvedMounts.ContainerVideoDirs -DataDir $wslDataDir -HasNvidiaGpu $hasNvidia -IsLinux $false -HasDri $false -Runtime $runtime
$allArgs = @($runArgs) + @($imageName)
& $runtime @allArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "コンテナの起動に失敗しました。"
    exit 1
}

# 8. ヘルスチェック (最大30秒)
Write-Host "--- ヘルスチェック実行中 (ポート 5620) ---" -ForegroundColor Cyan
$healthUrl = "http://localhost:${resolvedPort}/"
$maxRetries = 10
$retryCount = 0
$isHealthy = $false

while ($retryCount -lt $maxRetries) {
    Start-Sleep -Seconds 3
    $retryCount++
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
            $isHealthy = $true
            break
        }
    }
    catch {
        Write-Host "待機中... ($retryCount/$maxRetries)"
    }
}

if ($isHealthy) {
    Write-Host "=== デプロイ成功！TagBasedVideoManager は正常に稼働しています ($healthUrl) ===" -ForegroundColor Green
    exit 0
}
else {
    Write-Error "ヘルスチェックに失敗しました。タイムアウトしました。"
    Write-Host "--- 直近のコンテナログ ---" -ForegroundColor Yellow
    & $runtime logs --tail 50 $containerName
    exit 1
}
