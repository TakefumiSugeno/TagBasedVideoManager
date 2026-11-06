# companion-docker-control Specification

## Purpose

Desktop CompanionアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（Running / Stopped / Error）を `docker compose ps --format json` および HTTPヘルスチェックにより特定・監視し、アプリ内から直接 `docker compose` コマンドを発行して起動・停止・再起動を行える機能を提供する。

## Requirements

### Requirement: Dockerコンテナ稼働状態の特定と監視

システムは、利用可能なコンテナランタイム（Windows では `wslc` または `docker`、Linux では `docker` または `podman`）の CLI（`ps --format json` 等）およびポート 5620 への HTTP ヘルスチェックを用いて、TagBasedVideoManager コンテナ（`tag-based-video-manager`）の稼働状態（Running / Stopped / Unhealthy / Not Installed 等）を特定・リアルタイム表示しなければならない（SHALL）。ランタイム自体が検出できない場合は、起動不能である旨をユーザーに明示しなければならない。

#### Scenario: コンテナ正常稼働の特定と表示

- **WHEN** TagBasedVideoManager コンテナが起動し、ポート 5620 が HTTP 応答しているとき
- **THEN** デスクトップアプリのステータスバーに「● RUNNING」およびポート番号が表示されること

#### Scenario: コンテナ停止または異常の特定

- **WHEN** コンテナが停止しているか、マウント失敗等で起動していないとき
- **THEN** デスクトップアプリのステータスバーに「○ STOPPED」または「▲ UNHEALTHY」と表示されること

#### Scenario: ランタイム未導入時の状態表示

- **WHEN** ホスト上に `wslc`, `docker`, `podman` のいずれも検出できないとき
- **THEN** デスクトップアプリのステータスバーに「✕ NO RUNTIME」と表示され、ランタイムの導入案内が確認できること

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

### Requirement: リネーム連動コンテナ再起動

システムは、ファイルリネーム実行完了後に、自動または確認ダイアログを経てコンテナの再起動（`restart`）を一連のフローとして実行できなければならない（SHALL）。再起動に失敗した場合はリネーム自体の成功を維持しつつエラーを通知しなければならない。

#### Scenario: リネーム後のワンクリック復旧

- **WHEN** ユーザーが「リネームしてコンテナ再起動」を実行したとき
- **THEN** 物理ファイルのリネームが正常完了した直後にコンテナ再起動が自動実行され、最新のファイル状態がコンテナ側に反映されること

#### Scenario: 連動再起動失敗時の通知

- **WHEN** ファイルリネーム成功後にコンテナ再起動が失敗したとき
- **THEN** ファイルリネームは成功した旨と、コンテナ再起動のみ失敗した旨のエラーメッセージを表示し、手動再起動を促すこと
