# Proposal: WSL3・単一コンテナ移行に伴う不要な Docker Compose 関連資産の削除

- **Level**: L2（通常変更: 不要資産削除・リファクタリング・仕様同期）
- **Type**: refactor

## Why

WSL 3.0 (`wslc.exe`) および Linux ホストの単一コンテナ運用（`scripts/deploy.ps1`, `scripts/deploy.sh`, `ContainerController.fs`）への移行が完了したことで、複数コンテナ構成を前提とした `docker-compose.yml` および本番コードから参照されなくなった旧 `DockerController.fs`（`docker compose` 専用モジュール）が不要となりました。
これらが残存していると、設定・ドキュメントの二重管理やデッドコードによる保守コスト増大を招くため、Windows (WSL 3.0) と Linux ホスト（`docker` / `podman`）の双方で単一コンテナが動作する構成を維持しつつ、不要な Docker Compose 関連ファイル・コードを完全に削除してアーキテクチャを一本化します。

## What Changes

- **`docker-compose.yml` の廃止**: リポジトリルートの `docker-compose.yml` を削除し、コンテナのビルド・起動・GPU パススルー設定を `scripts/deploy.ps1`（Windows / WSL 3.0）および `scripts/deploy.sh`（Linux）へ完全に一本化します。
- **旧 `DockerController` モジュールとテストの廃止**: `docker compose` コマンド専用の旧実装である `src/TagBasedVideoManager.Renamer/DockerController.fs` および `test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs` を削除し、各 `.fsproj` からコンパイル対象登録を解除します。
- **エラー型の整理 (`DockerError` の廃止)**: `src/TagBasedVideoManager.Renamer/Domain.fs` の `RenamerError.DockerError` および `src/TagBasedVideoManager.Renamer/State.fs` の該当エラーハンドリング分岐を削除し、`ContainerError` に統一します。
- **Linux ホストおよび WSL 3.0 稼働基盤の維持**: `Dockerfile`、`.devcontainer/`、`scripts/deploy.ps1`、`scripts/deploy.sh`、`scripts/common.ps1`、および `ContainerController.fs`（Windows: `wslc` / `docker`、Linux: `docker` / `podman`）はそのまま維持し、Windows (WSL 3.0) と Linux ホストの両環境でアプリケーションが正常動作することを保証します。
- **ドキュメント・コメントの更新**: `Dockerfile` のヘッダコメントおよび `README.md` から `docker-compose.yml` / `Docker Compose` に関する記述を除去し、デプロイスクリプトによる単一コンテナ起動手順に統一します。

## Capabilities

### New Capabilities

（なし）

### Modified Capabilities

- `docker-infrastructure`: `docker compose build` および `docker compose up -d` への言及を廃止し、WSL 3.0 (`wslc`) および Linux/Windows の単一コンテナ CLI（`docker` / `podman`）・デプロイスクリプトによるビルドおよび起動仕様に統一する。
- `companion-docker-control`: Purpose および Requirement 見出しに残存していた `docker compose` 表記を廃止し、単一コンテナ OCI CLI（Windows: `wslc` / `docker`、Linux: `docker` / `podman`）による状態監視・操作仕様へ統一する。

## Non-goals（やらないこと）

- Linux ホスト環境および Windows フォールバック環境における `docker` / `podman` 単一コンテナ CLI サポートの削除（`ContainerController.fs` および `scripts/deploy.sh`, `scripts/deploy.ps1`, `scripts/common.ps1` のマルチランタイム対応は維持する）。
- ルートの `Dockerfile` や `.devcontainer/`（`devcontainer.json`, `Dockerfile`）の削除（これらは `wslc build` / `docker build` および VS Code DevContainer 開発環境で必須のため維持する）。
- Web アプリケーション本体（`src/TagBasedVideoManager/`）のロジック変更。

## Acceptance Criteria（受け入れ基準）

- **Scenario: 不要な Docker Compose 関連ファイルの完全削除**:
  - **GIVEN** リポジトリが単一コンテナ運用（`scripts/deploy.ps1`, `scripts/deploy.sh`, `ContainerController.fs`）へ移行済みであること
  - **WHEN** リポジトリ内のファイル構成およびビルド構成を確認したとき
  - **THEN** `docker-compose.yml`, `src/TagBasedVideoManager.Renamer/DockerController.fs`, `test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs` が存在せず、ソリューション全体のビルドと全テストがエラーなく成功すること
- **Scenario: Windows (WSL 3.0) および Linux ホスト両環境の単一コンテナ稼働維持**:
  - **GIVEN** `docker-compose.yml` と `DockerController.fs` が削除された状態であること
  - **WHEN** `ContainerController` のランタイム検出・コンテナ制御テスト、および `scripts/deploy.ps1` / `scripts/deploy.sh` の検証テストを実行したとき
  - **THEN** Windows 環境（`wslc` / `docker`）および Linux 環境（`docker` / `podman`）の双方でコンテナのビルド・起動・状態監視・再起動が従来通り正常に機能すること
- **Scenario: ドキュメントおよび Dockerfile コメントの整合性**:
  - **GIVEN** `Dockerfile` および `README.md` を参照したとき
  - **WHEN** コンテナ起動手順や GPU 設定案内を確認したとき
  - **THEN** 削除済みの `docker-compose.yml` や `docker compose` コマンドへの参照が存在せず、`scripts/deploy.ps1` および `scripts/deploy.sh` による手順が案内されていること

## Impact

- **削除対象ファイル**:
  - `docker-compose.yml`
  - `src/TagBasedVideoManager.Renamer/DockerController.fs`
  - `test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs`
- **変更対象ファイル**:
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`
  - `src/TagBasedVideoManager.Renamer/Domain.fs`
  - `src/TagBasedVideoManager.Renamer/State.fs`
  - `test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj`
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`（廃止ファイル不在・ドキュメント整合検証テストの追加）
  - `Dockerfile`
  - `README.md`
  - `openspec/specs/docker-infrastructure/spec.md`（デルタ仕様）
  - `openspec/specs/companion-docker-control/spec.md`（デルタ仕様）
