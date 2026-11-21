---
name: sdd-archive
description: "OpenSpec 変更の完了（Archive）フェーズを、完了基準（DoD）の最終検証・handover.md 作成・4 ロール（user/se/pg/qa-reviewer）総括レビュー・Gate 3 検証・openspec archive 実行・PR 自動作成付きで実行する。全タスク完了後のアーカイブ、openspec archive、sdd-archive、変更の完了処理や PR 作成を依頼されたときに使う。"
---

# SDD Archive（Definition of Done & Gate 3）

すべての実装・テストタスク完了後、**完了基準（Definition of Done）の最終確認・4 ロール総括レビュー・`handover.md` 作成・機械的ゲート検証（Gate 3）・PR 自動作成（人間レビューは PR 上で実施）** を経て変更を完了させる、5 エージェント（Antigravity CLI / Claude Code / GitHub Copilot CLI / OpenCode CLI / Hermes Agent）共通のスキルです。

- 詳細リファレンス（6 ロール表・カバレッジ基準・ゲート表・Git 操作・CLI）: [references/sdd-reference.md](references/sdd-reference.md)
- Antigravity CLI 実行時の専用手順（モデル指定・権限分離・並列実行・障害回復）: [references/antigravity-orchestration.md](references/antigravity-orchestration.md)

## 実行環境とサブエージェント分業（フォールバック）

本スキルの手順は特定 CLI 専用ツールを必須前提としません。総括レビューは、作成者とは別の独立コンテキストで行います。

| 実行環境               | 分業方式                                                                                                                                                       |
| :--------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Antigravity CLI        | [references/antigravity-orchestration.md](references/antigravity-orchestration.md) に従い、ロール別モデル・権限を指定したサブエージェントで実行する            |
| Claude Code            | `.claude/agents/` に展開された 6 ロール（`openspec/agents/*.md` と同一内容）を標準サブエージェントとして起動する                                               |
| GitHub Copilot CLI     | 標準のカスタムエージェント／サブエージェント機構があれば `openspec/agents/<role>.md` を指示として起動する                                                      |
| OpenCode CLI / Hermes  | 各エージェント標準のサブエージェント（subagent）機構があれば `openspec/agents/<role>.md` を指示として起動する                                                  |
| 上記機構が使えない環境 | **フォールバック**: 新規セッション（独立コンテキスト）を開き、ロール定義の本文を指示として読ませ、対象成果物のみを渡して 4 ロールを 1 つずつ**直列**に実行する |

どの方式でも次の原則を守ります。

- **独立コンテキスト**: レビュアーに作成者の思考過程を渡さず、対象成果物・`git diff`・チェックリストのパスのみを渡す。
- **単一ライター**: `reviews.md` と `tasks.md`・`handover.md` を編集するのは本スキルを実行している親（メイン）エージェントのみ。サブエージェントは `review_record.md` 形式のテキストを返却する。
- Antigravity 専用ツール（`define_subagent` / `run_workflow` / `swarm`）が無い環境でも、本ファイルの手順だけで完遂できる。

## ステップ 1: 完了基準（Definition of Done）の最終検証

1. 自動フォーマットおよび静的解析（リンター）・ビルドを実行し、差分 0・エラー 0・新規警告 0 を確認する。
2. 全テスト（単体・結合・E2E）を実行し、最新のテストレポート・カバレッジレポート・画面キャプチャを `test/TestResults/` に出力する:
   - 全テスト **100% Pass**（`Skip` / `Ignore` 放置 0 件）
   - **C0（命令網羅）**: 全体 80%以上 / 変更差分 90%以上
   - **C1（分岐網羅）**: 全体 80%以上 / 変更差分 85%以上
   - **C2（条件網羅）**: 複合条件デシジョンテーブル 100%
   - **Scenario 網羅率**: `specs/**/spec.md` の全 Scenario 100%
3. `README.md` や関連ドキュメントに変更内容の同期が必要であれば更新する。

