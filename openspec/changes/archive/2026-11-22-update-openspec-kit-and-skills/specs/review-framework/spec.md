# Delta Spec: review-framework

## MODIFIED Requirements

### Requirement: サブエージェント6ロール分業（TDD 3権分立）

システムは、テスト作成（Red）、実装（Green/Refactor）、および批判的レビューの役割を独立したサブエージェント（`qa-tester`, `pg-implementer`, `user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer`）に分離して実行させなければならない（SHALL）。さらに `openspec/agents/*.md` の各ロール定義は、YAML frontmatter として `model_tier`（`pro` | `flash`）・`thinking`（`high` | `medium`）・`write_tools`（`true` | `false`）を宣言し、レビュー・タスク記録への単一ライター原則および外部ツール環境でのフォールバック実行方針を規定しなければならない（SHALL）。

#### Scenario: テスト作成者と実装者のコンテキスト分離

- **WHEN** 新機能またはバグ修正のテストを実装する
- **THEN** `qa-tester` サブエージェントが `specs/` と `test_plan.md` のみに基づいて先行失敗テストを実装し、プロダクションコードには触れてはならない。

#### Scenario: 既存テストの無断改ざん防止

- **WHEN** `pg-implementer` サブエージェントがテストをパスさせるための実装を行う
- **THEN** `pg-implementer` は既存のテストコードの期待値変更、削除、またはスキップ化を行ってはならない。

#### Scenario: モデルティアと書き込み権限の分離契約

- **WHEN** `openspec/agents/*.md` の各ロール定義を読み込む
- **THEN** `user-reviewer` / `se-reviewer` / `pg-reviewer` は `model_tier: pro`・`thinking: high`・`write_tools: false`、`qa-reviewer` は `model_tier: pro`・`thinking: high`・`write_tools: true`、`qa-tester` は `model_tier: flash`・`thinking: high`・`write_tools: true`、`pg-implementer` は `model_tier: flash`・`thinking: medium`・`write_tools: true` として定義され、非推奨の Gemini 3.8 Flash が指定されていてはならない。

#### Scenario: 単一ライター原則および規約違反時の差し戻し

- **WHEN** サブエージェントがレビューまたは作業を完了する（または CLI 制約により運用規約メタデータが自動強制されない環境で権限外編集・誤モデル実行の逸脱が発生した）
- **THEN** サブエージェントは `reviews.md` や `tasks.md` を直接編集せず結果テキストを親エージェントへ返却し、親エージェントのみが単一ライターとしてファイルを更新しなければならない。万一権限外ファイルの改変を検知した場合、親エージェントは変更を破棄し独立コンテキストで再実行しなければならない。

## ADDED Requirements

### Requirement: 既存エージェント向け SDD スキル配布・旧ワークフロー撤去と非衝突共存

システムは、5 エージェント互換の Agent Skills 形式（`SKILL.md`＋自己完結 `references/`・`scripts/`）で定義された SDD 各フェーズ（`sdd-propose`, `sdd-apply`, `sdd-archive`）を、`-AgentTargets auto` 方針により本リポジトリに存在する 4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）へ展開し、旧形式 `.agents/workflows/sdd-*.md` を撤去しつつ、既存 `CLAUDE.md` / `GEMINI.md`・`.gemini/`・OpenSpec CLI `1.15.0` 生成物と非衝突で共存させなければならない（SHALL）。

#### Scenario: 既存 4 エージェントのみへのスキル展開と未存在・エントリー境界の維持

- **WHEN** `-AgentTargets auto` で `openspec-kit` を適用する（リポジトリには `.agents/`, `.gemini/`, `.github/`, `.opencode/`, `.hermes/`, `CLAUDE.md`, `GEMINI.md` が存在し、`.claude/` は存在しない）
- **THEN** `.agents/skills/sdd-*`、`.github/skills/sdd-*`、`.opencode/skills/sdd-*`、`.hermes/skills/sdd-*` の 4 箇所に同一内容・プレースホルダ置換済みのスキル一式が配置され、`.claude/` ディレクトリは作成されず、`.gemini/` には `sdd-*` を配置せず、既存の `CLAUDE.md` および `GEMINI.md`（`@AGENTS.md`）は無改変で保持されなければならない。

#### Scenario: 旧 SDD ワークフローの完全撤去と opsx / CLI 1.15.0 生成物の共存

