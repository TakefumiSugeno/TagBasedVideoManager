# Spec Delta: companion-docker-control

## Purpose

WinUI 3デスクトップアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（Running / Stopped / Error）を常時監視し、アプリ内から直接 `docker compose` コマンドを発行して起動・停止・再起動を行える機能を提供する。

## ADDED Requirements

### Requirement: Dockerコンテナ稼働状態の監視

システムは、TagBasedVideoManagerのコンテナ状態（Running / Stopped / Restarting 等）を非同期に取得し、ステータスインジケーター（色とラベル）としてリアルタイムに表示しなければならない（SHALL）。

#### Scenario: コンテナが正常稼働中の表示

- **WHEN** TagBasedVideoManagerコンテナが稼働しているとき
- **THEN** デスクトップアプリのステータスバーに「● RUNNING」およびポート番号が表示されること

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
