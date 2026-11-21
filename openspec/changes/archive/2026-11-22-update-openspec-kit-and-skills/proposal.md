# Proposal: update-openspec-kit-and-skills

## Meta

- **Level**: L2
- **Type**: chore
- **Application Versioning (SemVer 2.0.0)**: Web アプリ（`TagBasedVideoManager`）・デスクトップアプリ（`TagBasedVideoManager.Renamer`）のプロダクションコードには変更がないため、両プロジェクトの `<Version>` は据え置きとする。

## Why

本リポジトリの OpenSpec 環境は 2026-11-03 導入時点の構成のまま残っており、参照元である `D:\programming\repos\MyGitHubRepos\AIs\AIUtils`（`main` ブランチ / `e98b6b1`）の最新 `openspec-kit` および OpenSpec CLI `1.15.0` との間に以下の乖離と運用上の問題が生じている。

1. **`AGENTS.md` の 24,000 バイト上限超過**: 現在の `AGENTS.md` は 31,996 バイトあり、Antigravity CLI のルールファイル上限（24,000 バイト）を約 8 KB 超過しているため末尾のコマンドリファレンス等が切り捨てられるリスクがある。
2. **非推奨ワークフロー依存と既存エージェント横断展開の不足**: `.agents/workflows/sdd-{propose,apply,archive}.md` は Antigravity 2.0 以降で非推奨の旧形式であり、本リポジトリに存在する `.github/`・`.opencode/`・`.hermes/` へ SDD 手順が展開されていない。
3. **Gemini 4 モデル役割分担・権限分離の未導入**: `openspec/agents/*.md` に `model_tier` / `thinking` / `write_tools` メタデータおよび単一ライター契約がなく、`run_workflow` による並列レビュー手順も整備されていない。
4. **OpenSpec CLI 生成スキルの陳腐化**: 環境の CLI は `1.15.0` だが、リポジトリ内の `openspec-*` スキル・コマンド等は `1.13.2` 生成のままである。

## What Changes

- **`openspec-kit` 最新版（`AIUtils` `main` @ `e98b6b1`）の取り込み**:
  - `openspec/agents/*.md` を `model_tier`（`pro` / `flash`）・`thinking`（`high` / `medium`）・`write_tools`（`true` / `false`）および単一ライター契約付き定義へ更新する。
  - `openspec/templates/` および `openspec/scripts/` を `AIUtils` 最新版と同期する（改行・ベースブランチ `develop` 維持）。
  - `install.ps1` を `-AgentTargets auto -Force -SkipConfig` で実行し、本リポジトリに既に存在する **4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）** 配下へ 5 エージェント互換規格の SDD Agent Skills（`sdd-propose`, `sdd-apply`, `sdd-archive`、自己完結 `references/` および `run_workflow` 用 `scripts/`）を展開する（`.claude/` は新規作成せず、`.gemini/` には `sdd-*` を展開しない）。
  - `install.ps1` 実行により一時退避される旧 `.agents/workflows/sdd-{propose,apply,archive}.md.bak*` は、本リポジトリ固有のカスタム編集が存在しないことを事前確認済みのため、移行確認後に削除して AI による誤参照・コンテキスト浪費を防ぐ（同ディレクトリの `opsx-*.md` は保持）。
  - **`AGENTS.md` 固有ルールの移設と管理区間スリム化**:
    - 現行 `AGENTS.md` の**管理区間内（157〜166 行目）に混在していた固有ルール「セマンティックバージョニング（SemVer 2.0.0）規則と運用（X/Y/Z 桁と L3/L2/L1 規模レベル連動表）」を、管理区間外（103〜106 行目付近）へ移設**し、管理区間外の `## 利用可能なSkills` へ `sdd-propose`, `sdd-apply`, `sdd-archive` を追記する。
    - その上で `openspec-kit` 管理区間をスリム版 `AGENTS.openspec.md`（11,580 バイト）で置換し、総サイズを約 22,000 バイト前後（24,000 バイト未満）へ圧縮する。
    - ルート直下に既存の `CLAUDE.md` / `GEMINI.md`（`@AGENTS.md`）は無改変で保持する。
  - `openspec/config.yaml` はプロジェクト固有設定（F#/.NET 10/Avalonia/Alpine 技術スタック、SemVer 2.0.0、HTML モック要件、`test/TestResults/`、`chore` 許可）をすべて保持したまま、`reviews.workflows:` を `reviews.skills:`（`.agents/skills/sdd-{propose,apply,archive}/SKILL.md`）へ更新する。
