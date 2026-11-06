# Spec Delta: companion-docker-control

## Purpose

Desktop CompanionアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（Running / Stopped / Error）を `docker compose ps --format json` および HTTPヘルスチェックにより特定・監視し、アプリ内から直接 `docker compose` コマンドを発行して起動・停止・再起動を行える機能を提供する。

## ADDED Requirements

### Requirement: Dockerコンテナ稼働状態の特定と監視

システムは、プロジェクトの `docker-compose.yml` を基点として `docker compose ps --format json` を実行し、サービス `video-manager`（コンテナ名 `tag-based-video-manager`）の稼働状態を特定した上で、ポート5620へのHTTP疎通確認と併せてステータスインジケーター（Running / Stopped / Unhealthy 等）をリアルタイムに表示しなければならない（SHALL）。

#### Scenario: コンテナ正常稼働の特定と表示

- **WHEN** TagBasedVideoManagerコンテナが起動し、ポート5620がHTTP応答しているとき
- **THEN** デスクトップアプリのステータスバーに「● RUNNING」およびポート番号が表示されること

#### Scenario: コンテナ停止または異常の特定

- **WHEN** コンテナが停止しているか、マウント失敗等で起動していないとき
- **THEN** デスクトップアプリのステータスバーに「○ STOPPED」または「▲ UNHEALTHY」と表示されること

### Requirement: Docker Compose コマンドのGUI実行

システムは、GUI上のボタン操作によって `docker compose up -d`、`docker compose down`、`docker compose restart` を非同期実行し、実行結果やログ出力をユーザーに通知しなければならない（SHALL）。

#### Scenario: コンテナの再起動実行

- **WHEN** ユーザーが「再起動」ボタンをクリックしたとき
- **THEN** バックグラウンドでコンテナ再起動が実行され、完了後にステータスが更新されること

### Requirement: リネーム連動コンテナ再起動

システムは、ファイルリネーム実行完了後に、自動または確認ダイアログを経てDockerコンテナの再起動（`docker compose restart`）を一連のフローとして実行できなければならない（SHALL）。

#### Scenario: リネーム後のワンクリック復旧

- **WHEN** ユーザーが「リネームしてコンテナ再起動」を実行したとき
- **THEN** 物理ファイルのリネームが正常完了した直後にコンテナ再起動が自動実行され、最新のファイル状態がコンテナ側に反映されること
