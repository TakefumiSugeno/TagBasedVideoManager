---
description: フェーズゲート（Gate 0/1）・適用規模レベル（L1/L2/L3）・テスト計画（test_plan.md）・User/SEサブエージェントレビューを伴うSDD Proposeワークフロー
---

# SDD Propose ワークフロー（Gate 0 → Gate 1）

OpenSpec の標準 `/opsx-propose` に、**ブランチ安全シーケンス（Gate 0）・適用規模レベル（L1/L2/L3）判定・テストケース定義（`test_plan.md`）・サブエージェント批判的レビュー・機械的ゲート検証（Gate 1）** を統合したワークフローです。

## ステップ 1: Gate 0（ブランチ安全シーケンスと事前検証）

`openspec new change "<change-name>"` を実行する**前**に、必ず以下を実行する:

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
   ※ `FAILED` となった場合は後続ステップへ進んではならない。

## ステップ 2: 適用規模レベル（L1 / L2 / L3）と変更種別の決定

変更内容に応じて規模レベルを決定し、`.openspec.yaml`（`level: L1 | L2 | L3`）および `proposal.md` に明記する:

| レベル          | 対象例                                                                 | 必須成果物                                                                                  | Propose レビュー担当            | Apply 時のサブエージェント分業                                                                              |
| :-------------- | :--------------------------------------------------------------------- | :------------------------------------------------------------------------------------------ | :------------------------------ | :---------------------------------------------------------------------------------------------------------- |
| **L1 (軽微)**   | typo修正、設定微調整、1ファイル内の軽微バグ修正                        | `proposal.md`, `tasks.md`（仕様不変なら `skip_specs: true`）                                | `se-reviewer` のみ可            | メインエージェントが Red/Green 実装可 → `pg-reviewer` / `qa-reviewer` でレビュー                            |
| **L2 (通常)**   | 新機能追加、仕様変更、UI変更、通常のバグ修正                           | `proposal.md`, `specs/`, `design.md`, `test_plan.md`, `tasks.md`, （UI変更時）`mockup.html` | `user-reviewer` + `se-reviewer` | **`qa-tester`（Red作成） → `pg-implementer`（Green/Refactor） → `pg-reviewer` / `qa-reviewer`（レビュー）** |
| **L3 (大規模)** | アーキテクチャ変更、破壊的変更、DBスキーマ変更、複数コンポーネント横断 | L2 の全成果物 ＋ 移行計画・ロールバック計画                                                 | `user-reviewer` + `se-reviewer` | L2 の分業に加え、Apply 中も設計変更時は `se-reviewer` レビュー必須                                          |

## ステップ 3: 計画成果物とテスト計画（`test_plan.md`）の作成

1. `openspec new change "<change-name>"` を実行し、標準手順で `proposal.md` → `specs/**/spec.md` → `design.md` を作成する。
2. **UI/UX変更を伴う場合**: 単一ファイル HTML モック（`openspec/changes/<name>/mockup.html`）を作成し、主要状態（初期・空状態・正常・エラー）をブラウザで確認可能にする。
3. **テスト計画（`openspec/changes/<name>/test_plan.md`）の作成（L2/L3 必須）**:
   - `openspec/templates/test_plan.md` をコピーし、以下を具体的に定義する（`qa-tester` サブエージェントに委譲推奨）:
     - C0（80%+/差分90%+）・C1（80%+/差分85%+）・C2（対象100%）・Scenario網羅（100%）の目標とモック境界
     - シナリオ・トレーサビリティマトリクス（`spec.md` の全 Scenario ↔ テストケースID）
     - 単体テストの同値分割・境界値一覧（`min-1`, `min`, `max`, `max+1`, 空/0件/1件/上限, 日時境界）
     - 複合条件のデシジョンテーブル（C2）
     - 結合テスト（異常応答注入・状態遷移 0-switch/1-switch）と E2Eテスト（Happy / Zero-State / Recovery Path）
4. **`tasks.md` の作成**:
   - 各機能・モジュールについて「① `qa-tester` による先行 Red テスト作成・失敗確認タスク」→「② `pg-implementer` による Green 実装・Refactor タスク」→「③ 結合/E2E 検証・エビデンス出力タスク」の順で 2時間以内（3〜5ファイル以内）の粒度に分割する。
   - **不具合修正（`fix`）の場合**: タスク 1.1 は必ず「根本原因（Root Cause）の特定と、不具合を再現する失敗テスト（Red）の作成」とする。

## ステップ 4: サブエージェントによる批判的レビュー（最大 3 サイクル）

1. 別コンテキストのサブエージェントを起動し、`openspec/templates/checklist_propose.md` の全項目を判定させる:
   - **`user-reviewer`**（`openspec/agents/user-reviewer.md` のプロンプト定義を使用）
   - **`se-reviewer`**（`openspec/agents/se-reviewer.md` のプロンプト定義を使用）
2. 結果を `openspec/changes/<name>/reviews.md` に `review_record.md` 形式で追記する。
3. `Critical` / `Major` 指摘がある場合は成果物を修正して再レビューを行う。**最大 3 サイクル**回しても解消しない場合はユーザーへエスカレーションする。

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
