# Proposal

## Why

本プロジェクトにおける OpenSpec 運用は、初期の簡易設定と汎用的なチェックリストに基づいており、以下の課題が存在する。

1. **レビューの形骸化とトートロジー**: 形式的なチェックリスト表にチェック記号を埋める運用となっており、AI による同語反復（トートロジー）や追認レビューが発生し、潜在的な不具合やエッジケースの見落としにつながっている。
2. **TDD の自作自演リスク**: 同一エージェントがテストと実装を兼任することで、テスト期待値の無断改ざんや甘いアサーション、テストの skip 放置が発生しやすい構造となっている。
3. **客観的ゲート検証の欠如**: 各フェーズ（Propose / Apply / Archive）の完了判定が目視に依存しており、規律の遵守状況を機械的に検証できない。

別リポジトリ（AIUtils）で先行開発・実証された拡張キット「openspec-kit」および最新の「挑戦的レビューフレームワーク（Adversarial QA / 悪魔の代弁者 / ブラインド突合 / 特化ペルソナ）」を本リポジトリへ導入することで、開発品質、TDD 規律、およびレビュー実効性を大幅に向上させる。

## What Changes

- **サブエージェント6ロール分業（TDD 3権分立）の導入**:
  - `openspec/agents/` 配下に `qa-tester`, `pg-implementer`, `user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer` のシステムプロンプト定義を配置。
  - テスト先行（Red）と本実装（Green/Refactor）を分離し、テストコード改ざんを禁止。
- **成果物7種 × 4アプローチの挑戦的レビューフレームワーク導入**:
  - 旧来のチェックリスト表を廃止し、Adversarial QA（破壊テスト実行による客観判定）、悪魔の代弁者（死角告発・故障シミュレーション）、ブラインド仕様比較（先出し突合）、特化ペルソナ（初心者・コード潔癖症30%削減）によるレビュープロトコルへ全面刷新。
  - `openspec/templates/` 配下の `checklist_*.md`, `review_record.md`, `test_plan.md`, `handover.md`, `pr_body.md` を最新版で置換。
- **適用規模レベル（L1 / L2 / L3）の定義**:
  - 変更規模に応じた成果物作成・レビュー必須要件の最適化。
- **テスト計画書（`test_plan.md`）と C0/C1/C2・同値境界値基準の導入**:
  - L2/L3 での実装前テスト設計の義務化と、網羅性・デシジョンテーブル基準の確立。
- **機械的フェーズゲート検証スクリプトの導入**:
  - `openspec/scripts/verify-gate.ps1` および `verify-gate.sh` を配置し、Gate 0〜3 の客観判定を自動化。
- **SDD ワークフロー定義の配置**:
  - `.agents/workflows/` 配下に `sdd-propose.md`, `sdd-apply.md`, `sdd-archive.md` を配置。
- **AGENTS.md および openspec/config.yaml の再構成**:
  - `AGENTS.md` にマーカー区間（`<!-- openspec-kit:begin -->`）を導入し、キット共通運用ルールと TagBasedVideoManager 固有規約（F#, Avalonia, Alpine, FFmpeg等）を分離して共存。
  - `openspec/config.yaml` を最新キット仕様に適合させつつ、プロジェクト固有の `context` を保全。

## Capabilities

### New Capabilities

- `review-framework`: 成果物別の挑戦的レビュープロトコル（ブラインド比較・故障シミュレーション・破壊テスト実行・悪魔の代弁者）および機械的フェーズゲート検証機能。

### Modified Capabilities

<!-- なし -->

## Non-goals

- プロジェクトの既存コード（F# バックエンド、Avalonia アプリ、Web フロントエンド）の仕様や振る舞いの変更。
- OpenSpec CLI 自体のバイナリやコマンド体系の変更。
- 既存のテストフレームワーク（xUnit, FsUnit, Playwright）の変更。

## Impact

- `openspec/config.yaml`: 6ロール体制・規模レベル・ゲート連携・テスト基準を含む新形式へ更新（プロジェクト固有 context は保持）。
- `openspec/templates/`: 最新の挑戦的レビューおよびテスト計画テンプレートへ置換。
- `openspec/agents/`: 6ロールの定義ファイルを新規追加。
- `openspec/scripts/`: `verify-gate.ps1` / `verify-gate.sh` を新規追加。
- `.agents/workflows/`: SDD ワークフロー定義を新規追加。
- `AGENTS.md`: マーカー区間管理への移行とプロジェクト固有ルールの整理。
- 今後実施されるすべての OpenSpec 変更における開発・レビュー・検証体験。
