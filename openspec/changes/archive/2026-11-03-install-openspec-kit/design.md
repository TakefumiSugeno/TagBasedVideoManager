# Design

## Context

現状、本リポジトリの OpenSpec 構成（`openspec/config.yaml`, `AGENTS.md`, `openspec/templates/`）は初期のルールセットに基づいており、チェックリスト表形式の追認レビューや、同一エージェントによるテストと実装の兼任といった課題が存在する。
AIUtils リポジトリ（`feature/OpenSpec` ブランチ）にて、これらの課題を抜本的に解決する汎用拡張キット（`openspec-kit`）および最新の挑戦的レビューフレームワーク（脱トートロジー、Adversarial QA、悪魔の代弁者）が完成・実証された。
本設計では、`openspec-kit` の資材を本リポジトリへ安全かつプロジェクト固有規約を損なわずに導入・統合する技術的方針を定義する。

## Goals / Non-Goals

**Goals:**

- AIUtils の `openspec-kit`（テンプレート、エージェント定義、ゲートスクリプト、ワークフロー）を本リポジトリへ導入する。
- `openspec/config.yaml` を最新の6ロール・レベル制・ゲート連携仕様へ移行しつつ、TagBasedVideoManager 固有の技術スタック・制約を保全する。
- `AGENTS.md` にマーカー区間（`<!-- openspec-kit:begin -->`）を導入し、キット共通運用ルールと TagBasedVideoManager 固有実装規約（F#, Avalonia, Alpine, FFmpeg等）を分離・共存させる。
- `verify-gate.ps1` を用いて、機械的フェーズゲート検証（Gate 0〜3）が正しく動作することを確認する。

**Non-Goals:**

- プロダクションコード（F# バックエンド、Avalonia アプリ、Web UI）の変更。
- テストフレームワークや既存テストコードの変更。
- OpenSpec CLI 自体の改変。

## Decisions

### 1. 導入アプローチ: `openspec-kit/install.ps1` の活用と段階的マージ

AIUtils の `openspec-kit/install.ps1` をベースに資材を展開し、プロジェクト固有の設定を上乗せする。

- **理由**: `install.ps1` により、プレースホルダ（`{{BASE_BRANCH}}` → `develop`, `{{EVIDENCE_DIR}}` → `test/TestResults/`）の展開や、ディレクトリ配置（`templates/`, `agents/`, `scripts/`, `.agents/workflows/`）が自動的かつ一貫して行われる。
- **代替案（手動コピー）**: コピー漏れやプレースホルダ未置換のリスクが高いため不採用。

### 2. `openspec/config.yaml` の統合設計

キットの `config.yaml` 雛形をベースに、TagBasedVideoManager の既存コンテキストを融合させる。

- **スキーマ & メタデータ**:
  - `schema: spec-driven`
  - `reviews:` 配下に 6ロール（`qa-tester`, `pg-implementer`, `user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer`）、規模レベル（`L1`, `L2`, `L3`）、ゲートスクリプト、ワークフロー、新テンプレートパスを定義。
- **`context:` セクションの構成**:
  - キット共通ルール（SDD × TDD、6ロール分業、C0/C1/C2・結合・E2E基準、AIガードレール）を定義。
  - プロジェクト固有コンテキスト（F# .NET 10, Giraffe, SQLite/Dapper, HTML5 + Alpine.js + Tailwind CSS, Avalonia.FuncUI, FFmpeg, HTMLモック義務, 1:1ファイル構成）を追記・統合。

### 3. `AGENTS.md` のリファクタリングとマーカー区間導入

- **構成の再編**:
  - マーカー区間外（上部）: プロジェクト概要、構成、利用可能スキル・MCP、プロジェクト固有ルール（自動フォーマット表）
  - マーカー区間内（`<!-- openspec-kit:begin -->` 〜 `<!-- openspec-kit:end -->`）: OpenSpec 運用ルール（6ロール分業、TDD規律、C0/C1/C2基準、DoDとGate 0〜3、Git/PR運用）
  - マーカー区間外（下部）: 実装ガイドライン（F# Idioms, SQLite, ストリーミング, FFmpeg, Web, Avalonia, 環境変数）、参考文献
- **理由**: 将来 `install.ps1` でキットが更新された場合でも、プロジェクト固有の技術規約を壊さずにマーカー区間のみを安全に再同期できるようにする。

## Risks / Trade-offs

- **[Risk] 既存の `openspec validate` や旧形式 change との互換性**
  → **Mitigation**: 過去の change はすでに `openspec/changes/archive/` に格納されており、新スキーマ・新テンプレートの導入後も影響を受けない。`openspec validate` による構文チェックを各段階で実行して整合性を担保する。
- **[Risk] `AGENTS.md` の記述重複によるエージェントの混乱**
  → **Mitigation**: 旧バージョンのレビュー表や重複する Git ルールを完全に削除し、マーカー区間に一元化する。
