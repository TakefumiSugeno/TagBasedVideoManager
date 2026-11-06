# docker-infrastructure Specification Delta

## ADDED Requirements

### Requirement: Docker Compose 構成ファイルの非依存と単一コンテナ運用の一本化

システムは、`docker-compose.yml` を保持・参照することなく、Windows（WSL 3.0 `wslc` または `docker`）および Linux ホスト（`docker` または `podman`）において、`Dockerfile` とデプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）のみで単一コンテナのビルド・起動・GPU パススルー構成を完結させなければならない（SHALL）。

#### Scenario: リポジトリからの docker-compose.yml 廃止とドキュメント整合

- **WHEN** リポジトリのファイル構成および `Dockerfile`・`README.md` を検査したとき
- **THEN** リポジトリルートに `docker-compose.yml` が存在せず、`Dockerfile` のコメントおよび `README.md` の起動手順に `docker-compose.yml` や `docker compose` コマンドへの参照が含まれないこと

#### Scenario: Windows (WSL 3.0) および Linux ホスト双方でのデプロイ完結

- **WHEN** Windows 環境で `scripts/deploy.ps1`、または Linux ホスト環境で `scripts/deploy.sh` を参照・実行したとき
- **THEN** `docker-compose.yml` を一切必要とせず、Windows では `wslc`（または `docker`）、Linux では `docker`（または `podman`）を用いて `Dockerfile` からイメージをビルドし、単一コンテナを起動できること

## MODIFIED Requirements

### Requirement: マルチステージビルドと軽量ランタイム配信

システムは、`Dockerfile` において .NET 10 SDK（`mcr.microsoft.com/dotnet/sdk:10.0`）によるビルドステージと、フットプリントの小さい ASP.NET ランタイム（`mcr.microsoft.com/dotnet/aspnet:10.0`）による実行ステージの2ステージ構成を採用し、実行環境内に `ffmpeg` および `ffprobe` パッケージを自動導入しなければならない（SHALL）。

#### Scenario: コンテナイメージのビルド

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI（`wslc build` / `docker build` / `podman build`）が実行されたとき
- **THEN** SDKステージで F# Webアプリが Release ビルド・同封され、ランタイムステージに FFmpeg と共に配置された最小限のコンテナイメージが生成されること

### Requirement: ポート公開と環境変数注入

システムは、Kestrel Web サーバーのポート（既定: 5620）をホスト側にマッピング（`5620:5620`）し、必須環境変数（`PORT=5620`, `VIDEO_DIR=/app/videos`, `THUMBNAIL_DIR=/app/data/thumbnails`, `DATABASE_PATH=/app/data/metadata.db`）をコンテナ起動時に確実に注入しなければならない（SHALL）。

#### Scenario: 環境変数の注入と疎通確認

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI の `run` コマンドによりコンテナが起動したとき
- **THEN** ポート 5620 で HTTP サーバーが待機し、指定されたコンテナ内パスで DB や動画ディレクトリが初期化されること