- **WHEN** 旧 `.agents/workflows/sdd-{propose,apply,archive}.md` の移行および `openspec update` を完了する
- **THEN** `.agents/workflows/sdd-*.md` および一時退避ファイル（`.bak*`）はリポジトリ上に残存せず、同ディレクトリの `opsx-*.md`・`.gemini/commands/opsx/*`・各エージェントの `openspec-*` スキルが OpenSpec CLI `1.15.0` 相当へ更新され、`sdd-*` スキルと衝突なく共存し、かつ再実行しても冪等性が保たれなければならない。

#### Scenario: run_workflow 並列レビュー用スクリプトの実行ガードとエラー境界

- **WHEN** `sdd-propose/scripts/run_propose_review.py` または `sdd-archive/scripts/run_archive_review.py` を Antigravity `run_workflow` 外の通常の `python` CLI から直接実行する、または `run_workflow` 実行時に不正な `change_name` を渡す・一部ロールが失敗する
- **THEN** 通常 `python` 実行時は `NameError` でクラッシュせず代替手順の案内を標準エラー出力へ出して終了コード `2` で安全停止し、`run_workflow` での不正 `change_name` 時は `ValueError` で拒否し、一部ロール失敗時は `references/antigravity-orchestration.md` に従って未完了ロールのみ単体再実行する回復導線が明示されていなければならない（なお通常 `python` 実行時のエラー案内は `.agents/skills/...` を代表パスとして提示し、他エージェント環境では自ディレクトリの `SKILL.md` を参照する）。

#### Scenario: 外部前提不在または手動設定ミス時の検知と安全停止

- **WHEN** 参照元 `AIUtils` パスまたは必須 CLI（`openspec` `1.15.0`, `pwsh`, `python`）が不在・バージョン不一致である、または `openspec/config.yaml` の手動マージで YAML 構文不備や `-SkipConfig` 漏れによる `config.kit.yaml` / `config.yaml.bak-*` 残留が発生する
- **THEN** 変更適用前または検証時にエラーとして検知・停止し、`config.yaml` の復元・不要生成物の除去および `openspec instructions` による設定読み込み検証を経て復旧できなければならない。

### Requirement: ルールファイル（AGENTS.md）のサイズ上限遵守と固有ルール保全

システムは、ルールファイル `AGENTS.md`（UTF-8 BOM なし、Prettier 整形後）の総バイト数を Antigravity CLI 上限である `24,000` バイト未満に維持し、管理区間外および従来管理区間内に混在していたすべてのプロジェクト固有規約、ならびに `openspec/config.yaml` の固有設定を欠落なく保全しなければならない（SHALL）。

#### Scenario: 更新・整形後 AGENTS.md の 24,000 バイト未満維持と固有規約・SemVer 連動表保全

- **WHEN** 従来の管理区間内にあった「セマンティックバージョニング（SemVer 2.0.0）規則と運用（L3/L2/L1 規模レベル連動表）」を管理区間外へ移設し、`## 利用可能なSkills` へ `sdd-*` を追記した上で `openspec-kit` 管理区間を置換し、Prettier 整形後に `AGENTS.md`（UTF-8 BOM なし）を検査する
- **THEN** 総バイト数が `24,000` バイト未満であり、管理区間外の「プロジェクト概要」「構成」「利用可能なSkills」「自動フォーマット」「実装ガイドライン」「セマンティックバージョニング（L3/L2/L1 連動表含む）」「参考情報」がすべて保持されていなければならない。

#### Scenario: AGENTS.md サイズ超過およびマーカー破損時の異常系ハンドリング

- **WHEN** プロジェクト固有ルールの追記等で `AGENTS.md` が `24,000` バイト以上へ超過する、または `<!-- openspec-kit:begin` / `<!-- openspec-kit:end -->` マーカーの片側欠損・多重・順序逆転が発生した状態で同期または検証を行う
- **THEN** マーカー破損時は二重追記せずエラー終了して修復手順を案内し、`24,000` バイト以上へ超過した場合は実測検証で不合格として管理区間外のプロジェクト固有説明を別ドキュメントへ分割することで `24,000` バイト未満へ回復させなければならない。

#### Scenario: openspec/config.yaml の固有設定保持と skills 参照切替

- **WHEN** `openspec/config.yaml` の更新内容および `openspec instructions apply --change "<name>" --json` 出力を検査する
- **THEN** `reviews.skills` が `.agents/skills/sdd-{propose,apply,archive}/SKILL.md` を参照しており、F# / Avalonia / Alpine 技術スタック、SemVer 2.0.0 運用ルール、`test/TestResults/`、HTML モック要件、`chore` 許可などの固有設定が失われず、かつ CLI から正常にパース・注入されなければならない。
