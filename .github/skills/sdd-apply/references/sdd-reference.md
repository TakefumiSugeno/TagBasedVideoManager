# SDD 詳細リファレンス（sdd-apply）

本書は `sdd-apply` スキル内で参照する自己完結リファレンスです。`AGENTS.openspec.md` から分離された詳細基準（6 ロール定義とモデル割り当て・TDD 3 権分立・C0/C1/C2 テスト網羅性基準・不具合修正 TDD プロトコル・Gate 2 条件・Git コミット規律・OpenSpec CLI リファレンス）を格納しています。

---

## 1. サブエージェント6ロール分業・モデルティア・レビュー規律

同一エージェントがテストと実装の両方を担当すると「実装に合わせた甘いテスト」や「テストの期待値改ざん」が起きるため、以下のロールを別コンテキストのサブエージェントとして起動する（Gemini 3.8 Flash は非推奨のため採用しない）。

| ロール             | 定義ファイル                        | `model_tier` / `thinking` / `write_tools` | 担当フェーズ      | 責務・ガードレール                                                                                                                                       |
| ------------------ | ----------------------------------- | ----------------------------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **QA Tester**      | `openspec/agents/qa-tester.md`      | `flash` / `high` / `true`                 | Propose / Apply   | `specs/` と `test_plan.md` から先行失敗テスト（Red）および結合/E2Eテストを実装。**プロダクションコード編集禁止**                                         |
| **PG Implementer** | `openspec/agents/pg-implementer.md` | `flash` / `medium` / `true`               | Apply             | Red テストを通すプロダクションコード実装（Green）と Refactor。**既存テストの無断改ざん・skip化・TODO放置禁止**                                           |
| **PG Reviewer**    | `openspec/agents/pg-reviewer.md`    | `pro` / `high` / `false`                  | Apply / Archive   | コード潔癖症視点で暗黙の仮定（思い込み）・例外握りつぶし告発・30%削減リファクタ提案（`openspec/templates/checklist_apply_impl.md`）                      |
| **QA Reviewer**    | `openspec/agents/qa-reviewer.md`    | `pro` / `high` / `true`                   | Apply / Archive   | 敵対的QAとして破壊テスト作成・実行（Adversarial QA）・トートロジー（実装式コピペ）・甘いアサーション告発（`openspec/templates/checklist_apply_test.md`） |
| **SE Reviewer**    | `openspec/agents/se-reviewer.md`    | `pro` / `high` / `false`                  | 全フェーズ        | L3 または設計変更時に実施。外部依存ハング・通信瞬断時の故障シミュレーション・状態不整合告発（`openspec/templates/checklist_apply_impl.md`）              |
| **User Reviewer**  | `openspec/agents/user-reviewer.md`  | `pro` / `high` / `false`                  | Propose / Archive | ブラインド仕様比較・せっかち初心者視点でのZero-State/Error-State体験・Why欠落告発                                                                        |

### レビュー・エスカレーション規律

