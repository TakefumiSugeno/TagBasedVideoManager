---
name: sdd-propose
description: "OpenSpec の変更提案（Propose）フェーズを、ブランチ安全シーケンス（Gate 0）・適用規模レベル（L1/L2/L3）判定・テスト計画（test_plan.md）作成・User/SE サブエージェントの批判的レビュー・機械的ゲート検証（Gate 1）付きで実行する。新しい変更の提案、openspec new change の実行前後、propose / sdd-propose / 変更提案の作成を依頼されたときに使う。"
---

# SDD Propose（Gate 0 → Gate 1）

OpenSpec の標準提案手順に、**ブランチ安全シーケンス（Gate 0）・適用規模レベル（L1/L2/L3）判定・テストケース定義（`test_plan.md`）・サブエージェント批判的レビュー・機械的ゲート検証（Gate 1）** を統合した、5 エージェント（Antigravity CLI / Claude Code / GitHub Copilot CLI / OpenCode CLI / Hermes Agent）共通のスキルです。

- 詳細リファレンス（規模レベル表・6 ロール表・カバレッジ基準・ゲート表・Git 操作・CLI）: [references/sdd-reference.md](references/sdd-reference.md)
- Antigravity CLI 実行時の専用手順（モデル指定・権限分離・並列実行・障害回復）: [references/antigravity-orchestration.md](references/antigravity-orchestration.md)

## 実行環境とサブエージェント分業（フォールバック）

本スキルの手順は特定 CLI 専用ツールを必須前提としません。レビュー・テスト計画作成は「作成者とは別の独立コンテキスト」で行い、実行環境に応じて次のとおり分業方式を選びます。

| 実行環境               | 分業方式                                                                                                                                                |
| :--------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Antigravity CLI        | [references/antigravity-orchestration.md](references/antigravity-orchestration.md) に従い、ロール別モデル・権限を指定したサブエージェントで実行する     |
| Claude Code            | `.claude/agents/` に展開された 6 ロール（`openspec/agents/*.md` と同一内容）を標準サブエージェントとして起動する                                        |
| GitHub Copilot CLI     | 標準のカスタムエージェント／サブエージェント機構があれば `openspec/agents/<role>.md` を指示として起動する                                               |
| OpenCode CLI / Hermes  | 各エージェント標準のサブエージェント（subagent）機構があれば `openspec/agents/<role>.md` を指示として起動する                                           |
| 上記機構が使えない環境 | **フォールバック**: 新規セッション（独立コンテキスト）を開き、ロール定義の本文を指示として読ませ、対象成果物のみを渡して**直列**に 1 ロールずつ実行する |

どの方式でも次の原則を守ります。

- **独立コンテキスト**: レビュアーに作成者の思考過程を渡さず、対象成果物・`git diff`・チェックリストのパスのみを渡す。
- **単一ライター**: `reviews.md` と `tasks.md` を編集するのは本スキルを実行している親（メイン）エージェントのみ。サブエージェントは `review_record.md` 形式のテキストまたは作業サマリを返却し、ファイルへは書き込まない。
- Antigravity 専用ツール（`define_subagent` / `run_workflow` / `swarm`）が無い環境でも、本ファイルの手順だけで完遂できる。

## ステップ 1: Gate 0（ブランチ安全シーケンスと事前検証）

`openspec new change "<change-name>"` を実行する**前**に、必ず以下を実行する。

1. 未コミット変更の確認と退避:
   ```bash
   git status --porcelain
   # 変更がある場合は破棄せず退避
   git stash push -u -m "wip: backup before switch to <change-name>"
   ```
2. ベースブランチの最新化とマージ済みブランチ削除・新トピックブランチ作成:
   ```powershell
   # PowerShell 環境
   git checkout develop
   git pull origin develop
   git fetch --prune
   # マージ済みローカルブランチを一括削除（develop, main, master 等は除外保護）
   git branch --merged develop | Where-Object { $_.Trim() -notmatch '^(develop|main|master|\*)' } | ForEach-Object { git branch -d $_.Trim() }
   git checkout -b feature/<change-name>   # バグ修正は fix/<change-name>、リファクタは refactor/<change-name>
   ```
   ```bash
   # Bash 環境
   git checkout develop
   git pull origin develop
   git fetch --prune
   # マージ済みローカルブランチを一括削除（develop, main, master 等は除外保護）
   git branch --merged develop | grep -vE "^(\*|develop|main|master)$" | xargs -r git branch -d
   git checkout -b feature/<change-name>
   ```
3. **Gate 0 検証スクリプトの実行**:
   ```powershell
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose
   ```
   ※ `FAILED` となった場合は後続ステップへ進んではならない（Bash 環境: `./openspec/scripts/verify-gate.sh --gate pre-propose`）。

## ステップ 2: 適用規模レベル（L1 / L2 / L3）と変更種別の決定

変更内容に応じて規模レベルを決定し、`.openspec.yaml`（`level: L1 | L2 | L3`。`openspec` CLI が未知キー警告を出す場合は `x-level: L1 | L2 | L3` とし `proposal.md` 先頭にも規模レベルを明記）および `proposal.md` に明記する。

