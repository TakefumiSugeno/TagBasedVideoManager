# Design: openspec-git-workflow-rules

## Context

本プロジェクトでは仕様駆動開発（OpenSpec）とブランチ運用・PRマージを統合した開発フローを採用している。
これまでの運用では、Gitブランチの作成タイミングや作業ツリーの保護、PRの作成手順、マージ戦略が開発者の裁量に依存していたため、手順の漏れやブランチの散らかりが発生するリスクがあった。
詳細は `proposal.md` 参照。

## Goals / Non-Goals

**Goals:**

- GitHub CLI (`gh`) の安全な導入と配置（管理者権限不要なユーザー環境インストール）。
- Explore → Propose 移行時の作業ツリー確認・stash退避・develop最新化・トピックブランチ作成の自動安全シーケンスの設計。
- Archive完了後の Spec駆動 PR 作成（OpenSpec成果物を情報源とする自動テンプレート）の設計。
- Merge commit 方式によるマージ戦略とローカルブランチ削除（クリーンアップ）の標準化。
- チェックリスト（`checklist_propose.md`, `checklist_archive.md`）および `config.yaml` への規約反映。

**Non-Goals:**

- アプリケーション本体（F# / Web / GUI）のコード変更。
- GitHub Actions CI/CD パイプラインの新規構築（現フェーズではCLI/ローカル運用の標準化にフォーカス）。

## Decisions

### 1. GitHub CLI のインストール方式

- **採択**: 公式 zip アーカイブを `%LOCALAPPDATA%\Programs\gh` に展開し、ユーザー環境変数 PATH へ追加。
- **理由**: winget の msi インストーラーは UAC / 管理者権限（Error 1603）で失敗するリスクがあるが、zip 展開方式であれば一般ユーザー権限で確実にインストールでき、ポータビリティが高い。
- **代替案**: 管理者権限昇格での winget 実行（ユーザーにUAC許可ダイアログを強制するため不採用）。

### 2. ブランチ切り替え前の安全シーケンス

- **採択**: `git status --porcelain` で未コミット変更を検出し、変更がある場合は `git stash push -u -m "wip: backup before switch to <change-name>"` で退避した上で `develop` を最新化してトピックブランチを作成。
- **理由**: Exploreフェーズでの試行錯誤コードや一時ファイルが残ったままチェックアウトやプルを行うと、コンフリクトや巻き込みコミットのリスクがあるため、確実にstashで保護する。
- **代替案**: 未コミット変更の破棄（`git reset --hard` / `git clean`）（作業内容の喪失につながるため厳禁）。

### 3. PR 作成の Spec 駆動設計

- **採択**: AIエージェントが GitHub CLI (`gh pr create`) を実行し、`proposal.md`, `specs/`, `design.md`, `reviews.md`, `handover.md` から構造化PR本文を生成する。
- **理由**: PRの作成者が人手であるかAIであるかを問わず、OpenSpecのSingle Source of Truthから一貫した情報（What/Why、テスト通過件数、カバレッジ等）がPRに記載されることで、GitHub上のレビュー品質とトレーサビリティが最大化される。

### 4. マージ戦略（Merge Commit）

- **採択**: `gh pr merge --merge --delete-branch` による Merge Commit 運用。
- **理由**: OpenSpecのタスク単位でのコミット、TDD（Red-Green-Refactor）の履歴、およびサブエージェントレビュー履歴の完全性を `develop` ブランチへ残すため。Squash マージではこれらの詳細な変更理由が1コミットに潰れてしまう。

## Risks / Trade-offs

- **[Risk] GitHub CLI の初回認証（未ログイン）**:
  - `gh pr create` 実行時に未認証だと失敗する。
  - **Mitigation**: ユーザーに事前に `gh auth login`（Webブラウザ認証）を実施いただくか、PR作成前に認証チェック（`gh auth status`）を行い、未認証の場合はログイン手順を案内する。
- **[Risk] stash 退避後の pop 忘れ**:
  - 退避した変更が必要な場合に取り出しを忘れる懸念。
  - **Mitigation**: ブランチ切り替え直後に必要に応じて `git stash pop` を実行し、作業ツリーを復元するフローを明確にする。
