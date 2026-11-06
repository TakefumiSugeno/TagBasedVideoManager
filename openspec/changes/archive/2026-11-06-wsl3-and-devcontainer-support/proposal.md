# Proposal: WSL3 および DevContainer サポート

- **Level**: L2（通常変更: 機能追加・仕様変更・開発基盤改善）
- **Type**: feat
- **Target Issue**: [#17 [機能改善]WSL3に対応させる](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/17)

## Why

Windows Subsystem for Linux 3.0（2026年9月 GA）において、Docker Desktop 等のサードパーティ製ツールなしに Linux コンテナをネイティブ実行可能な「WSL Containers (`wslc.exe`)」が正式提供されました。
本システムは単一コンテナ（SQLite + Kestrel + FFmpeg）で完結する設計であり、複数コンテナを前提とする Docker Compose への依存が不要となったため、Windows 11 標準機能である WSL 3.0 への最適化を行い、開発・運用の軽量化および DevContainer による環境構築ゼロの開発体験を実現します。

## What Changes

- **単一コンテナ OCI CLI 抽象化**: Windows 環境では WSL 3.0 の `wslc.exe`、Linux ホスト環境では `docker` または `podman` を自動判別して使用する共通 CLI 実行基盤を確立します。
- **デプロイスクリプトの追加 (`scripts/`)**: ソースコード変更後に「イメージ再ビルド → 旧コンテナ停止・破棄 → 新コンテナ起動 → ポート疎通確認」を 1 コマンドで安全に完了するスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）を提供します。
- **Desktop Companion アプリのコンテナ制御更新**: `TagBasedVideoManager.Renamer` 内の `DockerController.fs` を `ContainerController.fs` へリファクタリングし、`docker compose` コマンド依存を解消して `wslc` / `docker` の共通単一コンテナ CLI（`ps`, `start`, `stop`, `restart`）による監視・制御に対応させます。
- **DevContainer 開発環境の追加 (`.devcontainer/`)**: .NET 10 SDK、FFmpeg、SQLite3、F# (Ionide) / C# 拡張機能を同梱した DevContainer 構成を提供します。
- **ポート分離設計**: 本番常用コンテナ（ポート 5620）と DevContainer（ポート 5621）のポート競合を防止し、本番コンテナを常駐稼働させたまま DevContainer で安全に並行開発できる構成とします。

## Capabilities

### New Capabilities

- `devcontainer-environment`: .NET 10 SDK、FFmpeg、SQLite3 および VS Code 拡張機能（Ionide 等）を備えた開発用コンテナ環境を提供し、ホスト環境を汚さずに開発・テストを即座に実行可能とする。
- `container-deployment-scripts`: ソースコード変更後の再ビルド・停止・起動・ヘルスチェックを一括実行するクロスプラットフォーム（PowerShell / Bash）デプロイスクリプトを提供する。

### Modified Capabilities

- `docker-infrastructure`: `docker-compose.yml` 依存から、WSL 3.0 (`wslc`) および Docker による単一コンテナ直接実行・ボリューム永続化・NVIDIA GPU パススルー仕様へと拡張する。
- `companion-docker-control`: `docker compose` コマンド体系から、単一コンテナ OCI CLI（`wslc` / `docker`）の共通制御（`ps`, `start`, `stop`, `restart`）仕様へと更新する。

## Non-goals（やらないこと）

- `wslc` における AMD GPU（`/dev/dri`）の独自ハック・非公式パススルー対応（`wslc` の `--device` 非対応制約に従い、AMD GPU は CPU フォールバックとする）。
- Avalonia.FuncUI デスクトップアプリ自体の DevContainer 内 GUI 実行（デスクトップアプリはホスト OS 上での直接ビルド・実行を標準とする）。
- 外部 DB（PostgreSQL 等）やマルチコンテナ構成への拡張（本システムは単一コンテナ SQLite 構成を堅持する）。

## Acceptance Criteria（受け入れ基準）

- **Scenario: WSL 3.0 環境でのデプロイスクリプト実行**:
  - **GIVEN** Windows 11 / WSL 3.0 環境で `wslc` が利用可能であること
  - **WHEN** ユーザーが `scripts/deploy.ps1` を実行したとき
  - **THEN** `wslc` を自動検出し、コンテナイメージのビルド、既存コンテナの停止・更新、ポート 5620 での HTTP 疎通確認が正常に完了すること
- **Scenario: Linux ホスト環境でのデプロイスクリプト実行**:
  - **GIVEN** Linux ホスト環境で `docker` が利用可能であること
  - **WHEN** ユーザーが `scripts/deploy.sh` を実行したとき
  - **THEN** `docker` を自動検出し、Linux 環境上でビルドおよびコンテナ起動が正常に完了すること
- **Scenario: Companion アプリによる単一コンテナ制御**:
  - **GIVEN** `wslc` または `docker` で起動された単一コンテナが存在すること
  - **WHEN** Companion アプリが起動、またはリネーム完了時に再起動ボタンが押されたとき
  - **THEN** 単一コンテナ CLI 経由でコンテナ状態（Running 等）が取得され、再起動が正常に成功すること
- **Scenario: DevContainer での開発並行動作とポート分離**:
  - **GIVEN** ポート 5620 で本番コンテナが稼働中であること
  - **WHEN** VS Code で DevContainer を起動して Web アプリを実行したとき
  - **THEN** ポート衝突を起こさず、開発用ポート 5621 で Web アプリが待機し、ホストブラウザからアクセス可能であること

## Impact

- **デスクトップアプリ**: `src/TagBasedVideoManager.Renamer/DockerController.fs` のリファクタリング（`ContainerController.fs` への改称・実装更新）、およびこれに伴う `Domain.fs`, `State.fs`, `Views.fs` の参照更新。
- **スクリプト追加**: `scripts/deploy.ps1`, `scripts/deploy.sh` の新規追加。
- **開発設定追加**: `.devcontainer/devcontainer.json`, `.devcontainer/Dockerfile` の新規追加。
- **仕様書**: `openspec/specs/docker-infrastructure/spec.md` および `openspec/specs/companion-docker-control/spec.md` のデルタ仕様適用。
