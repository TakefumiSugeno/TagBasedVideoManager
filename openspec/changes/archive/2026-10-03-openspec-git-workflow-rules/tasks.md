# Tasks: openspec-git-workflow-rules

## 1. ツール環境セットアップと動作確認

- [x] 1.1 GitHub CLI (`gh`) のユーザー環境配備と PATH 登録の確認（`gh --version` の実行検証）

## 2. ドキュメント・設定・レビューテンプレート改定

- [x] 2.1 `AGENTS.md` の「Git操作ルール」にブランチ戦略（stash安全退避シーケンス、develop最新化、Featureブランチ作成）、AIによるSpec駆動PR作成、Merge commitマージ規約を反映する
- [x] 2.2 `openspec/config.yaml` の `rules.proposal` および `operations.archive.guidance` に安全ブランチシーケンスとghによるPR作成・マージ規約を反映する
- [x] 2.3 `openspec/templates/checklist_propose.md`（観点15: Featureブランチ確認）および `checklist_archive.md`（観点20: PR作成準備確認）にチェック項目を追加する

## 3. 全体検証とフォーマット確認

- [x] 3.1 Prettier による Markdown / YAML ファイルの自動フォーマット実行と OpenSpec 構文検証（`openspec validate`）
