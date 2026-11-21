# SDD 詳細リファレンス（sdd-archive）

本書は `sdd-archive` スキル内で参照する自己完結リファレンスです。`AGENTS.openspec.md` から分離された詳細基準（完了基準 DoD・4 ロール総括レビュー規律・C0/C1/C2 テスト網羅性最終確認・Gate 3 条件・Archive/Push/PR 自動作成規律・OpenSpec CLI リファレンス）を格納しています。

---

## 1. サブエージェント6ロール分業・モデルティア・総括レビュー規律

Archive フェーズでは、全 4 レビュアー（`user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer`）を別コンテキストのサブエージェントとして起動し、`openspec/templates/checklist_archive.md` による総括レビューを実施する（Gemini 3.8 Flash は非推奨のため採用しない）。

| ロール             | 定義ファイル                        | `model_tier` / `thinking` / `write_tools` | 担当フェーズ      | 総括レビューでの責務・ガードレール                                                                                                                |
| ------------------ | ----------------------------------- | ----------------------------------------- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| **User Reviewer**  | `openspec/agents/user-reviewer.md`  | `pro` / `high` / `false`                  | Propose / Archive | 要件充足・Zero-State/Error-State 体験・初見エンジニア視点での `handover.md` / README の Why 欠落告発（`openspec/templates/checklist_archive.md`） |
| **SE Reviewer**    | `openspec/agents/se-reviewer.md`    | `pro` / `high` / `false`                  | 全フェーズ        | アーキテクチャ整合・運用/移行リスク・工数都合で隠された技術的負債や時限爆弾の告発（`openspec/templates/checklist_archive.md`）                    |
| **PG Reviewer**    | `openspec/agents/pg-reviewer.md`    | `pro` / `high` / `false`                  | Apply / Archive   | コード品質・暗黙の仮定・例外握りつぶし・TODO/デッドコード残存の最終摘出（`openspec/templates/checklist_archive.md`）                              |
| **QA Reviewer**    | `openspec/agents/qa-reviewer.md`    | `pro` / `high` / `true`                   | Apply / Archive   | C0/C1/C2 カバレッジ・全 Scenario 網羅・skip 放置ゼロ・トートロジー排除・エビデンス確認（`openspec/templates/checklist_archive.md`）               |
| **QA Tester**      | `openspec/agents/qa-tester.md`      | `flash` / `high` / `true`                 | Propose / Apply   | （総括レビューでテスト不足が判明した場合の追加テスト実装担当）                                                                                    |
| **PG Implementer** | `openspec/agents/pg-implementer.md` | `flash` / `medium` / `true`               | Apply             | （総括レビューでコード修正が必要となった場合の修正実装担当）                                                                                      |

### レビュー・エスカレーション規律

