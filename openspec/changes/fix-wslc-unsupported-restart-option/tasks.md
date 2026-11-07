# Tasks: WSL 3.0 (wslc) における未サポートの --restart オプション除外

- **Change**: `fix-wslc-unsupported-restart-option`
- **Level**: L1

## 1. 再現テストと実装（TDD）

- [ ] 1.1 根本原因の特定と再現テスト（Red）の作成
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` の `RunArgumentsScriptTests` モジュールに `UT-RUN-ARGS-WSLC: wslc 指定時は --restart オプションを含めない` を追加。
  - テストが失敗すること（Red）を確認。
- [ ] 1.2 プロダクションスクリプトの修正（Green）
  - `scripts/common.ps1` の `New-RunArguments` に `[string]$Runtime = ""` パラメータを追加し、`wslc` の場合に `--restart` を除外する。
  - `scripts/deploy.ps1` で `New-RunArguments` を呼び出す際に `-Runtime $runtime` を付与する。
  - 再現テストおよび既存テストが PASS すること（Green）を確認。
- [ ] 1.3 既存テストスイートの実行と回帰確認
  - `DeployScriptTests` の全ケースを実行し、リグレッションがないことを確認。

## 2. 検証と完了

- [ ] 2.1 実機環境での `scripts/deploy.ps1` 起動引数確認
  - `deploy.ps1` によるコンテナ起動引数に `--restart` が渡されないことを確認。
