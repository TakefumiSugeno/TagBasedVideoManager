# =============================================================================
# DevContainer Dockerfile for TagBasedVideoManager
# =============================================================================
FROM mcr.microsoft.com/devcontainers/dotnet:10.0

# 必要な追加ツールの導入 (FFmpeg, SQLite3, curl)
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive && apt-get install -y --no-install-recommends ffmpeg sqlite3 curl && apt-get clean && rm -rf /var/lib/apt/lists/*