1. **独立コンテキストと単一ライター原則**: 必ずサブエージェントに対象成果物・`git diff`・`openspec/templates/checklist_archive.md` のパスを渡して実行する。`reviews.md`・`tasks.md`・`handover.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントは `openspec/templates/review_record.md` 形式のテキストを返却する。
2. **最大3サイクルとエスカレーション**: 全 4 ロールの `Critical` / `Major` 指摘が 0 件（`LGTM: true`）になるまで修正・再レビューを繰り返す。**最大 3 サイクル**で解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
3. **保留指摘の引き継ぎ**: Apply〜Archive を通じて `Minor` / `Suggestion` で保留とした指摘は、`openspec/changes/<name>/handover.md` に全件転記する。

---

## 2. 完了基準（Definition of Done）とテスト網羅性最終確認

Archive 前に以下の全基準が満たされ、最新エビデンスが `test/TestResults/` に出力されていることを確認する。

| テストレベル                 | 網羅指標                                                                                   | 目標基準（デフォルト）                                                                  | 最終確認内容                                                                                                                               |
| :--------------------------- | :----------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------- | :----------------------------------------------------------------------------------------------------------------------------------------- |
| **単体テスト (Unit)**        | **C0（命令網羅 / Line）**<br>**C1（分岐網羅 / Branch）**<br>**C2（条件網羅 / Condition）** | 全体 **80%+** / 差分 **90%+**<br>全体 **80%+** / 差分 **85%+**<br>対象複合条件 **100%** | ・全テスト **100% Pass**（`skip` / `ignore` 放置 **0 件**）<br>・`test_plan.md` の境界値・デシジョンテーブル全件実装済み                   |
| **結合テスト (Integration)** | **IF契約網羅**<br>**状態遷移網羅（0/1-switch）**                                           | 全外部/DB境界 **100%**<br>全状態遷移 **100%**                                           | ・正常往復・異常系注入（Fault Injection）・状態遷移（0/1-switch）・冪等性の全テスト Pass                                                   |
| **E2Eテスト (End-to-End)**   | **仕様 Scenario 網羅率**<br>**主要ジャーニー網羅**                                         | `specs/` 全 Scenario **100%**                                                           | ・Happy / Zero-State / Negative & Recovery Path の全テスト Pass<br>・UI 変更時は実画面キャプチャ（PNG）出力済み                            |
| **コード品質・ドキュメント** | **フォーマット / 静的解析 / ビルド**<br>**引き継ぎ書 (`handover.md`)**                     | エラー・差分 **0 件**<br>未記入プレースホルダ **0 件**                                  | ・`AGENTS.md` 記載のフォーマッタ・リンター通過<br>・`README.md` 等の関連ドキュメント同期済み、`handover.md` に変更サマリ・保留指摘記載済み |

---

## 3. 機械的フェーズゲート（Gate 3）

| ゲート     | 実行タイミング            | 検証コマンド                                                                 | 完了基準（DoD / ゲート通過条件）                                                                                                                                    |
| :--------- | :------------------------ | :--------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Gate 3** | `openspec archive` 実行前 | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"` | ・`tasks.md` の未完了タスク（`- [ ]`）が 0 件であること<br>・`handover.md` が記入済みであること<br>・全4ロール総括レビュー `LGTM: true` かつ DoD を達成していること |

---

## 4. Git / Push / Pull Request / Merge ルール（Archive フェーズ）

1. **Archive 完了後の Commit（Gate 3 通過後）**:
   - `openspec archive "<change-name>"` を実行し、更新されたメイン仕様（`openspec/specs/`）、アーカイブされた変更（`openspec/changes/archive/`）、`README.md` 等の関連ドキュメントを自動フォーマット後に `chore(<change-name>): archive change and sync specs` でコミットする。
2. **Push & PR 自動作成**:
   - **Push**: トピックブランチへの push（`git push -u origin <branch>`）は Archive 完了時の自走タスクとして自動実行する。
   - **PR 作成**: `openspec/templates/pr_body.md` に従い `gh pr create --base develop --head <branch> --title "<type>(<scope>): <subject>" --body-file <PR本文ファイル>` を自動実行する（ローカルブランチはトピックブランチのままでセッション完了とし、人間レビューは PR 上で実施）。
   - **マージ**: 人間によるレビュー承認後、**Merge commit**（`gh pr merge <PR番号> --merge --delete-branch`）で統合する（Squash / Rebase merge は禁止）。
3. **禁止事項（ハードガードレール）**:
   - `develop` および `main` への直接コミット・直接 push
   - `verify-gate.ps1 -Gate archive` を実行・通過せずに行う `openspec archive` 実行
   - **ユーザー承認なしでの PR マージ（ベースブランチへの統合）**
   - テストエビデンス（`test/TestResults/`）・カバレッジレポート・秘密情報（`.env` 等）の commit

---

## 5. OpenSpec CLI & ゲートコマンド リファレンス

```bash
# 進捗・検証
openspec status --change "<name>" --json                  # 成果物の進捗確認
openspec validate "<name>"                                # 変更の検証

# Gate 3 検証（PowerShell 版 / Bash 版）
pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"
./openspec/scripts/verify-gate.sh --gate archive --change-name "<name>"

# アーカイブ実行（Gate 3 通過後）
openspec archive "<name>"
```
