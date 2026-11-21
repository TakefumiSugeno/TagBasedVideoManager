---
name: sdd-apply
description: "OpenSpec 変更のタスク実装（Apply）フェーズを、qa-tester による先行 Red テスト作成・pg-implementer による Green/Refactor 実装・pg-reviewer/qa-reviewer の批判的レビュー・C0/C1/C2 カバレッジ検証・Gate 2 検証付きの TDD 3 権分立で実行する。tasks.md のタスク実装、openspec apply、sdd-apply、実装着手・継続を依頼されたときに使う。"
---

# SDD Apply（TDD 3 権分立 & Gate 2）

同一エージェントがテストと実装の両方を書くことによる「甘いテスト」「テスト改ざん」を防ぐため、**Tester（Red 作成） → Implementer（Green/Refactor 実装） → Reviewer（批判的レビュー）** のサブエージェント分業と機械的フェーズゲート（Gate 2）でタスクを遂行する、5 エージェント（Antigravity CLI / Claude Code / GitHub Copilot CLI / OpenCode CLI / Hermes Agent）共通のスキルです。

- 詳細リファレンス（6 ロール表・カバレッジ基準・ゲート表・Git 操作・CLI）: [references/sdd-reference.md](references/sdd-reference.md)
- Antigravity CLI 実行時の専用手順（モデル指定・権限分離・障害回復・L3 swarm）: [references/antigravity-orchestration.md](references/antigravity-orchestration.md)

## 実行環境とサブエージェント分業（フォールバック）

本スキルの手順は特定 CLI 専用ツールを必須前提としません。テスト作成・実装・レビューは、それぞれ別の独立コンテキストで実行します。

| 実行環境               | 分業方式                                                                                                                                                                        |
| :--------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Antigravity CLI        | [references/antigravity-orchestration.md](references/antigravity-orchestration.md) に従い、ロール別モデル・権限を指定したサブエージェントで実行する                             |
| Claude Code            | `.claude/agents/` に展開された 6 ロール（`openspec/agents/*.md` と同一内容）を標準サブエージェントとして起動する                                                                |
| GitHub Copilot CLI     | 標準のカスタムエージェント／サブエージェント機構があれば `openspec/agents/<role>.md` を指示として起動する                                                                       |
| OpenCode CLI / Hermes  | 各エージェント標準のサブエージェント（subagent）機構があれば `openspec/agents/<role>.md` を指示として起動する                                                                   |
| 上記機構が使えない環境 | **フォールバック**: 新規セッション（独立コンテキスト）を開き、ロール定義の本文を指示として読ませ、対象成果物のみを渡して Tester → Implementer → Reviewer の順に**直列**実行する |

どの方式でも次の原則を守ります。

- **独立コンテキスト**: Tester・Implementer・Reviewer は互いの思考過程を共有せず、成果物（仕様・テスト・差分）のみを受け渡す。
- **単一ライター**: `reviews.md` と `tasks.md` を編集するのは本スキルを実行している親（メイン）エージェントのみ。サブエージェントは `review_record.md` 形式のテキストまたは作業サマリを返却する。
- **テスト保護**: Implementer は既存テストの期待値・アサーション・入力データを書き換えない。テスト側の仕様誤認は書き換えずに親へ差し戻す。
- Antigravity 専用ツール（`define_subagent` / `run_workflow` / `swarm`）が無い環境でも、本ファイルの手順だけで完遂できる。

## 事前確認

1. Propose ゲート（Gate 1）が通過済みであることを確認する:
   ```powershell
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<change-name>"
   ```
2. `openspec instructions apply --change "<change-name>" --json` でコンテキストと未完了タスクを確認する。

---

## 各タスクの実行サイクル（タスクごとに繰り返す）

### フェーズ A: TDD Red（先行テスト作成 — `qa-tester`）

1. **`qa-tester` の起動**（`openspec/agents/qa-tester.md` 参照）:
   - 入力: `specs/**/spec.md`, `design.md`, `test_plan.md`、および対象タスクの定義。
   - 指示: `test_plan.md` に定義されたテストケース（同値分割・境界値 `min-1`/`min`/`max`/`max+1`・空/0件/1件・C2 デシジョンテーブル・結合異常系・E2Eシナリオ）をテストコードとして先行実装する。プロダクションコードの本実装は行わない。
