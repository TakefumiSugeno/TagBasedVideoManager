# 申し送り事項 (Handover): openspec-git-workflow-rules

## 1. 既知の課題・残タスク

- **GitHub CLI (`gh`) の初回認証**:
  - `gh.exe` はユーザー環境にインストール済みですが、初回のみ対話認証（`gh auth login`）が必要です。
  - 次回以降の自動PR作成に向けて、ターミナルで `gh auth login` を実行してブラウザ認証を完了してください。

## 2. 将来的な改善・拡張候補

- **GitHub Actions による PR 自動ラベル付け**:
  - `feature/*`, `fix/*` などのブランチ名に応じた自動ラベル付与の検討。
- **PR テンプレートファイル（`.github/pull_request_template.md`）の配置**:
  - Web UI から手動でPRを作成する場合にも今回策定した Spec 駆動フォーマットが初期入力されるよう、リポジトリ直下にテンプレートを配備する検討。
