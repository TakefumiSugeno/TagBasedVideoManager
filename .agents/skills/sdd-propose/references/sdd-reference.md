# SDD 詳細リファレンス（sdd-propose）

本書は `sdd-propose` スキル内で参照する自己完結リファレンスです。`AGENTS.openspec.md` から分離された詳細基準（適用規模レベル・6 ロール定義とモデル割り当て・C0/C1/C2 テスト網羅性基準・Gate 0〜3 条件・Git ブランチ安全シーケンス・OpenSpec CLI リファレンス）を格納しています。

---

## 1. ディレクトリ構成

| パス                               | 内容                                                                                                    |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `openspec/config.yaml`             | OpenSpec 設定（スキーマ・規模レベル・6ロール定義・TDD/カバレッジ基準・運用ルール）                      |
| `openspec/specs/`                  | メイン仕様（システムの恒久的な仕様）                                                                    |
| `openspec/changes/<name>/`         | 変更単位の成果物（proposal / specs / design / test_plan / tasks / mockup / reviews / handover）         |
| `openspec/changes/archive/`        | 完了した変更のアーカイブ                                                                                |
| `openspec/agents/`                 | サブエージェント6ロールのシステムプロンプト定義（`qa-tester`, `pg-implementer`, `*-reviewer`）          |
| `openspec/templates/`              | テスト計画・レビュー記録・チェックリスト・handover・PR 本文テンプレート                                 |
| `openspec/scripts/verify-gate.ps1` | フェーズゲート（Gate 0〜3 / DoD）の機械的検証スクリプト（Bash 版: `verify-gate.sh`）                    |
| `.agents/skills/sdd-*/SKILL.md`    | ゲート・サブエージェント分業を組み込んだ 5 エージェント共通スキル（`.claude/skills/` 等にも同内容展開） |
| `test/TestResults/`                | テストエビデンス・カバレッジレポート出力先（Git 管理外）                                                |

---

## 2. 適用規模レベル（L1 / L2 / L3）と変更ディレクトリの成果物

変更開始時に規模レベル（`L1` / `L2` / `L3`）を決定し、`.openspec.yaml`（`level: L2` 等）と `proposal.md` に明記する。

| 成果物 / 運用                       | L1（軽微: typo・設定・局所修正）   | L2（通常: 機能追加・仕様変更・通常バグ）           | L3（大規模: アーキテクチャ・破壊的変更・DB変更） |
| ----------------------------------- | ---------------------------------- | -------------------------------------------------- | ------------------------------------------------ |
| `proposal.md` / `tasks.md`          | 必須                               | 必須                                               | 必須（移行計画・ロールバック手順含む）           |
| `specs/**/spec.md` / `design.md`    | 仕様不変なら `skip_specs: true` 可 | 必須                                               | 必須                                             |
| `test_plan.md`（テストケース定義）  | 任意（`tasks.md` 内に記載可）      | **必須**（境界値・C2表・結合/E2E定義）             | **必須**                                         |
| `mockup.html`（単一ファイルHTML）   | 不要                               | UI/UX変更時は**必須**                              | UI/UX変更時は**必須**                            |
| Propose レビュー                    | `se-reviewer` のみ可               | `user-reviewer` + `se-reviewer`                    | `user-reviewer` + `se-reviewer`                  |
| Apply 時の実装・テスト分業          | メイン兼任可 ＋ PG/QA レビュー     | **`qa-tester` → `pg-implementer` → PG/QAレビュー** | L2 ＋ 設計変更時は `se-reviewer` 必須            |
| Archive 総括レビュー・`handover.md` | 必須                               | 必須                                               | 必須                                             |

---

## 3. サブエージェント6ロール分業・モデルティア・レビュー規律

同一エージェントがテストと実装の両方を担当すると「実装に合わせた甘いテスト」や「テストの期待値改ざん」が起きるため、6 ロールを別コンテキストのサブエージェントとして起動する（Gemini 3.8 Flash は非推奨のため採用しない）。

