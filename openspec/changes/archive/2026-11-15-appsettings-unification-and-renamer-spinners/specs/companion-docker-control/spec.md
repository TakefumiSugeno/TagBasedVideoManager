# companion-docker-control Delta Specification

## ADDED Requirements

### Requirement: コンテナ操作中のステータスバー小型スピナー表示

システムは、デスクトップコンパニオンアプリ上からコンテナ制御コマンド（`start` / `stop` / `restart`）が非同期実行されている間（`IsDockerBusy = true`）、上部コンテナステータスバーのステータスバッジ付近に小型の回転スピナーと処理中テキスト（例: `コンテナ操作中...`）を表示し、多重操作防止のため制御ボタン群を非活性（Disabled）としなければならない（SHALL）。コマンドが成功・失敗（`ContainerError` / `TimeoutError`）のいずれで完了した場合も、スピナーは確実に解除されなければならない（SHALL）。

#### Scenario: コンテナ再起動実行中の小型スピナー表示と完了後の解除

- **WHEN** ユーザーが上部バーの「🔄 Restart」または「リネームしてコンテナ再起動」を実行し `IsDockerBusy = true` となったとき
- **THEN** ステータスバーに小型スピナーと処理中表示が現れ、`Start` / `Stop` / `Restart` ボタンが非活性となり、処理完了またはタイムアウト時にスピナーが消えて最新ステータスバッジが表示されること
