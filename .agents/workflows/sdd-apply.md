---
description: Tester（Red作成）とPG（Green/Refactor）のサブエージェント分業・C0/C1/C2カバレッジ検証・Gate 2検証を伴うSDD Applyワークフロー
---

# SDD Apply ワークフロー（TDD 3権分立 & Gate 2）

同一エージェントがテストと実装の両方を書くことによる「甘いテスト」「テスト改ざん」を防ぐため、**Tester（Red 作成） → Implementer（Green/Refactor 実装） → Reviewer（批判的レビュー）** のサブエージェント分業と機械的フェーズゲート（Gate 2）でタスクを遂行します。

## 事前確認

1. Propose ゲート（Gate 1）が通過済みであることを確認する:
   ```powershell
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<change-name>"
   ```
2. `openspec instructions apply --change "<change-name>" --json` でコンテキストと未完了タスクを確認する。

---

## 各タスクの実行サイクル（タスクごとに繰り返す）

### フェーズ A: TDD Red（先行テスト作成 — `qa-tester` サブエージェント）

1. **`qa-tester` サブエージェントの起動**（`openspec/agents/qa-tester.md` 参照）:
   - 入力: `specs/**/spec.md`, `design.md`, `test_plan.md`、および対象タスクの定義。
   - 指示: `test_plan.md` に定義されたテストケース（同値分割・境界値 `min-1`/`min`/`max`/`max+1`・空/0件/1件・C2 デシジョンテーブル・結合異常系・E2Eシナリオ）をテストコードとして先行実装する。プロダクションコードの本実装は行わない。
2. **Red（失敗）の実確認**:
   - テストを実行し、**意図通り失敗すること（Red）**を確認する。失敗ログの要点を控える（レビュー記録の証跡に使用）。

### フェーズ B: TDD Green & Refactor（実装 — `pg-implementer` サブエージェント）

1. **`pg-implementer` サブエージェントの起動**（`openspec/agents/pg-implementer.md` 参照）:
   - 入力: フェーズ A で作成された Red テスト、`specs/**/spec.md`, `design.md`, `mockup.html`。
   - 指示: **既存のテストコードを一切改ざん・削除・skip化せずに**、すべてのテストが Pass（Green）するプロダクションコードを実装し、続けて重複排除・命名改善・定数化（Refactor）を行う。
   - ※ 万が一テストコード側に仕様誤認がある場合は、`pg-implementer` は勝手にテストを書き換えずメインエージェントへ差し戻す。
2. **自動フォーマット・静的解析・テスト・カバレッジ出力**:
   - `AGENTS.md` 記載のフォーマッタおよびリンターを実行する（エラー・新規警告 0 件）。
   - テストおよびカバレッジ計測を実行し、エビデンスとカバレッジレポートを `test/TestResults/` に出力する。
   - **カバレッジ目標の確認**:
     - C0（命令網羅）: 全体 **80%以上** / 新規・変更差分 **90%以上**
     - C1（分岐網羅）: 全体 **80%以上** / 新規・変更差分 **85%以上**
     - C2（条件網羅）: `test_plan.md` の複合条件デシジョンテーブル **100%**
   - 未達の場合は `qa-tester` で不足している境界値・分岐のテストケースを追加する。

### フェーズ C: サブエージェント批判的レビュー（最大 3 サイクル）

1. 作成者とは別コンテキストのレビュアーサブエージェントを起動する:
   - **テストタスク / テストコード**: **`qa-reviewer`**（`openspec/agents/qa-reviewer.md` + `checklist_apply_test.md`）
   - **実装タスク / プロダクションコード**: **`pg-reviewer`**（`openspec/agents/pg-reviewer.md` + `checklist_apply_impl.md`）
   - **L3 または設計変更を伴う場合**: **`se-reviewer`** も併せて実施
2. レビュー結果を `openspec/changes/<change-name>/reviews.md` に `review_record.md` 形式で追記し、各指摘への対応方針（採用 / 保留 / 却下＋理由）を明記する。
3. 採用指摘（`Critical` / `Major` は必須採用）を修正し、フォーマット・テスト再実行後に再レビューを行う。
   - **最大 3 サイクル**で `Critical` / `Major` が 0 件（`LGTM: true`）にならなければ、作業を停止してユーザーへエスカレーションする。
   - `Minor` / `Suggestion` で「保留」とした指摘は、`reviews.md` の「保留指摘（申し送り）」に記録し、Archive 時に `handover.md` へ転記する。

### フェーズ D: Gate 2（タスク完了ゲート検証）とチェック更新・コミット

1. **Gate 2 検証スクリプトの実行**:
   ```powershell
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<change-name>"
   ```
   ※ `FAILED`（未判定項目の残存、LGTM 未取得、skip テスト放置等）の場合は `[x]` に更新してはならない。
2. `openspec/changes/<change-name>/tasks.md` の該当タスクを `- [ ]` → `- [x]` に更新する。
3. タスク単位でコミットする（テストエビデンス `test/TestResults/` はステージングしない）:
   ```bash
   git status --short
   git commit -m "feat(<change-name>): <タスク要約> (Task X.Y)"
   ```
