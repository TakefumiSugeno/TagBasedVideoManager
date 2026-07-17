# =============================================================================
# Dockerfile for TagBasedVideoManager
# =============================================================================
# ffmpegのGPUアクセラレーション対応について:
#   - CPU処理 (デフォルト): 変更不要。このファイルをそのまま使用してください。
#   - AMD GPU (ROCm/VAAPI): 変更不要。docker-compose.yml のコメントを参照してください。
#   - NVIDIA GPU (CUDA): 変更不要。docker-compose.yml のコメントを参照してください。
#
# ※ ffmpeg のハードウェアデコードオプション (-hwaccel auto) はアプリ内で
#    自動設定されます。GPUリソースの付与は docker-compose.yml 側で行います。
# =============================================================================

# 1. ビルドステージ
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# プロジェクトファイルをコピーして復元 (レイヤーキャッシュ最適化)
COPY ["src/TagBasedVideoManager.fsproj", "./src/"]
RUN dotnet restore "./src/TagBasedVideoManager.fsproj"

# ソースコードをコピーしてパブリッシュ
COPY src/ ./src/
WORKDIR /src/src
RUN dotnet publish "TagBasedVideoManager.fsproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. ランタイムステージ
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# FFmpeg および ffprobe のインストール
# - CPU処理 / AMD GPU (VAAPI) / NVIDIA GPU (CUDA) のいずれの場合も
#   標準の ffmpeg パッケージを使用します。
#   GPUアクセラレーションはホストからのデバイスマウントにより有効化されます。
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# ビルド成果物および静的ファイルのコピー
COPY --from=build /app/publish .

# 永続化およびデータマウント用ディレクトリの作成
RUN mkdir -p /app/videos /app/data/thumbnails

# 環境変数のデフォルト設定
ENV PORT=5620
ENV VIDEO_DIR=/app/videos
ENV THUMBNAIL_DIR=/app/data/thumbnails
ENV DATABASE_PATH=/app/data/metadata.db
EXPOSE 5620

ENTRYPOINT ["dotnet", "TagBasedVideoManager.dll"]
