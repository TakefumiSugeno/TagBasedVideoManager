# テスト計画・テストケース定義書 (Test Plan): update-openspec-kit-and-skills

- **変更名**: `update-openspec-kit-and-skills`
- **適用規模レベル**: `L2`
- **変更種別**: `chore`（OpenSpec ツールキット・スキル構成および設定の更新）

---

## 1. 検証方針とモック境界

本変更はプロダクションコード（`src/`）およびアプリテストコード（`test/`）を変更しない設定・運用基盤の更新であるため、C0/C1/C2 アプリケーションコードカバレッジ計測は **対象外** とする（なお参照元 `AIUtils` リポジトリの `tests/openspec-kit.*.Tests.ps1` にキット側の単体テスト群が存在するが、本変更では外部テスト結果に依存せず、**本リポジトリへ展開された全成果物・境界条件・実行パスを直接検証する**）。

- **検証実行形態**: 検証コマンドおよび一時インライン検証スクリプトは一時ディレクトリまたは標準出力上で実行し、本リポジトリの `test/` 配下へ不要なテストコードを残さない。
- **モック境界方針**:
  - **実物を使用する範囲**: 本リポジトリ配下の `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `openspec/`, `.agents/`, `.github/`, `.opencode/`, `.hermes/`, `.gemini/`、および実 `pwsh` / `python` / `openspec` CLI / `npx prettier`。
  - **モック化する境界（制約明記）**: `UT-04` の `run_{propose,archive}_review.py` 論理検証では、Antigravity `run_workflow` ランタイム提供シンボル（`agent`, `parallel`, `phase`, `log`, `args`）のみを最小スタブ注入する。したがって本検証はスクリプトの引数バリデーションと構造契約（`model="pro"`, 対象ロール名）を確認するものであり、Antigravity 実ランタイムの `agent()` API シグネチャ変更までは保証しない。

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                                  | Scenario 名                                         | 検証種別 | 対応ケースID               |
| :------------------------------------------------------------- | :-------------------------------------------------- | :------: | :------------------------- |
| `review-framework` / サブエージェント6ロール分業               | モデルティアと書き込み権限の分離契約                | 構造検証 | `UT-01`, `UT-02`           |
| `review-framework` / サブエージェント6ロール分業               | 単一ライター原則および規約違反時の差し戻し          | 構造検証 | `UT-02`                    |
| `review-framework` / 既存エージェント向け SDD スキル配布       | 既存 4 エージェントのみへの展開とエントリー境界維持 | 統合検証 | `IT-01`, `E2E-01`, `DT-01` |
| `review-framework` / 既存エージェント向け SDD スキル配布       | 旧 SDD ワークフロー完全撤去と CLI 1.15.0 共存       | 統合検証 | `IT-02`, `E2E-02`          |
| `review-framework` / 既存エージェント向け SDD スキル配布       | `run_workflow` スクリプトの実行ガードとエラー境界   | 境界検証 | `UT-03`, `UT-04`           |
| `review-framework` / 既存エージェント向け SDD スキル配布       | 外部前提不在または手動設定ミス時の検知と安全停止    | 異常検証 | `IT-04`, `E2E-03`          |
| `review-framework` / ルールファイル（AGENTS.md）サイズ上限遵守 | 更新・整形後 `AGENTS.md` 24KB 未満と SemVer 表保全  | 境界検証 | `UT-05`, `UT-06`           |
| `review-framework` / ルールファイル（AGENTS.md）サイズ上限遵守 | `AGENTS.md` サイズ超過およびマーカー破損時の異常系  | 異常検証 | `E2E-03`                   |
| `review-framework` / ルールファイル（AGENTS.md）サイズ上限遵守 | `openspec/config.yaml` 固有設定保持・パース検証     | 統合検証 | `IT-03`                    |

---

## 3. 単体・境界値検証ケース定義

| ケースID | 対象資産                                                                     | 検証観点（区分）             | 入力・条件                                                                                           | 期待される結果                                                                                                                                                    |
| :------- | :--------------------------------------------------------------------------- | :--------------------------- | :--------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `UT-01`  | `openspec/agents/*.md`                                                       | メタデータ契約（正常系）     | 6 ロール全ファイルの YAML frontmatter を解析                                                         | `user/se/pg-reviewer`=`pro/high/false`, `qa-reviewer`=`pro/high/true`, `qa-tester`=`flash/high/true`, `pg-implementer`=`flash/medium/true`                        |
| `UT-02`  | `openspec/agents/*.md`, `skills/sdd-*`                                       | 禁止モデル・単一ライター確認 | 4 エージェント配下の全 `sdd-*` と `openspec/agents/*.md` を走査                                      | 推奨モデルに `3.8` / `gemini-3.8-flash` が存在せず、単一ライター契約が記載されていること                                                                          |
| `UT-03`  | `.{agents,github,opencode,hermes}/skills/sdd-{propose,archive}/scripts/*.py` | 直接実行ガード（異常系境界） | **4 エージェントすべて** の `run_propose_review.py` / `run_archive_review.py` を `python` で直接実行 | 全 8 ファイルが `NameError` なく、代替案内を stderr へ出力して終了コード `2` で停止すること                                                                       |
| `UT-04`  | `.agents/skills/sdd-{propose,archive}/scripts/*.py`                          | 正常スタブ実行・入力境界     | `args` 欠落、空文字、記号始まり（`"-bad"`）、正常名（`"sample-1"`）を注入実行                        | 不正入力は `ValueError` で拒否され、正常入力は `user/se`（Propose）または 4 ロール（Archive）を `model="pro"` で呼び出すこと                                      |
| `UT-05`  | `AGENTS.md`（Prettier 整形後）                                               | サイズ上限境界（`< 24000`）  | Prettier 整形後の UTF-8（BOM なし）実バイト数・管理区間内外の内訳を計測                              | 先頭 3 バイトが UTF-8 BOM（`EF BB BF`）でなく、総バイト数が `24,000` バイト未満、マーカー開始/終了が各 1 回正しい順序で出現すること                               |
| `UT-06`  | `AGENTS.md`（Prettier 整形後）                                               | 固有記述・SemVer 表保全      | 管理区間外および新 `## 利用可能なSkills` を検査                                                      | 「プロジェクト概要」「構成」「自動フォーマット」「実装ガイドライン」「SemVer 2.0.0（L3/L2/L1 連動表含む）」「参考情報」「`sdd-*` スキル記載」がすべて存在すること |

---

## 4. 複合条件デシジョンテーブル（C2）

| ケースID | `.agents/` 存在 | `.claude/` 存在 | `CLAUDE.md` 存在 | `.github/.opencode/.hermes` 存在 | 期待される配置先・挙動                                                                                    |
| :------- | :-------------: | :-------------: | :--------------: | :------------------------------: | :-------------------------------------------------------------------------------------------------------- |
| `DT-01`  |        T        |        F        |        T         |                T                 | 4 エージェントの `skills/sdd-*` 展開、`.claude/` 未作成、`CLAUDE.md` / `GEMINI.md` 無改変維持             |
| `DT-02`  |        T        |        T        |        T         |                T                 | `.claude/skills/sdd-*` と `.claude/agents/*` も展開される仕様境界（一時ディレクトリ上のドライランで確認） |

---

## 5. 結合・E2E 検証ケース定義

| ケースID | 検証種別                      | 手順（Given → When）                                                                                                                                                                                  | 期待される結果（Then）                                                                                                                                                                 |
| :------- | :---------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IT-01`  | **事前ドライラン・同一性**    | `install.ps1 -AgentTargets auto -Force -SkipConfig -WhatIf` 実行後、本実行し 4 エージェントの `skills/sdd-*` の SHA256 と未置換プレースホルダを検査                                                   | `-WhatIf` は無書き込み、本実行後は 4 エージェントの全 `sdd-*` ファイルが完全一致し `{{BASE_BRANCH}}` / `{{EVIDENCE_DIR}}` 残存が 0 件であること                                        |
| `IT-02`  | **CLI 同期・冪等性検証**      | `openspec update` 実行後、再度 `openspec update --dry-run --json` および `install.ps1`（再実行）結果を確認                                                                                            | 事前確認 53 ファイル更新後に `openspec update --dry-run --json` の `changes` が空配列（`[]`）となり、`openspec-*` が `1.15.0`、`sdd-*` が保持され、`.claude/` 未作成であること         |
| `IT-03`  | **設定・両ゲート検証**        | `openspec/config.yaml` 更新後、`openspec instructions apply`、`openspec validate`、`verify-gate.ps1`（`x-level: L2` フォールバック境界含む）、および `verify-gate.sh`（または Bash 環境確認）を実行   | `openspec instructions apply` が固有 `context` / `rules` をエラーなく返し、`openspec validate`・`verify-gate.ps1`（Level=`L2` 判定）・`verify-gate.sh` が終了コード `0` で通過すること |
| `IT-04`  | **前提環境確認**              | 参照元 `AIUtils` パス、`openspec --version`（`1.15.0`）、`pwsh`、`python`、`npx` の存在・バージョンを確認                                                                                             | 全必須ツールと参照元リポジトリが利用可能であること                                                                                                                                     |
| `E2E-01` | **Happy Path**                | `AGENTS.md` 移設→`install.ps1`→`.bak` 削除→`config.yaml` 更新→`openspec update`→Prettier 整形を完了し、既存テスト影響有無を確認                                                                       | 4 エージェントへ `sdd-*` 展開、`AGENTS.md` `< 24KB`、Prettier 整形後の全チェックが一貫して通過すること                                                                                 |
| `E2E-02` | **不要物排除・共存**          | `.claude/` 不在、`CLAUDE.md`/`GEMINI.md` 無改変（`git diff` なし）、`.agents/workflows/sdd-*.md*` 不在、`opsx-*.md` 残存を検査                                                                        | `.claude/` 未作成、`CLAUDE.md`/`GEMINI.md` 差分ゼロ、`.agents/workflows/` に `opsx-*.md` のみが残存すること                                                                            |
| `E2E-03` | **Recovery Path（一時環境）** | 実ワークツリーを汚さない**一時ディレクトリ（`$env:TEMP` 配下の複製）**上で、① マーカー片側破損 `AGENTS.md` への `install.ps1` 実行、② `AGENTS.md` 24KB 超過状態、③ YAML 構文破損 `config.yaml` を再現 | ① マーカー破損で二重追記せず非ゼロ停止、② 超過警告・検出が可能、③ 修正・復元後に `verify-gate.ps1` / `openspec validate` が回復通過すること                                            |