- **OpenSpec CLI 標準スキルの更新（`openspec update`）**:
  - `openspec update` を実行し、既存の `.agents/`, `.gemini/`, `.github/`, `.hermes/`, `.opencode/` 配下の `openspec-*` スキルおよび `opsx-*` コマンド／プロンプト（事前 `--dry-run` 確認済み 53 ファイル）を `1.13.2` → `1.15.0` へ更新する。

## Capabilities

### New Capabilities

<!-- なし（既存の review-framework 仕様を拡張して管理する） -->

### Modified Capabilities

- `review-framework`: 6 ロール定義のモデルティア・書き込み権限メタデータ契約、既存 4 エージェント向け `sdd-*` Skills 配布（旧 `sdd-*.md` ワークフロー撤去・`opsx-*` / `.gemini/` 共存）、`run_workflow` 並列レビュースクリプト、外部前提・誤操作復旧境界、および `AGENTS.md` の 24,000 バイト上限遵守・SemVer 連動表移設保全を追加・更新する。

## Non-goals

- `.claude/` ディレクトリの新規作成および Claude Code 向け展開（`-AgentTargets auto` により既存エージェントディレクトリのみを対象とする）。
- `TagBasedVideoManager` / `TagBasedVideoManager.Renamer` のプロダクションコード、テストコード、およびプロジェクト固有ルール（SemVer 規則・自動フォーマット表・実装ガイドライン等）の仕様変更。
- `openspec/scripts/verify-gate.ps1` / `verify-gate.sh` および `AIUtils` 側スクリプト本体（`install.ps1`・`run_*_review.py`）の独自改修（上流改善候補は `handover.md` へ申し送る）。

## Acceptance Criteria

### Given / When / Then

1. **Given**: 現在の `TagBasedVideoManager` リポジトリ（`.agents/`, `.gemini/`, `.github/`, `.opencode/`, `.hermes/`, `CLAUDE.md`, `GEMINI.md` が存在し、`.claude/` は存在しない）
   **When**: `AGENTS.md` 固有 SemVer 表の管理区間外移設、`install.ps1 -AgentTargets auto -Force -SkipConfig` 適用、`.bak` 削除、`openspec/config.yaml` の `reviews.skills` 更新、および `openspec update` を実施する
   **Then**: 4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）の `skills/sdd-{propose,apply,archive}/` に同一内容のスキル一式が展開され、`.claude/` は作成されず、`CLAUDE.md` / `GEMINI.md` は無改変で維持され、`.agents/workflows/sdd-*.md*` は完全に除去され、`openspec update --dry-run --json` で対象 53 ファイルの差分が `changes: []`（`1.15.0` 同期済み）となること。
2. **Given**: Prettier 整形および UTF-8（BOM なし）保存後の `AGENTS.md` と `openspec/config.yaml`
   **When**: ファイルサイズ、マーカー整合、固有記述、および `openspec instructions` 出力を検証する
   **Then**: 管理区間外の固有ルール（移設した SemVer × 規模レベル連動表を含む）が欠落なく保持され、`AGENTS.md` 総バイト数が `24,000` バイト未満であり、`openspec instructions apply`・`openspec validate "update-openspec-kit-and-skills"`・`verify-gate.ps1` がすべて正常通過すること。
3. **Given**: 展開された `openspec/agents/*.md` および 4 エージェント配下の `run_{propose,archive}_review.py`
   **When**: メタデータ検査および Python 直接実行ガード検証を行う
   **Then**: `user/se/pg-reviewer` が `pro`/`high`/`write_tools: false`、`qa-reviewer` が `pro`/`high`/`write_tools: true`、`qa-tester` が `flash`/`high`/`write_tools: true`、`pg-implementer` が `flash`/`medium`/`write_tools: true` であり、`.py` スクリプトを `python` で直接実行した場合は終了コード `2` で安全停止すること。

## Impact

- `AGENTS.md`: SemVer × 規模レベル連動表と `sdd-*` スキル一覧の管理区間外整備、および `openspec-kit:begin`〜`end` 区間のスリム化更新（31,996 B → 約 22 KB）
- `openspec/config.yaml`: `reviews.workflows` → `reviews.skills` への参照更新
- `openspec/agents/*.md`, `openspec/templates/*`, `openspec/scripts/*`: 最新 `openspec-kit` への同期
- `.agents/skills/sdd-*`, `.github/skills/sdd-*`, `.opencode/skills/sdd-*`, `.hermes/skills/sdd-*`: 新規展開
- `.agents/workflows/sdd-*.md`: 退避後削除（`opsx-*.md` は保持）
- `.agents/`, `.gemini/`, `.github/`, `.hermes/`, `.opencode/` 配下の `openspec-*` / `opsx-*`: `openspec update` による `1.15.0` 更新