1. **独立コンテキストと単一ライター原則**: 必ずサブエージェントに対象成果物・`git diff`・チェックリストのパスを渡して実行する。`reviews.md` および `tasks.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントは `openspec/templates/review_record.md` 形式のテキストまたは作業サマリを返却する。
2. **挑戦的設問回答と破壊テスト実行**: 各成果物の挑戦的設問への具体的回答・根拠、および実装コードを例外終了させる破壊テスト実行ログ（Adversarial QA）を `openspec/changes/<name>/reviews.md` に追記する。フォーマット・静的解析・テスト合否・カバレッジ数値等の機械的検証は `verify-gate.ps1` に委譲し LLM レビューから除外する。
3. **最大3サイクルとエスカレーション**: `Critical` / `Major` 指摘（破壊テストによるバグ検出含む）が 0 件（`LGTM: true`）になるまで修正・再レビューを繰り返す。**最大 3 サイクル**で解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
4. **保留指摘の引き継ぎ**: `Minor` / `Suggestion` で保留とした指摘は `reviews.md` に記録し、Archive 時に `handover.md` へ全件転記する。

---

## 2. テスト網羅性基準（C0 / C1 / C2・結合・E2E）

`openspec/changes/<name>/test_plan.md` に定義された全テストケースを実装し、以下の基準を達成する。

| テストレベル                 | 網羅指標                                                                                   | 目標基準（デフォルト）                                                                  | 必須設計技法・検証内容                                                                                                                                                                                                                                                                                                                                                |
| :--------------------------- | :----------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **単体テスト (Unit)**        | **C0（命令網羅 / Line）**<br>**C1（分岐網羅 / Branch）**<br>**C2（条件網羅 / Condition）** | 全体 **80%+** / 差分 **90%+**<br>全体 **80%+** / 差分 **85%+**<br>対象複合条件 **100%** | ・**同値分割・境界値分析**: 数値（`min-1`, `min`, `max`, `max+1`, `0`, 負数）、文字列（空, 空白, 1文字, 上限±1, 特殊文字）、配列（`[]`, 1件, 複数, 上限±1）、日時（境界時刻, TZ, うるう日）<br>・**デシジョンテーブル**: 複合条件（`A && B \|\| C`）の真偽組み合わせ<br>・**モック最小化**: 外部I/Oのみモック可。対象ロジックのモック化やトートロジーアサーション禁止 |
| **結合テスト (Integration)** | **IF契約網羅**<br>**状態遷移網羅（0/1-switch）**                                           | 全外部/DB境界 **100%**<br>全状態遷移 **100%**                                           | ・正常往復＋シリアライズ境界＋トランザクションロールバック<br>・**異常系注入（Fault Injection）**: 4xx/5xx応答、Timeout、切断、DB制約違反<br>・**状態遷移**: 有効遷移（0-switch）＋無効遷移の拒否（1-switch）・二重実行時の冪等性                                                                                                                                     |
| **E2Eテスト (End-to-End)**   | **仕様 Scenario 網羅率**<br>**主要ジャーニー網羅**                                         | `specs/` 全 Scenario **100%**                                                           | ・① **Happy Path**（主要正常導線）<br>・② **Zero-State / 初回起動 Path**（データ0件・APIキー未設定・未ログイン）<br>・③ **Negative / Recovery Path**（エラー表示→修正→再試行成功）<br>・**実画面レンダリング検証**: 実ブラウザ/実ウィンドウで描画し PNG キャプチャを出力（名ばかりE2Eの禁止）                                                                         |

### 不具合修正（`fix/<change-name>`）時の専用 TDD プロトコル

1. **根本原因（Root Cause）の特定**: 現象の対症療法ではなく、なぜ発生したかを `proposal.md` と `design.md` に明記する。
2. **再現テスト（Red）の先行作成**: 修正前のコードで確実に失敗する回帰テストを `qa-tester` で最初に作成する。
3. **最小修正（Green）と横展開チェック**: `pg-implementer` で修正後、同種のバグが他モジュールに存在しないか検索・確認する。

---

## 3. 完了基準（Definition of Done）と機械的フェーズゲート

| ゲート     | 実行タイミング                         | 検証コマンド                                                                 | 完了基準（DoD / ゲート通過条件）                                                                                                                                                                  |
| :--------- | :------------------------------------- | :--------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Gate 1** | Apply 着手前（事前確認）               | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"` | ・規模レベルに応じた必須ファイルが揃い、Propose レビューが `LGTM: true` であること                                                                                                                |
| **Gate 2** | Apply 各タスクの `[x]` 更新・commit 前 | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<name>"`   | ・フォーマット差分0、静的解析エラー0、全テストPass、C0/C1/C2基準達成<br>・テストの `skip` 放置が 0 件であること<br>・`reviews.md` の最新タスクレビューが `LGTM: true` であること                  |
| **Gate 3** | 全タスク完了後、`openspec archive` 前  | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"` | ・`tasks.md` の未完了タスク（`- [ ]`）が 0 件であること<br>・`handover.md` が記入済みであること<br>・全4ロール総括レビュー `LGTM: true` かつ DoD（テスト100%Pass、skip放置0件）を達成していること |

---

## 4. 自動フォーマットと Git コミット規律（Apply フェーズ）

1. **自動フォーマット**:
   - ファイル更新時・コミット前・テスト実行前に、`AGENTS.md` の「自動フォーマット」表に記載されたフォーマッタおよび静的解析を実行する。
2. **タスク完了時の Commit（Gate 2 通過後）**:
   - `verify-gate.ps1 -Gate apply` が PASS したことを確認してから、`tasks.md` の該当タスクを `[x]` に更新する。
   - 実装・テストコード、`tasks.md` の進捗、`reviews.md` のレビュー記録を Conventional Commits（`<type>(<change-name>): <subject>`、type は `feat|fix|docs|refactor|test|chore|review|spec`）でコミットする。
3. **禁止事項（ハードガードレール）**:
   - `develop` および `main` への直接コミット・直接 push
   - `verify-gate.ps1` を通過せずに行う `[x]` 更新
   - 既存テストの期待値改ざん・アサーション削除・`skip` 化による見せかけの Green 化
   - テストエビデンス（`test/TestResults/`）・カバレッジレポート・秘密情報（`.env` 等）の commit

---

## 5. OpenSpec CLI & ゲートコマンド リファレンス

```bash
# 実装指示・進捗確認
openspec instructions apply --change "<name>" --json      # 実装指示（operations.apply.guidance を含む）
openspec status --change "<name>" --json                  # 成果物の進捗確認
openspec validate "<name>"                                # 変更の検証

# フェーズゲート検証（PowerShell 版 / Bash 版）
pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"
pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply   -ChangeName "<name>"
./openspec/scripts/verify-gate.sh --gate propose --change-name "<name>"
./openspec/scripts/verify-gate.sh --gate apply   --change-name "<name>"
```
