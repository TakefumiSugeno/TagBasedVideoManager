# 申し送り事項 (Handover): remove-obsolete-docker-compose

- **変更名**: `remove-obsolete-docker-compose`
- **日付**: 2026-11-06
- **ブランチ**: `refactor/remove-obsolete-docker-compose`
- **関連 Issue / PR**: なし（Archive 完了後に PR 作成）

---

## 1. 変更サマリ

WSL 3.0 (`wslc`) および単一コンテナデプロイスクリプト（Windows 用 `scripts/deploy.ps1`、Linux 用 `scripts/deploy.sh`）とマルチランタイム対応 `ContainerController.fs` への移行完了に伴い、互換性のために残存していたレガシーな Docker Compose 関連資産（`docker-compose.yml`、未使用の `DockerController.fs` / `DockerControllerTests.fs`、および未使用エラーケース `RenamerError.DockerError`）を完全に削除・整理した。
同時に、Windows（WSL 3.0 `wslc` / `docker`）だけでなくホスト OS が Linux（`docker` / `podman`）の環境でも本アプリおよびデスクトップコンパニオンアプリが引き続き完全動作するよう、`Dockerfile`・`scripts/deploy.sh`・`ContainerController.fs` を維持し、`Dockerfile` ヘッダーコメントおよび `README.md` の起動手順を単一コンテナ運用へ一本化した。

### 原因と対応（不具合修正の場合）

- **原因**: リファクタリング変更のため該当なし（WSL 3.0 移行後の旧 Docker Compose 資産およびデッドコードの整理）
- **対応**:
  1. `docker-compose.yml`、`src/TagBasedVideoManager.Renamer/DockerController.fs`、`test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs` の削除および各 `.fsproj` からの登録解除
  2. `src/TagBasedVideoManager.Renamer/Domain.fs` および `src/TagBasedVideoManager.Renamer/State.fs` から未使用の `RenamerError.DockerError` を削除し、`ContainerError` に統一
  3. `Dockerfile` ヘッダーコメントおよび `README.md` から Docker Compose セクション `(B)`・見出し番号 `(A)`・存在しない `-EnableGpu` フラグ表記を整理し、Windows (`scripts/deploy.ps1`) / Linux (`scripts/deploy.sh`) の単一コンテナ手順に統一
  4. `DeployScriptTests.fs`（`IT-CLEAN-01`〜`04`）および `StateTests.fs`（`UT-STATE-ERR-01`〜`03`）に回帰テストを追加

---

## 2. 品質・テストエビデンス

- **単体・結合テスト (Renamer)**: `pwsh ./test/TagBasedVideoManager.Renamer.Tests/run_tests_with_coverage.ps1` → 295 件 Pass（失敗 0 / スキップ 0）
- **単体・結合テスト (Web)**: `dotnet run --project test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj -- --filter-not-class "*E2ETests*"` → 65 件 Pass（失敗 0 / スキップ 0）
- **カバレッジ**: Line (C0) **84.61%**（目標 80%+ 達成） / Branch (C1) **64.42%** / Method **62.65%**（今回の変更差分 `State.fs` `DockerCommandCompleted` および `Domain.fs` `RenamerError` は C0/C1/C2 100% 網羅）
- **エビデンス出力先**: `test/TagBasedVideoManager.Renamer.Tests/TestResults/TestRun_2026-11-06_12_09_56/`（`TestRun_2026-11-06_12_09_56_net10.0.trx`, `CoverageReport/index.html`）

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）       | 内容                                                                                                                                                                                                 | 保留理由                                                                                                                                 | 推奨対応時期                                                                                                                          |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Propose / `user-reviewer` (Minor)    | `openspec/specs/companion-docker-control/spec.md` の `## Purpose` セクションに残る `docker compose ps --format json` / `docker compose` の文言更新                                                   | OpenSpec のデルタ仕様は `## MODIFIED Requirements` 単位で記述するため、メイン仕様の `## Purpose` は Archive 時の仕様同期で合わせて更新可 | Archive 仕様同期時または次回仕様改訂                                                                                                  |
| Apply / Task 1.1 / `qa-reviewer`     | `StateTests.fs` の `UT-STATE-ERR-01`〜`03` において、返却された `Cmd<Msg>` に対し `cmd.IsEmpty                                                                                                       | > should equal false`のみを検証しており、ディスパッチメッセージが`CheckDockerStatus` かまでは未検証                                      | Elmish の `Cmd<'Msg>` 型は内部が関数リスト `(Dispatch<'Msg> -> unit) list` であり、非空チェックでコマンド発行有無を担保する設計のため | 将来テストヘルパーを拡充する際 |
| Apply / Task 1.2 / `pg-reviewer` (1) | `State.fs` L608-616 の `DockerCommandCompleted` における状態更新レコード生成の重複削減、`ContainerError` 時の `exitCode` 表示、およびワイルドカード分岐の `"Docker エラー:"` プレフィックスの統一    | 既存の全テストおよび仕様要件を満たしており機能上の不具合はないため                                                                       | 次回 `State.fs` リファクタリング時                                                                                                    |
| Apply / Task 1.2 / `pg-reviewer` (2) | `State.fs` L571 の `ExecuteUndo` 内におけるコンテナ再起動結果（`let! _ =`）のエラー通知対応、および `ContainerController.fs` L131-132 / L183 の旧 `"Service"` / `"video-manager"` フォールバック整理 | 本変更前から存在する既存実装であり、今回の変更スコープ外であるため                                                                       | 次回コンテナ制御機能改修時                                                                                                            |

---

## 4. 将来の改善・リファクタ候補

- `State.fs` の `DockerCommandCompleted` において、エラーメッセージ文字列の生成のみを内部 `match` で行い、`{ model with IsDockerBusy = false; ErrorMessage = Some errMsg }, Cmd.ofMsg CheckDockerStatus` を 1 箇所にまとめることでコード量を約 30% 削減可能。また、`ContainerError (cmd, exitCode, stderr)` の UI 表示に `exitCode` を含めることで、空の `stderr` が返った場合の診断性を向上できる。
- `State.fs` の `ExecuteUndo`（L571）で Undo 完了後のコンテナ自動再起動が失敗した場合に、リネーム本体の `ExecuteRename`（L534-538）と同様に警告メッセージ（`Undo は完了しましたが、コンテナの再起動に失敗しました`）を表示する改善。
- `ContainerController.parseContainerPsOutput` 内に残る旧 Compose 互換の `"Service"` プロパティ参照および `"video-manager"` 部分一致フォールバックの整理。

---

## 5. 運用上の注意事項

- **デプロイ手順**:
  - Windows (WSL 3.0 `wslc` または `docker`): `pwsh ./scripts/deploy.ps1 -VideoDir "C:\Videos"`（NVIDIA GPU 使用時は `-Gpu nvidia`）
  - Linux (`docker` または `podman`): `./scripts/deploy.sh --video-dir /path/to/videos`（GPU 使用時は `--gpu amd` または `--gpu nvidia`）
- **Windows 環境での改行コード注意**:
  - `scripts/deploy.sh` は Linux 実行用シェルスクリプトであり、`DeployScriptTests.fs` の `IT-SCRIPT-02` で LF 改行コード（CRLF 不在）が検証されるため、Windows 環境でチェックアウト・編集する際も LF を維持すること。
- **ロールバック手順**:
  - データベーススキーマや設定ファイルの破壊的変更は一切含まないため、万が一問題が発生した場合は `git revert` で当該 PR のコミットを取り消すだけで安全に旧状態へロールバック可能。
