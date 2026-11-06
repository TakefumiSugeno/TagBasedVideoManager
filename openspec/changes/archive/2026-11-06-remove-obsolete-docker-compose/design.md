# Design: WSL3・単一コンテナ移行に伴う不要な Docker Compose 関連資産の削除

## Context

直前の変更（`wsl3-and-devcontainer-support`）により、本システムのコンテナ運用および Desktop Companion アプリ（`TagBasedVideoManager.Renamer`）のコンテナ制御は以下の単一コンテナ OCI 基盤へ移行完了しています。

- **Windows 11 環境**: `scripts/deploy.ps1`（`scripts/common.ps1`）および `ContainerController.fs` が WSL 3.0 の `wslc.exe`（未導入時は `docker`）を自動検出し、`Dockerfile` から単一コンテナ `tag-based-video-manager` をビルド・起動・制御する。
- **Linux ホスト環境**: `scripts/deploy.sh` および `ContainerController.fs` が `docker`（未導入時は `podman`）を自動検出し、`Dockerfile` から単一コンテナ `tag-based-video-manager`（NVIDIA `--gpus all` または AMD `/dev/dri` 自動パススルー含む）をビルド・起動・制御する。

一方、移行前の資産として以下が残存しており、設定の二重管理およびデッドコードとなっています。

1. `docker-compose.yml`: 単一コンテナ構成でありながら Compose 定義が二重に存在し、マウントパスや GPU 設定コメントが `scripts/deploy.ps1` / `scripts/deploy.sh` と重複している。
2. `src/TagBasedVideoManager.Renamer/DockerController.fs` および `test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs`: `docker compose ps --format json` や `docker compose <action>` を呼び出す旧モジュール。既に `State.fs` からは一切参照されていない。
3. `RenamerError.DockerError`: `Domain.fs` に定義され `State.fs` の `DockerCommandCompleted` でパターンマッチされているが、`ContainerController.fs` は `ContainerError` / `TimeoutError` / `ValidationError` のみを返すため到達不能なバリアントとなっている。
4. `Dockerfile` コメントおよび `README.md`: `docker-compose.yml` や `docker compose up -d --build` の記述が残存している。

## Goals / Non-Goals

**Goals:**

- `docker-compose.yml`、`DockerController.fs`、`DockerControllerTests.fs`、および `RenamerError.DockerError` を完全に削除し、デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）と `ContainerController.fs` による単一コンテナ構成へ一本化する。
- Windows (WSL 3.0 `wslc` / `docker`) および Linux ホスト (`docker` / `podman`) の双方で本アプリ（Web アプリ・Desktop Companion・DevContainer）が一切の機能劣化なく動作することを保証する。
- `Dockerfile` コメント、`README.md`、および OpenSpec 仕様書の記述を実態に合わせて同期する。

**Non-Goals:**

- UI/UX の変更（画面レイアウトや操作フローに変更はないため `mockup.html` は対象外）。
- `Dockerfile`、`.devcontainer/`、`scripts/deploy.ps1`、`scripts/deploy.sh`、`scripts/common.ps1`、`ContainerController.fs` のランタイムサポート縮小（Linux ホストの `docker` / `podman` 対応および Windows の `wslc` / `docker` 対応は完全に維持する）。

## Decisions

### 1. アーキテクチャと責務分離の整理

```text
+-------------------------------------------------------------------------+
|                 UNIFIED SINGLE-CONTAINER ARCHITECTURE                   |
+-------------------------------------------------------------------------+
|                                                                         |
|  [Deployment Layer]                                                     |
|    Windows : scripts/deploy.ps1 + scripts/common.ps1 (wslc -> docker)   |
|    Linux   : scripts/deploy.sh                       (docker -> podman) |
|       |                                                                 |
|       +--> Builds & runs root `Dockerfile` as `tag-based-video-manager` |
|                                                                         |
|  [Desktop Companion Control Layer]                                      |
|    State.fs ---> ContainerController.fs                                 |
|                    |-- detectContainerRuntime (Win: wslc/docker,        |
|                    |                           Linux: docker/podman)    |
|                    |-- checkStatus (`<runtime> ps -a --format json`)    |
|                    +-- executeAction (`<runtime> start|stop|restart`)   |
|                          |                                              |
|                          +--> Result<string, RenamerError>              |
|                               (ContainerError / TimeoutError /          |
|                                ValidationError)                         |
+-------------------------------------------------------------------------+
```