| レベル          | 対象例                                                                 | 必須成果物                                                                                  | Propose レビュー担当            | Apply 時のサブエージェント分業                                                                              |
| :-------------- | :--------------------------------------------------------------------- | :------------------------------------------------------------------------------------------ | :------------------------------ | :---------------------------------------------------------------------------------------------------------- |
| **L1 (軽微)**   | typo修正、設定微調整、1ファイル内の軽微バグ修正                        | `proposal.md`, `tasks.md`（仕様不変なら `skip_specs: true`）                                | `se-reviewer` のみ可            | メインエージェントが Red/Green 実装可 → `pg-reviewer` / `qa-reviewer` でレビュー                            |
| **L2 (通常)**   | 新機能追加、仕様変更、UI変更、通常のバグ修正                           | `proposal.md`, `specs/`, `design.md`, `test_plan.md`, `tasks.md`, （UI変更時）`mockup.html` | `user-reviewer` + `se-reviewer` | **`qa-tester`（Red作成） → `pg-implementer`（Green/Refactor） → `pg-reviewer` / `qa-reviewer`（レビュー）** |
| **L3 (大規模)** | アーキテクチャ変更、破壊的変更、DBスキーマ変更、複数コンポーネント横断 | L2 の全成果物 ＋ 移行計画・ロールバック計画                                                 | `user-reviewer` + `se-reviewer` | L2 の分業に加え、Apply 中も設計変更時は `se-reviewer` レビュー必須                                          |

## ステップ 3: 計画成果物とテスト計画（`test_plan.md`）の作成

1. `openspec new change "<change-name>"` を実行し、標準手順（`openspec instructions <artifact> --change "<change-name>" --json`）で `proposal.md` → `specs/**/spec.md` → `design.md` を作成する。
2. **UI/UX変更を伴う場合**: 単一ファイル HTML モック（`openspec/changes/<name>/mockup.html`）を作成し、主要状態（初期・空状態・正常・エラー）をブラウザで確認可能にする。
3. **テスト計画（`openspec/changes/<name>/test_plan.md`）の作成（L2/L3 必須）**:
   - `openspec/templates/test_plan.md` をコピーし、以下を具体的に定義する（`qa-tester` サブエージェントへの委譲を推奨）:
     - C0（80%+/差分90%+）・C1（80%+/差分85%+）・C2（対象100%）・Scenario網羅（100%）の目標とモック境界
     - シナリオ・トレーサビリティマトリクス（`spec.md` の全 Scenario ↔ テストケースID）
     - 単体テストの同値分割・境界値一覧（`min-1`, `min`, `max`, `max+1`, 空/0件/1件/上限, 日時境界）
     - 複合条件のデシジョンテーブル（C2）
     - 結合テスト（異常応答注入・状態遷移 0-switch/1-switch）と E2Eテスト（Happy / Zero-State / Recovery Path）
4. **`tasks.md` の作成**:
   - 各機能・モジュールについて「① `qa-tester` による先行 Red テスト作成・失敗確認タスク」→「② `pg-implementer` による Green 実装・Refactor タスク」→「③ 結合/E2E 検証・エビデンス出力タスク」の順で 2時間以内（3〜5ファイル以内）の粒度に分割する。
   - **不具合修正（`fix`）の場合**: タスク 1.1 は必ず「根本原因（Root Cause）の特定と、不具合を再現する失敗テスト（Red）の作成」とする。

## ステップ 4: サブエージェントによる批判的レビュー（最大 3 サイクル）

1. 独立コンテキストのサブエージェント（またはフォールバックの直列実行）で、`openspec/templates/checklist_propose.md` の全項目を判定させる（**L1 は `se-reviewer` のみ可、L2 / L3 は `user-reviewer` + `se-reviewer` の両方必須**）。
   - **`user-reviewer`**（`openspec/agents/user-reviewer.md` のロール定義を使用。L2 / L3 で実施）
   - **`se-reviewer`**（`openspec/agents/se-reviewer.md` のロール定義を使用。L1 / L2 / L3 で実施）
   - 各レビュアーには変更名・対象成果物（`openspec/changes/<name>/` 配下）・チェックリストのパスを渡し、`openspec/templates/review_record.md` 形式のテキストで返却させる（レビュアー自身は `reviews.md` を編集しない）。
   - Antigravity CLI では `scripts/run_propose_review.py` を `run_workflow` で実行すると 2 ロールを並列起動できる（手順: [references/antigravity-orchestration.md](references/antigravity-orchestration.md)）。他環境では 2 ロールを順に直列実行する。
2. 親エージェントが返却テキストを `openspec/changes/<name>/reviews.md` に `review_record.md` 形式で追記する（単一ライター）。
3. `Critical` / `Major` 指摘がある場合は成果物を修正して再レビューを行う。**最大 3 サイクル**回しても解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
4. `Minor` / `Suggestion` で保留とした指摘は `reviews.md` に記録し、Archive 時に `handover.md` へ転記する。

## ステップ 5: Gate 1（Propose 完了ゲート検証）とユーザー合意・コミット

1. OpenSpec 検証とフェーズゲート検証を実行する:
   ```powershell
   openspec validate "<change-name>"
   pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<change-name>"
   ```
2. 両方が PASS したことを確認し、計画内容・`test_plan.md`・`mockup.html`・レビュー結果をユーザーへ提示して **実装着手の合意** を得る。
3. 自動フォーマットを実行し、`openspec/changes/<name>/` 一式をコミットする:
   ```bash
   git add "openspec/changes/<change-name>"
   git commit -m "spec(<change-name>): Propose成果物・テスト計画・レビュー記録の作成"
   ```

次フェーズ: Apply は `sdd-apply` スキルを使用する。