| ロール             | 定義ファイル                        | `model_tier` / `thinking` / `write_tools` | 担当フェーズ      | 責務・ガードレール                                                                                                                                       |
| ------------------ | ----------------------------------- | ----------------------------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **User Reviewer**  | `openspec/agents/user-reviewer.md`  | `pro` / `high` / `false`                  | Propose / Archive | ブラインド仕様比較（異常系・境界先出し突合）・せっかち初心者視点でのZero-State/Error-State体験・Why欠落告発（`openspec/templates/checklist_propose.md`） |
| **SE Reviewer**    | `openspec/agents/se-reviewer.md`    | `pro` / `high` / `false`                  | 全フェーズ        | 外部依存ハング・通信瞬断時の故障シミュレーション（連鎖崩壊検証）・状態不整合・隠された負債告発（`openspec/templates/checklist_propose.md`）              |
| **QA Tester**      | `openspec/agents/qa-tester.md`      | `flash` / `high` / `true`                 | Propose / Apply   | `specs/` と `test_plan.md` から先行失敗テスト（Red）および結合/E2Eテストを実装。**プロダクションコード編集禁止**                                         |
| **PG Implementer** | `openspec/agents/pg-implementer.md` | `flash` / `medium` / `true`               | Apply             | Red テストを通すプロダクションコード実装（Green）と Refactor。**既存テストの無断改ざん・skip化・TODO放置禁止**                                           |
| **PG Reviewer**    | `openspec/agents/pg-reviewer.md`    | `pro` / `high` / `false`                  | Apply / Archive   | コード潔癖症視点で暗黙の仮定（思い込み）・例外握りつぶし告発・30%削減リファクタ提案                                                                      |
| **QA Reviewer**    | `openspec/agents/qa-reviewer.md`    | `pro` / `high` / `true`                   | Apply / Archive   | 敵対的QAとして破壊テスト作成・実行（Adversarial QA）・トートロジー（実装式コピペ）・甘いアサーション告発                                                 |

### レビュー・エスカレーション規律

1. **独立コンテキストと単一ライター原則**: 必ずサブエージェントに対象成果物・`git diff`・チェックリストのパスを渡して実行する。`reviews.md` および `tasks.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントは `openspec/templates/review_record.md` 形式のテキストを返却する。
2. **挑戦的設問回答**: 各成果物の挑戦的設問への具体的回答・根拠を `openspec/changes/<name>/reviews.md` に記録する。フォーマット・静的解析・テスト合否・カバレッジ数値等の機械的検証は `verify-gate.ps1` に委譲し LLM レビューから除外する。
3. **最大3サイクルとエスカレーション**: `Critical` / `Major` 指摘が 0 件（`LGTM: true`）になるまで修正・再レビューを繰り返す。**最大 3 サイクル**で解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
4. **保留指摘の引き継ぎ**: `Minor` / `Suggestion` で保留とした指摘は `reviews.md` に記録し、Archive 時に `handover.md` へ全件転記する。

---

## 4. テスト網羅性基準（C0 / C1 / C2・結合・E2E）とテスト計画策定基準

L2 / L3 の変更では、コードを書く前に `openspec/templates/test_plan.md` から `openspec/changes/<name>/test_plan.md` を作成し、以下の基準を満たすテストケースを定義する。

| テストレベル                 | 網羅指標                                                                                   | 目標基準（デフォルト）                                                                  | 必須設計技法・検証内容                                                                                                                                                                                                                                                                                                                                                |
| :--------------------------- | :----------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **単体テスト (Unit)**        | **C0（命令網羅 / Line）**<br>**C1（分岐網羅 / Branch）**<br>**C2（条件網羅 / Condition）** | 全体 **80%+** / 差分 **90%+**<br>全体 **80%+** / 差分 **85%+**<br>対象複合条件 **100%** | ・**同値分割・境界値分析**: 数値（`min-1`, `min`, `max`, `max+1`, `0`, 負数）、文字列（空, 空白, 1文字, 上限±1, 特殊文字）、配列（`[]`, 1件, 複数, 上限±1）、日時（境界時刻, TZ, うるう日）<br>・**デシジョンテーブル**: 複合条件（`A && B \|\| C`）の真偽組み合わせ<br>・**モック最小化**: 外部I/Oのみモック可。対象ロジックのモック化やトートロジーアサーション禁止 |
| **結合テスト (Integration)** | **IF契約網羅**<br>**状態遷移網羅（0/1-switch）**                                           | 全外部/DB境界 **100%**<br>全状態遷移 **100%**                                           | ・正常往復＋シリアライズ境界＋トランザクションロールバック<br>・**異常系注入（Fault Injection）**: 4xx/5xx応答、Timeout、切断、DB制約違反<br>・**状態遷移**: 有効遷移（0-switch）＋無効遷移の拒否（1-switch）・二重実行時の冪等性                                                                                                                                     |
| **E2Eテスト (End-to-End)**   | **仕様 Scenario 網羅率**<br>**主要ジャーニー網羅**                                         | `specs/` 全 Scenario **100%**                                                           | ・① **Happy Path**（主要正常導線）<br>・② **Zero-State / 初回起動 Path**（データ0件・APIキー未設定・未ログイン）<br>・③ **Negative / Recovery Path**（エラー表示→修正→再試行成功）<br>・**実画面レンダリング検証**: 実ブラウザ/実ウィンドウで描画し PNG キャプチャを出力（名ばかりE2Eの禁止）                                                                         |

### 不具合修正（`fix/<change-name>`）時の専用 TDD プロトコル

1. **根本原因（Root Cause）の特定**: 現象の対症療法ではなく、なぜ発生したかを `proposal.md` と `design.md` に明記する。
2. **再現テスト（Red）の先行作成**: 修正前のコードで確実に失敗する回帰テストを `qa-tester` で最初に作成するタスクを `tasks.md` の先頭に配置する。
3. **最小修正（Green）と横展開チェック**: `pg-implementer` で修正後、同種のバグが他モジュールに存在しないか検索・確認する。

---

## 5. 完了基準（Definition of Done）と機械的フェーズゲート

| ゲート     | 実行タイミング                         | 検証コマンド                                                                                                 | 完了基準（DoD / ゲート通過条件）                                                                                                                                                                   |
| :--------- | :------------------------------------- | :----------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Gate 0** | `openspec new change` 直前             | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose`                                                  | ・未コミット変更の退避済み<br>・最新 `develop` から切った `feature/*` / `fix/*` 等のブランチ上にいること                                                                                           |
| **Gate 1** | Propose 合意・commit 前                | `openspec validate "<name>"`<br>`pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"` | ・規模レベルに応じた必須ファイル（`proposal`, `specs`, `design`, `test_plan`, `tasks`, `mockup`）が揃っていること<br>・`reviews.md` に未判定項目がなく、Propose レビューが `LGTM: true` であること |
| **Gate 2** | Apply 各タスクの `[x]` 更新・commit 前 | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<name>"`                                   | ・フォーマット差分0、静的解析エラー0、全テストPass、C0/C1/C2基準達成<br>・テストの `skip` 放置が 0 件であること<br>・`reviews.md` の最新タスクレビューが `LGTM: true` であること                   |
| **Gate 3** | `openspec archive` 実行前              | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"`                                 | ・`tasks.md` の未完了タスク（`- [ ]`）が 0 件であること<br>・`handover.md` が記入済みであること<br>・全4ロール総括レビュー `LGTM: true` かつ DoD（テスト100%Pass、skip放置0件）を達成していること  |

