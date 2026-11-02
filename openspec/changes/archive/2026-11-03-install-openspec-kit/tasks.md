# Tasks

## 1. openspec-kit 資材の配置・同期

- [x] 1.1 `AIUtils/openspec-kit/install.ps1` を実行（`-Force -SkipConfig`）し、最新テンプレート（`openspec/templates/`）、エージェント定義（`openspec/agents/`）、ゲートスクリプト（`openspec/scripts/`）、ワークフロー（`.agents/workflows/`）を本リポジトリに展開・配置し、各ファイルが存在することを確認する
- [x] 1.2 `.gitignore` にテストエビデンス出力先（`test/TestResults/`）が登録されていることを確認する

## 2. openspec/config.yaml の統合・設定

- [x] 2.1 AIUtils の `openspec-kit/config.yaml`（6ロール、L1-L3レベル、ゲートスクリプト、ワークフロー、新テンプレート）をベースに、TagBasedVideoManager の固有コンテキスト（F# .NET 10, Giraffe, SQLite, Avalonia.FuncUI, Alpine.js + Tailwind CSS, FFmpeg等）を統合した `openspec/config.yaml` を作成・反映する
- [x] 2.2 `openspec validate --change "install-openspec-kit"` を実行し、設定および変更アーティファクトにエラーがないことを確認する

## 3. AGENTS.md のリファクタリング・マーカー区間導入

- [x] 3.1 既存 `AGENTS.md` の旧運用ルール（旧レビュー表など）を整理し、`<!-- openspec-kit:begin -->` 〜 `<!-- openspec-kit:end -->` マーカー区間に最新の OpenSpec 運用ルール（6ロール分業、挑戦的レビュー、C0-C2基準、DoDとGate 0〜3）を組み込む
- [x] 3.2 TagBasedVideoManager 固有部分（プロジェクト概要、構成、自動フォーマット表、実装ガイドライン、参考文献）がマーカー区間外に完全に保持されていることを確認する

## 4. ゲート検証・動作確認

- [x] 4.1 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose` を実行し、Gate 0 が正常に PASS することを確認する
- [x] 4.2 `openspec/templates/review_record.md` に基づいて `openspec/changes/install-openspec-kit/reviews.md` を作成し、Propose レビュー（`user-reviewer` および `se-reviewer`）を実施して記録する
- [x] 4.3 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "install-openspec-kit"` を実行し、Gate 1 が正常に PASS することを確認する

## Workflow follow-up

- 全タスク完了後、総括レビュー（Gate 3）および `handover.md` を作成する。
- `openspec sync specs` を実行し、メイン仕様（`openspec/specs/`）へ `review-framework` を同期する。
- `openspec archive "install-openspec-kit"` でアーカイブし、トピックブランチから PR を作成する。