## ステップ 2: `handover.md` の作成

`openspec/templates/handover.md` をもとに `openspec/changes/<change-name>/handover.md` を作成し、以下を漏れなく記録する。

- 変更サマリ（What & Why、バグ修正時は根本原因と対応）
- テスト結果サマリ（単体/結合/E2E 件数、C0/C1/C2 カバレッジ率、エビデンス出力先パス）
- Apply フェーズで「保留」とした全指摘事項の転記（ない場合は「なし」）
- 将来の改善・リファクタ候補および運用上の注意事項

## ステップ 3: 全 4 ロールによる Archive 総括レビュー（最大 3 サイクル）

`openspec/templates/checklist_archive.md` を用い、4 つのレビュアー（`user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer`）による総括レビューを実施する。

- 各ロールの定義: `openspec/agents/user-reviewer.md`, `openspec/agents/se-reviewer.md`, `openspec/agents/pg-reviewer.md`, `openspec/agents/qa-reviewer.md`
- 各レビュアーには変更名・対象成果物（`openspec/changes/<change-name>/` 配下と `git diff`）・チェックリストのパスを渡し、`openspec/templates/review_record.md` 形式のテキストで返却させる（レビュアー自身は `reviews.md` を編集しない）。
- Antigravity CLI では `scripts/run_archive_review.py` を `run_workflow` で実行すると 4 ロールを並列起動できる（手順: [references/antigravity-orchestration.md](references/antigravity-orchestration.md)）。他環境では 4 ロールを順に直列実行する。
- 親エージェントが返却テキストを `openspec/changes/<change-name>/reviews.md` に追記する（単一ライター）。
- 指摘があれば修正し、全ロールが `LGTM: true` になるまで繰り返す。**最大 3 サイクル**で解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
- `Minor` / `Suggestion` で保留とした指摘は `handover.md` に全件転記する。

## ステップ 4: Gate 3 検証（機械的ゲート検証）

全 4 ロールの総括レビューが完了し `LGTM: true` となったことを確認した上で、**Gate 3 検証スクリプトを実行する**。

```powershell
pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<change-name>"
```

※ `PASSED` であることを確認してから次へ進む（Bash 環境の場合は `./openspec/scripts/verify-gate.sh --gate archive --change-name "<change-name>"`）。

## ステップ 5: OpenSpec Archive 実行・コミット・PR 自動作成（自走完走）

Gate 3 通過後、以下のステップを自律的に完走させる。

1. アーカイブを実行する（デルタ仕様が `openspec/specs/` に同期され、`openspec/changes/archive/` へ移動される）:
   ```bash
   openspec archive "<change-name>"
   ```
2. 自動フォーマットを実行し、アーカイブ結果をコミットする:
   ```bash
   npx prettier --write "openspec/**/*.md"
   git add openspec/   # README.md 等を更新した場合は併せてステージング
   git commit -m "chore(<change-name>): archive change and sync specs"
   ```
3. トピックブランチをリモートへプッシュする:
   ```bash
   git push -u origin <current-branch>
   ```
4. `openspec/templates/pr_body.md` に従って PR 本文を作成し、GitHub CLI で `develop` 向け Pull Request を自動作成する:
   ```bash
   gh pr create --base develop --head <current-branch> --title "<type>(<change-name>): <subject>" --body-file <pr-body-file>
   ```
5. **PR の URL と変更・検証サマリを提示してセッションを完了する**:
   - ローカルブランチはトピックブランチ（`<current-branch>`）のままで終了する（ローカルで動作確認やコード差分の確認をそのまま行える）。
   - 人間による最終レビューと合意は GitHub PR 上で実施し、承認後に **Merge commit**（`--merge`）で統合する。**ユーザー承認なしで PR をマージしてはならない。**
     ```bash
     # 人間承認後のマージ例
     gh pr merge <PR番号> --merge --delete-branch
     ```