---

## 6. Git 操作ルール（Propose フェーズ）

1. **Gate 0 ブランチ作成シーケンス**:
   - `git status --porcelain` で未コミット変更を確認し、存在する場合は `git stash push -u -m "wip: backup before switch to <change-name>"` で退避する。
   - `git checkout develop && git pull origin develop && git fetch --prune` を実行し、マージ済みローカルブランチを削除した上で `git checkout -b feature/<change-name>`（または `fix/<change-name>` 等）を作成する。
2. **Gate 1 通過後の Commit**:
   - `openspec/changes/<name>/` 配下の全計画成果物（`proposal`, `specs`, `design`, `test_plan`, `tasks`, `mockup`, `reviews`）を `spec(<change-name>): <subject>` でコミットする。
3. **禁止事項**:
   - `develop` および `main` への直接コミット・直接 push
   - `verify-gate.ps1` を通過せずに行うフェーズ移行
   - テストエビデンス（`test/TestResults/`）や秘密情報（`.env` 等）の commit

---

## 7. OpenSpec CLI & ゲートコマンド リファレンス

```bash
# 変更の作成・一覧・進捗
openspec new change "<name>"                              # 新規変更作成（Gate 0 通過後）
openspec list --json                                      # 進行中の変更一覧
openspec list --specs                                     # メイン仕様（capability）一覧
openspec status --change "<name>" --json                  # 成果物の進捗確認

# 成果物の作成指示・検証
openspec instructions <artifact> --change "<name>" --json # 作成指示（context / rules / template）
openspec validate "<name>"                                # 変更の検証

# フェーズゲート検証（PowerShell 版 / Bash 版）
pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose
pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"
./openspec/scripts/verify-gate.sh --gate pre-propose
./openspec/scripts/verify-gate.sh --gate propose --change-name "<name>"
```
