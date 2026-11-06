# companion-docker-control Specification Delta

## MODIFIED Requirements

### Requirement: Docker Compose コマンドのGUI実行

システムは、旧 `docker compose` サブコマンド体系（`DockerController` モジュールおよび `DockerError`）を使用せず、利用可能な単一コンテナ OCI ランタイム（Windows では `wslc` または `docker`、Linux では `docker` または `podman`）に対して直接 `start`、`stop`、`restart` コマンドを GUI 操作から非同期実行し、実行結果をユーザーに通知しなければならない（SHALL）。処理失敗時は `ContainerError`、`TimeoutError`、または `ValidationError` としてエラー内容を明示し、回復手段を提示しなければならない。

#### Scenario: コンテナの再起動実行

- **WHEN** ユーザーが「再起動」ボタンをクリックしたとき
- **THEN** バックグラウンドで単一コンテナ CLI（`<runtime> restart tag-based-video-manager`）による再起動が実行され、完了後にステータスが更新されること

#### Scenario: コマンド実行失敗時のエラー通知

- **WHEN** コンテナ制御コマンドが非ゼロの終了コードまたはタイムアウトで失敗したとき
- **THEN** アプリがフリーズすることなく `ContainerError` または `TimeoutError` に基づくエラーメッセージで失敗理由を表示し、再試行可能な状態を維持すること

#### Scenario: 旧 DockerController および DockerError の完全廃止

- **WHEN** デスクトップコンパニオンアプリのプロジェクト構成およびエラー型定義を検査したとき
- **THEN** `DockerController.fs` および `DockerControllerTests.fs` が存在せず、状態管理およびエラーハンドリングが `ContainerController` と `ContainerError` に一本化されていること
