# Tasks: WSL3 および DevContainer サポート

## 1. DevContainer 環境の構築 (devcontainer-environment)

- [ ] 1.1 `.devcontainer/Dockerfile` を作成し、.NET 10 SDK、FFmpeg、SQLite3、curl を導入する定義を記述する
- [ ] 1.2 `.devcontainer/devcontainer.json` を作成し、開発用ポート 5621 のフォワード、環境変数 `PORT=5621` の設定、および F# (Ionide) / C# DevKit 拡張機能の自動導入を定義する
- [ ] 1.3 DevContainer 設定の構文妥当性検証テスト（`IT-DEV-01`）を作成し、`forwardPorts` に 5621 が含まれ、`PORT` 環境変数が 5621 に設定されていることを検証する

## 2. デプロイスクリプトの構築 (container-deployment-scripts)

- [ ] 2.1 `scripts/deploy.ps1` を作成し、ポート 5620 事前衝突チェック、Windows 環境における `wslc` / `docker` 自動判別、未導入時ガイダンス、`.env` 読み込み、パス変換、イメージビルド、旧コンテナ停止・削除、新コンテナ起動（`./data:/app/data` ボリューム永続化）、およびヘルスチェック（失敗時 `logs` 出力）を実装する
- [ ] 2.2 `scripts/deploy.sh` を作成し、ポート 5620 事前チェック、Linux 環境における `docker` / `podman` 自動判別、ビルド、コンテナ再作成（ボリューム永続化）、およびヘルスチェック（失敗時 `logs` 出力）を実装する
- [ ] 2.3 PowerShell 構文解析器を用いた `scripts/deploy.ps1` の静的構文チェック（`IT-SCRIPT-01`）および `scripts/deploy.sh` の静的解析（`IT-SCRIPT-02`）をテストプロジェクトに追加し、構文エラー 0 件であることを確認する
- [ ] 2.4 パス変換ユーティリティ（Windows ↔ WSL）の単体テスト（`UT-PATH-01`〜`UT-PATH-04`）を作成し、正常に変換されることを検証する

## 3. Desktop Companion コンテナ制御の抽象化 (companion-docker-control)

- [ ] 3.1 【qa-tester】`ContainerController` に対する先行 Red テスト（`UT-CC-01`〜`UT-CC-09`, `UT-RUN-01`〜`UT-RUN-03`, `DT-01`〜`DT-04`）を `TagBasedVideoManager.Renamer.Tests` に作成し、意図通り失敗することを確認する
- [ ] 3.2 【pg-implementer】`src/TagBasedVideoManager.Renamer/ContainerController.fs` を実装し、`docker compose` 依存を解消して `wslc` / `docker` 単一コンテナ CLI（`ps`, `start`, `stop`, `restart`）による監視・制御、10秒タイムアウト・強制Kill、およびエラーログ出力ロジックを Green 化する
- [ ] 3.3 【pg-implementer】`TagBasedVideoManager.Renamer.fsproj` および `TagBasedVideoManager.Renamer.Tests.fsproj` のファイル参照を `DockerController.fs` から `ContainerController.fs` へ更新し、既存の `DockerControllerTests.fs` を `ContainerControllerTests.fs` へ移行する
- [ ] 3.4 【pg-implementer】Companion アプリの `Domain.fs`, `State.fs`, `Views.fs` の呼び出し箇所を `ContainerController` へ移行・リファクタリングし、`NotInstalled` 表示や失敗時エラー通知、リネーム連動再起動機能が正常動作することを検証する

## 4. ドキュメント整備と仕様同期

- [ ] 4.1 `README.md` および `docker-compose.yml` に WSL 3.0（`wslc`）による単一コンテナ運用と DevContainer による開発手順を追記する
- [ ] 4.2 `npx prettier --write "**/*.md" "**/*.json"` を実行してドキュメントと設定ファイルを自動フォーマットする

## 5. 統合検証とフェーズゲート確認

- [ ] 5.1 全単体テスト・結合テストを実行し、テスト結果 TRX ファイルおよびカバレッジレポート（C0 90%+, C1 85%+, C2 100%）を出力して完了基準を満たすことを確認する
- [ ] 5.2 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName wsl3-and-devcontainer-support` を実行し、Gate 2 が PASS することを確認する
