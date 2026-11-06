# Tasks: remove-obsolete-docker-compose

## 1. 不要な Docker Compose 関連資産の廃止とエラー型統一

- [ ] 1.1 [`qa-tester`] `test_plan.md`（`IT-CLEAN-01`〜`04`, `UT-STATE-ERR-01`〜`03`）に基づき、廃止対象ファイル（`docker-compose.yml`, `DockerController.fs`, `DockerControllerTests.fs`）の不在、`RenamerError` からの `DockerError` バリアント廃止、`State.update` の `DockerCommandCompleted` エラーハンドリング、および `Dockerfile` / `README.md` からの `docker-compose` 参照除去を検証する先行 Red テストを `DeployScriptTests.fs` および `StateTests.fs` に追加し、失敗（Red）を確認する
- [ ] 1.2 [`pg-implementer`] `docker-compose.yml`、`src/TagBasedVideoManager.Renamer/DockerController.fs`、`test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs` を削除し、各 `.fsproj` からの登録解除、`Domain.fs` / `State.fs` からの `DockerError` 除去、および `Dockerfile`・`README.md` の記述更新（Windows WSL3 `deploy.ps1` / Linux `deploy.sh` への一本化・見出し番号 `(A)` や `-EnableGpu` 表記の整理含む）を実施して全テストを Green 化する

## 2. 品質検証・カバレッジ確認・フェーズゲート検証

- [ ] 2.1 全テスト・C0/C1/C2 カバレッジ・エビデンス出力（`pwsh ./test/TagBasedVideoManager.Renamer.Tests/run_tests_with_coverage.ps1` および Web アプリテスト）、自動フォーマット、サブエージェント批判的レビュー（`pg-reviewer` / `qa-reviewer`）、および `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName remove-obsolete-docker-compose` の PASS を確認する
