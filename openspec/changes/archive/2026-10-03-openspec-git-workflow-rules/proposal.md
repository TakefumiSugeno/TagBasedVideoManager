# Proposal: openspec-git-workflow-rules

## Why

本プロジェクトにおける OpenSpec 運用において、要件探索（Explore）から変更提案（Propose）への移行時にブランチ作成を忘れて `develop` 上で直接作業してしまうリスクや、未コミット変更の予期せぬコンフリクト、手動PR作成に伴う情報欠落、マージ方式の不統一（コミット履歴の喪失）といった課題が存在していた。
これを解決するため、GitHub CLI (`gh`) の導入、安全なブランチ作成シーケンス（作業ツリーの stash 退避と確認）、Spec 駆動による PR 自動生成、および Merge Commit によるマージ規約を標準ワークフローとして体系化・明文化する。

## What Changes

- **GitHub CLI (`gh`) の導入**: ターミナルから直接 PR 作成・マージを行える環境を整備（v2.102.0 配備）。
- **Explore → Propose 安全シーケンスの義務化**:
  - `git status --porcelain` による作業ツリー確認。
  - 未コミット変更がある場合の `git stash push -u -m "wip: backup..."` 自動退避。
  - `git checkout develop` && `git pull origin develop` による最新化。
  - `git checkout -b feature/<change-name>`（または `fix/*` 等）の作成と確認。
- **AI による GitHub CLI (`gh`) を用いた PR 作成（Spec 駆動）**:
  - Archive 完了コミット push 後、AI が OpenSpec の成果物（`proposal.md`, `specs/`, `design.md`, `reviews.md`, `handover.md`）から構造化された PR 本文を自動生成して `gh pr create` を実行。
- **マージ戦略の統一（Merge Commit 運用）**:
  - TDD サイクルおよびタスク別コミット履歴を完全保持するため、PR マージ方式を `Merge Commit`（`gh pr merge --merge --delete-branch`）に限定（Squash/Rebase 禁止）。
  - マージ完了後のローカル develop 最新化およびローカルブランチ削除手順の定義。
- **レビューチェックリストへの観点追加**:
  - `checklist_propose.md`: 適切な Feature ブランチ上で作業されているかの確認（観点15）。
  - `checklist_archive.md`: PR 作成準備および Merge Commit 方針の確認（観点20）。

## Non-goals

- 本プロジェクトのアプリケーション機能コード（F# ソースコード、Web フロントエンド、テストコード等）の変更は対象外（開発運用規約および OpenSpec 設定・テンプレートのみの改定）。

## Capabilities

_(本変更は開発運用・ツーリング・ドキュメント規約の改定であるため、システム要件のデルタ仕様は作成せず `skip_specs: true` を適用)_

## Impact

- 影響範囲:
  - `AGENTS.md`
  - `openspec/config.yaml`
  - `openspec/templates/checklist_propose.md`
  - `openspec/templates/checklist_archive.md`
  - 開発者および AI エージェントの Git / OpenSpec 運用フロー