- **判断**: `DockerController.fs` を段階的廃止のまま残すのではなく、物理ファイルごと削除し `.fsproj` から除外する。
- **理由**: `State.fs` の全コンテナ操作は既に `ContainerController.fs` に移行済みであり、未参照の `DockerController.fs` を残すとカバレッジ母数の歪みや誤参照リスクを生むため。

### 2. エラーハンドリングの統一 (`RenamerError`)

- **変更前 (`Domain.fs`)**:
  ```fsharp
  type RenamerError =
      | IoError of message: string * ex: exn option
      | SettingsError of message: string
      | OpenRouterError of statusCode: int * message: string
      | ContainerError of command: string * exitCode: int * stderr: string
      | DockerError of command: string * exitCode: int * stderr: string
      | TimeoutError of command: string
      | ValidationError of message: string
      | UndoConflictError of path: string * message: string
  ```
- **変更後 (`Domain.fs` / `State.fs`)**:
  - `DockerError` ケースを削除し、コンテナ CLI 実行エラーはすべて `ContainerError of command: string * exitCode: int * stderr: string` で表現する。
  - `State.fs` の `DockerCommandCompleted` ハンドラから `Error (DockerError ...)` 分岐を削除し、`Error (ContainerError ...)`、`Error (TimeoutError ...)`、`Error (ValidationError ...)` / `Error other` で網羅的に処理する。

### 3. ドキュメントおよび `Dockerfile` コメントの更新方針

- **`Dockerfile`**:
  - 先頭コメントの `docker-compose.yml のコメントを参照してください`・`GPUリソースの付与は docker-compose.yml 側で行います` を、`scripts/deploy.ps1`（Windows / WSL 3.0）および `scripts/deploy.sh`（Linux）による自動付与の説明へ更新する。
- **`README.md`**:
  - 「動作環境」のコンテナ環境表記を `WSL 3.0 (wslc) / Docker / Podman`（Docker Compose 削除）に更新する。
  - 「3. コンテナ起動 (Web アプリ)」から「(B) Docker Compose を使用する場合」セクションを削除し、対になっていた「(A)」の見出し番号や実在しない `-EnableGpu` オプション記述も整理の上、Windows (WSL 3.0 / Docker) 向け `scripts/deploy.ps1` および Linux (Docker / Podman) 向け `scripts/deploy.sh` の手順（GPU 自動判定の説明を含む）に一本化する。

### 4. タスク分解方針

変更規模（変更・削除 8 ファイル程度）を踏まえ、TDD 3権分立に従い以下の 2 フェーズに分解する:

1. **Task 1 (Red → Green → Refactor)**: `qa-tester` による廃止ファイル不在・ドキュメント整合・`State.update` の `ContainerError` ハンドリング検証テスト追加（Red） → `pg-implementer` による `docker-compose.yml`, `DockerController.fs`, `DockerControllerTests.fs`, `DockerError` の削除と `Dockerfile` / `README.md` の更新（Green）。
2. **Task 2 (Verification & Gate 2)**: 全単体・結合・E2E テスト実行、C0/C1/C2 カバレッジ計測、および `verify-gate.ps1` 検証。

## Risks / Trade-offs

- **リスク**: Linux ホスト利用者が `docker-compose.yml` 削除により起動方法が分からなくなる懸念。
  - **軽減策**: `scripts/deploy.sh` が Linux 上の `docker` / `podman` 自動検出、ボリュームマウント、NVIDIA/AMD GPU パススルー、ヘルスチェックをすべて内蔵しており、`README.md` に `./scripts/deploy.sh` の実行手順を明記することで、むしろ手動で `docker-compose.yml` を編集するより安全かつ簡単になる。
- **ロールバック方針**: 万一問題が発生した場合は `git revert` により該当コミットを取り消すだけで即座に復元可能（DB スキーマや永続化データ `./data` への変更は一切ない）。
