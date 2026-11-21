# Tasks: update-openspec-kit-and-skills

> **注記（L2 非コード `chore` 変更の実行方針）**: 本変更は `src/`・`test/` を変更しない運用基盤更新のため、`qa-tester` / `pg-implementer` によるコード実装 Red→Green サイクルは適用せず、作業者がタスク 1.1〜3.2 を実施した上で、タスク 3.3 にて独立した批判的レビュアー（`pg-reviewer`, `qa-reviewer`）によるレビューおよび Gate 2（`verify-gate.ps1 -Gate apply`）を通過させて完了・コミットする。

## 1. 事前確認・`AGENTS.md` 固有要素移設と `openspec-kit` 同期

- [ ] 1.1 事前前提確認（`IT-04`）と `AGENTS.md` 固有要素移設: 参照元 `D:\programming\repos\MyGitHubRepos\AIs\AIUtils\openspec-kit\install.ps1`・`openspec` CLI (`1.15.0`)・`pwsh`・`python`・`npx` を確認し、現行 `AGENTS.md` 管理区間内の「セマンティックバージョニング（SemVer 2.0.0）規則と運用（L3/L2/L1 連動表）」を管理区間外（103〜106 行目配下）へ移設して `## 利用可能なSkills` へ `sdd-*` を追記する（`CLAUDE.md` / `GEMINI.md` は無改変）
- [ ] 1.2 `install.ps1` ドライラン（`IT-01`）および本実行: `-TargetPath . -BaseBranch develop -EvidenceDir test/TestResults/ -AgentTargets auto -Force -SkipConfig -WhatIf` で対象が 4 エージェント（`.agents`, `.github`, `.opencode`, `.hermes`）かつ無書き込みであることを確認した上で `-WhatIf` なしで実行し、`openspec/{agents,templates,scripts}`・4 エージェントの `skills/sdd-*`・`AGENTS.md` 管理区間を更新する（途中失敗時は `git clean` を使わず `git status` 確認の上で `-Force` 再実行または限定パス復元で回復する）
- [ ] 1.3 退避された旧 `.agents/workflows/sdd-{propose,apply,archive}.md.bak*` を削除し（同ディレクトリの `opsx-*.md` は保持）、`.claude/` が未作成であることを確認する（`E2E-02`）

## 2. `openspec/config.yaml` 更新と `openspec update` 実行

- [ ] 2.1 `openspec/config.yaml` の固有設定（F#/Avalonia/Alpine 技術スタック、SemVer 2.0.0、`test/TestResults/`、HTML モック、`chore` 許可）を保持したまま `reviews.workflows` を `reviews.skills`（`.agents/skills/sdd-{propose,apply,archive}/SKILL.md`）へ更新し、`openspec instructions apply --change "update-openspec-kit-and-skills" --json` でエラーなく読み込まれることを確認する
- [ ] 2.2 `openspec update` を実行（サンドボックス内 `EPERM` 発生時は `openspec update` 単体のみ `BypassSandbox: true`）し、事前ドライラン確認済み 53 ファイル（`openspec-*` / `opsx-*`）を `1.15.0` へ更新した上で、再実行した `openspec update --dry-run --json` の `changes` が `[]`（差分 0 件）、`sdd-*` 保持、`.claude/` 未作成であることを確認する（`IT-02`）

## 3. 対象限定 Prettier 整形と最終検証・Apply レビュー・Gate 2

- [ ] 3.1 対象ファイル限定での Prettier 整形（`openspec/changes/archive/` 等の過去ファイルは対象外）: `AGENTS.md`・`openspec/{agents,templates}/*.md`・`openspec/changes/update-openspec-kit-and-skills/**/*.md`・4 エージェント配下の `skills/sdd-*/**/*.md` のみに `npx prettier --write` を実行する
- [ ] 3.2 `test_plan.md` 全検証ケース（`UT-01`〜`UT-06`, `DT-01`, `DT-02` 一時環境, `IT-01`〜`IT-04`, `E2E-01`〜`E2E-03` 一時環境）を**整形後の状態に対して**最終実行し、4 エージェント間の `sdd-*` SHA256 一致、全 8 個の `.py` 直接実行 `exit 2`、`AGENTS.md` の UTF-8 BOM なし・バイト数内訳（管理区間外 / 管理区間内 / 合計 `< 24,000` B）・固有規約＆SemVer 表保持、ならびに `verify-gate.ps1` / `verify-gate.sh` を確認する
- [ ] 3.3 `pg-reviewer` / `qa-reviewer` による Apply レビューを実施して `reviews.md` へ記録（`LGTM: true`）し、`openspec validate "update-openspec-kit-and-skills"` および `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "update-openspec-kit-and-skills"` が `PASSED` になることを確認してコミットする

## Workflow follow-up

- 全タスク完了後、4 ロール（`user/se/pg/qa-reviewer`）総括レビューと `handover.md` 作成（上流 `AIUtils` 向け改善候補の転記含む）、Gate 3 検証（`verify-gate.ps1 -Gate archive`）を経て `openspec archive "update-openspec-kit-and-skills"` を実行し、PR を作成する。
