# =============================================================================
# scripts/deploy.ps1: Windows / WSL3 向け自動デプロイスクリプト
# =============================================================================
[CmdletBinding()]
param(
    [string]$VideoDirs = $env:VIDEO_DIRS,
    [string]$DataDir = $env:DATA_DIR,
    [string]$Port = $env:PORT,
    [string]$Environment = ($env:ASPNETCORE_ENVIRONMENT ?? "Production")
)

$ErrorActionPreference = 'Stop'

# リポジトリルートを確定
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

# 共通ライブラリを dot-source
. (Join-Path $scriptDir "common.ps1")

# 設定解決 (CLI引数 -> 環境変数 -> appsettings.{Environment}.json -> appsettings.json)
$settings = Resolve-WebDeploySettings -RepoRoot $repoRoot -Environment $Environment

if (-not $PSBoundParameters.ContainsKey('VideoDirs') -and [string]::IsNullOrWhiteSpace($VideoDirs)) {
    $VideoDirs = $settings.VideoDirs
}

$dataDirInput = if (-not [string]::IsNullOrWhiteSpace($settings.DataDir)) { $settings.DataDir } else { "./data" }
$dataDirSource = if (-not [string]::IsNullOrWhiteSpace($settings.DataDirSource)) { $settings.DataDirSource } else { "既定値" }
if ($PSBoundParameters.ContainsKey('DataDir') -and -not [string]::IsNullOrWhiteSpace($DataDir)) {
    $dataDirInput = $DataDir
    $dataDirSource = "CLI"
}
elseif (-not [string]::IsNullOrWhiteSpace($env:DATA_DIR)) {
    $dataDirInput = $env:DATA_DIR
    $dataDirSource = "環境変数"
}

# 早期入力検証・解決・DATA_DIR 事前作成（イメージビルドおよび既存コンテナ停止より前に実行）
$resolvedMounts = Resolve-VideoMounts -VideoDirs $VideoDirs -OldVideoDir $env:VIDEO_DIR -TargetIsWindows $true -RepoRoot $repoRoot
$resolvedDataDir = Resolve-DataDir -Path $dataDirInput -RepoRoot $repoRoot -CreateIfMissing -ResolvedFrom $dataDirSource

# ポートの解決（既定ポート: 5620）
$rawPort = if (-not [string]::IsNullOrWhiteSpace($Port)) { $Port } else { "$($settings.Port)" }
$resolvedPort = Resolve-WebPort $rawPort

$containerName = "tag-based-video-manager"
$imageName = "tag-based-video-manager:latest"

Write-Host "=== TagBasedVideoManager デプロイ開始 ===" -ForegroundColor Cyan
Write-Host "ポート: $resolvedPort"
Write-Host "動画フォルダ: $VideoDirs"
Write-Host "DATA_DIR 設定元: $($resolvedDataDir.ResolvedFrom) (生値: '$($resolvedDataDir.RawInput)')"
if (-not [string]::IsNullOrWhiteSpace($resolvedDataDir.DistroName)) {
    Write-Host "DATA_DIR WSLディストロ: $($resolvedDataDir.DistroName)"
    $wslInnerDataDir = ConvertTo-WslPath -Path $resolvedDataDir.HostPath
    Write-Host "DATA_DIR WSL内パス: $wslInnerDataDir"
}
Write-Host "DATA_DIR HostPath: $($resolvedDataDir.HostPath)"
Write-Host "DATA_DIR MountSource: $($resolvedDataDir.MountSource)"

# 1. コンテナランタイム自動検出（既存コンテナ検査より前に実行）
$runtime = Get-ContainerRuntimeName -IsWindows $true
if (-not $runtime) {
    Write-Error "利用可能なコンテナランタイムが見つかりません。Windows 11 の場合は 'wsl --update' を実行して WSL 3.0 を有効化するか、Docker Desktop をインストールしてください。"
    exit 1
}
Write-Host "検出されたランタイム: $runtime" -ForegroundColor Green

# 2. 既存コンテナの /app/data マウント元変更チェック（ビルド・停止・削除より前に実行）
$existingDataSource = Get-ExistingContainerDataMount -Runtime $runtime -ContainerName $containerName
if ($null -ne $existingDataSource -and -not (Test-SameMountSource -ExistingSource $existingDataSource -NewSource $resolvedDataDir.MountSource)) {
    $escapedOldForJson = $existingDataSource.Replace('\', '\\')
    Write-Host (@(
        "警告: 既存コンテナ '$containerName' の /app/data マウント元が今回と異なります。"
        "  旧マウント元: $existingDataSource"
        "  新マウント先: $($resolvedDataDir.MountSource)"
        "※旧ディレクトリ内の既存データ（metadata.db / thumbnails）は削除されず、旧パス側にそのまま残存しています。"
        "  - 旧データをそのまま使い続ける場合: src/TagBasedVideoManager/appsettings.Production.json の `"DATA_DIR`" に旧パスを設定してください（例: `"DATA_DIR`": `"$escapedOldForJson`"）。"
        "  - 新ディレクトリへ既存データを移行する場合: デプロイ後または停止状態で以下を実行してください:"
        "    Copy-Item -Path `"$existingDataSource\metadata.db`" -Destination `"$($resolvedDataDir.HostPath)\`" -Force"
        "    Copy-Item -Path `"$existingDataSource\thumbnails`" -Destination `"$($resolvedDataDir.HostPath)\`" -Recurse -Force"
    ) -join [Environment]::NewLine) -ForegroundColor Yellow
}

# 3. ポート事前衝突チェック
if (Test-PortInUse -Port $resolvedPort) {
    Write-Warning "ポート $resolvedPort は既に他のプロセスで使用されています。"
}

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
$runArgs = New-RunArguments -Name $containerName -Port $resolvedPort -VideoMountArgs $resolvedMounts.VolumeArgs -ContainerVideoDirs $resolvedMounts.ContainerVideoDirs -DataDir $resolvedDataDir.MountSource -HasNvidiaGpu $hasNvidia -IsLinux $false -HasDri $false -Runtime $runtime
$allArgs = @($runArgs) + @($imageName)
& $runtime @allArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "コンテナの起動に失敗しました。"
    exit 1
}

# 8. ヘルスチェック (最大30秒)
Write-Host "--- ヘルスチェック実行中 (ポート $resolvedPort) ---" -ForegroundColor Cyan
# wslc は IPv4 (127.0.0.1) にのみバインドするため、IPv6 優先によるタイムアウトを防ぐため明示的に 127.0.0.1 を指定する
$healthUrl = "http://127.0.0.1:${resolvedPort}/"
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