2. **Red（失敗）の実確認**:
   - テストを実行し、**意図通り失敗すること（Red）**を確認する。失敗ログの要点を控える（レビュー記録の証跡に使用）。

### フェーズ B: TDD Green & Refactor（実装 — `pg-implementer`）

1. **`pg-implementer` の起動**（`openspec/agents/pg-implementer.md` 参照）:
   - 入力: フェーズ A で作成された Red テスト、`specs/**/spec.md`, `design.md`, `mockup.html`。
   - 指示: **既存のテストコードを一切改ざん・削除・skip化せずに**、すべてのテストが Pass（Green）するプロダクションコードを実装し、続けて重複排除・命名改善・定数化（Refactor）を行う。
   - ※ テストコード側に仕様誤認がある場合は、`pg-implementer` は勝手にテストを書き換えず親（メイン）エージェントへ差し戻す。
2. **自動フォーマット・静的解析・テスト・カバレッジ出力**:
   - `AGENTS.md` 記載のフォーマッタおよびリンターを実行する（エラー・新規警告 0 件）。
   - テストおよびカバレッジ計測を実行し、エビデンスとカバレッジレポートを `test/TestResults/` に出力する。
   - **カバレッジ目標の確認**:
     - C0（命令網羅）: 全体 **80%以上** / 新規・変更差分 **90%以上**
     - C1（分岐網羅）: 全体 **80%以上** / 新規・変更差分 **85%以上**
     - C2（条件網羅）: `test_plan.md` の複合条件デシジョンテーブル **100%**
   - 未達の場合は `qa-tester` で不足している境界値・分岐のテストケースを追加する。

### フェーズ C: サブエージェント批判的レビュー（最大 3 サイクル）

1. 作成者とは別コンテキストのレビュアーを起動する:
   - **テストタスク / テストコード**: **`qa-reviewer`**（`openspec/agents/qa-reviewer.md` + `openspec/templates/checklist_apply_test.md`）
   - **実装タスク / プロダクションコード**: **`pg-reviewer`**（`openspec/agents/pg-reviewer.md` + `openspec/templates/checklist_apply_impl.md`）
   - **L3 または設計変更を伴う場合**: **`se-reviewer`**（`openspec/agents/se-reviewer.md` + `openspec/templates/checklist_apply_impl.md`）も併せて実施
   - 各レビュアーには対象成果物・`git diff`・チェックリストのパスを渡し、`openspec/templates/review_record.md` 形式のテキストで返却させる（レビュアー自身は `reviews.md` を編集しない）。
2. 親エージェントが返却テキストを `openspec/changes/<change-name>/reviews.md` に `review_record.md` 形式で追記し、各指摘への対応方針（採用 / 保留 / 却下＋理由）を明記する（単一ライター）。
3. 採用指摘（`Critical` / `Major` は必須採用）を修正し、フォーマット・テスト再実行後に再レビューを行う。
   - **最大 3 サイクル**で `Critical` / `Major` が 0 件（`LGTM: true`）にならなければ、作業を停止してユーザーへエスカレーションする。
   - `Minor` / `Suggestion` で「保留」とした指摘は、`reviews.md` の「保留指摘（申し送り）」に記録し、Archive 時に `handover.md` へ転記する。

### フェーズ D: Gate 2（タスク完了ゲート検証）とチェック更新・コミット

1. **Gate 2 検証スクリプトの実行**:
   ```powershell
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<change-name>"
   ```
   ※ `FAILED`（未判定項目の残存、LGTM 未取得、skip テスト放置等）の場合は `[x]` に更新してはならない（Bash 環境: `./openspec/scripts/verify-gate.sh --gate apply --change-name "<change-name>"`）。
2. `openspec/changes/<change-name>/tasks.md` の該当タスクを `- [ ]` → `- [x]` に更新する（親エージェントのみ）。
3. タスク単位でコミットする（テストエビデンス `test/TestResults/` はステージングしない）:
   ```bash
   git status --short
   git commit -m "feat(<change-name>): <タスク要約> (Task X.Y)"
   ```

次フェーズ: 全タスク完了後は `sdd-archive` スキルを使用する。
