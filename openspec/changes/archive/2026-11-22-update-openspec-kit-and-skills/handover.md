# 申し送り事項 (Handover): update-openspec-kit-and-skills

- **変更名**: `update-openspec-kit-and-skills`
- **日付**: 2026-11-22
- **ブランチ**: `chore/update-openspec-kit-and-skills`
- **関連 Issue / PR**: 作成予定（Archive 完了後に `develop` 向け PR を自動作成）

---

## 1. 変更サマリ

- **What & Why（背景・設計判断）**:
  - 参照元 `D:\programming\repos\MyGitHubRepos\AIs\AIUtils`（`main` ブランチ @ `e98b6b1`）の最新 `openspec-kit` および OpenSpec CLI `1.15.0` を本リポジトリへ取り込んだ。
  - **1) `AGENTS.md` 24,000 バイト上限超過の解消と固有 SemVer 連動表の保全**: 従来 `AGENTS.md` は 31,996 バイトで Antigravity CLI 上限（24,000 バイト）を超過していた上、管理区間内に固有の「セマンティックバージョニング (SemVer 2.0.0)」L3/L2/L1 連動表が混在していた。同連動表および `sdd-*` スキル一覧を管理区間外へ移設・追記した上で管理区間をスリム版へ置換し、Prettier 整形後 **22,444 バイト**（管理区間外 10,837 B + 管理区間内 11,607 B、余裕 1,556 B）へ圧縮した。
  - **2) 非推奨ワークフロー廃止 → 5 エージェント互換 Skills 展開（`-AgentTargets auto`）**: 旧 `.agents/workflows/sdd-{propose,apply,archive}.md` は Antigravity 2.0 以降非推奨で他エージェント非互換のため廃止し、既存 4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）の `skills/sdd-*` へ展開した（`.claude/` は未存在のため非生成、`CLAUDE.md`/`GEMINI.md` は無改変）。旧 `sdd-*.md` には固有カスタム編集がなかったため、一時退避 `.bak` を削除して AI による誤参照を防いだ（`opsx-*.md` は保持）。
  - **3) 6 ロール定義のモデル・権限分離と `config.yaml` 保護**: `openspec/agents/*.md` に `model_tier`（`pro`/`flash`）・`thinking`（`high`/`medium`）・`write_tools`（`true`/`false`）・単一ライター契約を追加。`openspec/config.yaml` は `-SkipConfig` で保護した上で `reviews.workflows` のみを `reviews.skills` へ更新した（`-OverwriteConfig` 不採用理由は F#/Avalonia/SemVer/HTML モック/`chore` 許可の固有設定消失防止）。
  - **4) OpenSpec CLI `1.15.0` 更新**: `openspec update` により 5 ツール計 53 ファイルを `1.13.2` → `1.15.0` へ更新した。
- **適用規模・アプリバージョン**: `L2` / `chore`（`src/`・`test/` に変更なしのため Web / Renamer の `<Version>` は据え置き）。

---

## 2. 品質・テストエビデンス

本変更はプロダクションコード（`src/`）およびアプリテスト（`test/`）を変更しないため C0/C1/C2 アプリコードカバレッジ計測は対象外とし、設定・構成・境界条件・CLI 動作を実機検証した（全一時環境は検証後削除済み、`test/TestResults/` 出力なし）。

- **単体・境界値検証（`UT-01`〜`UT-06`）**: 6 ロール frontmatter・Gemini 3.8 非採用（50 ファイル走査）・4 エージェント全 8 `.py` 直接実行 `exit 2` ＆ 20 種悪意入力 `ValueError`・`AGENTS.md` BOM なし/LF のみ/`22,444` B (`< 24,000` B)・SemVer 連動表保全 → **全 Pass**
- **結合・E2E 検証（`DT-01`/`DT-02`, `IT-01`〜`IT-04`, `E2E-01`〜`E2E-03`）**: 4 エージェント `sdd-*` 11 ファイル SHA256 完全一致・プレースホルダ残存 0 件・`openspec update --dry-run --json` 差分 0 件・`openspec instructions apply` 正常読込（`context` 3,109 文字、stderr 警告 0）・一時ディレクトリ上の 23,999/24,000 B 境界・マーカー破損 `exit 1`・`.claude` 検出・Prettier `3.10.1` `--check`・`verify-gate.ps1` / `verify-gate.sh`（Git Bash 経由 Propose ゲート） → **全 Pass**
- **アプリ差分**: `git diff --stat -- src/ test/ CLAUDE.md GEMINI.md openspec/changes/archive/` → 差分 0 件

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）                                               | 内容                                                                                                                                                                                                                                                             | 保留理由                                                                                                        | 推奨対応時期                                                  |
| ---------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Propose Cycle 2 / `se-reviewer` (指摘 2 / Minor)                             | `verify-gate.sh`（Bash）の Windows サンドボックス内実行制約（WSL/Git Bash 起動不可時は静的検査＋通常環境 Git Bash 実行で代替）                                                                                                                                   | 環境制約による補助確認のため実測記録で担保済み                                                                  | 次回 CI / Linux・DevContainer 検証時                          |
| Apply / `pg-reviewer` (指摘 1 / Minor) & `qa-reviewer` (指摘 3 / Suggestion) | ルートに `.prettierignore` がない状態で `AGENTS.md` L61/L63（`"**/*.md"`, `"openspec/**/*.md"`）および `sdd-archive/SKILL.md` L82 に広域グロブが残存し、過去アーカイブ一括書換の恐れがある                                                                       | 本変更は対象限定 Prettier で回避済み、上流資産・固有規約外（Non-Goals）のため                                   | 別タスクで `.prettierignore` 導入または対象限定ルール化を検討 |
| Apply / `pg-reviewer` (指摘 2 / Minor)                                       | `run_propose_review.py`（65 行）と `run_archive_review.py`（67 行）の重複約 60%（統合で約 43% 削減可能）、`_agent()` への `write_tools`/`thinking` 引数未伝達、`openspec/changes/{name}` 実在確認欠如、`None` フォールバック漏れ、ロール別進捗ログ・案内パス固定 | 上流 `AIUtils` リポジトリ管理資産であり本変更での独自パッチ禁止（Non-Goals）のため                              | 上流 `AIUtils` リポジトリへの改善反映時                       |
| Apply / `pg-reviewer` (指摘 3 / Minor) & `qa-reviewer` (指摘 2 / Minor)      | `verify-gate.{ps1,sh}` のフェーズ別 `LGTM` 未分離、`x-level:` 直接非読取、`AGENTS.md < 24,000` B 自動検査不在、および `verify-gate.sh` L161 GNU `sed` `\L` 依存                                                                                                  | 上流 `AIUtils` リポジトリ管理資産（Non-Goals）のため（本変更では `proposal.md` 先頭 `L2` 記載と実測検証で回避） | 上流 `AIUtils` リポジトリへの改善反映時                       |
| Apply / `pg-reviewer` (指摘 4 / Suggestion)                                  | `sdd-apply/SKILL.md` L70-71 と `checklist_apply_impl.md` L4 のレビュー担当表記の軽微な揺れ、ブランチ例示（`refactor/`, `chore/`）の揺れ                                                                                                                          | 実動作への支障なし、上流同期資産のため                                                                          | 上流 `AIUtils` リポジトリへの改善反映時                       |
| Apply / `qa-reviewer` (指摘 1 / Minor)                                       | `openspec` CLI が不正 YAML 構文時に stderr 警告付きで exit 0 を返し `context` を空にするため、exit 0 のみでは設定破損を見逃す                                                                                                                                    | OpenSpec CLI 本体仕様であり、本リポジトリ `config.yaml` は正常読込確認済みのため                                | 運用チェック手順で stderr 警告ゼロ＋`context` 非空を継続確認  |

---

## 4. 将来の改善・リファクタ候補

- **上流 `AIUtils/openspec-kit` へのフィードバック**:
  1. `run_propose_review.py` / `run_archive_review.py` の共通ランナー統合（約 43% コード削減）、`changes/{name}` 事前実在確認、ロール別開始・終了ログ、および `agent()` 引数・`.agents/` 固定案内パスの改善。
  2. `verify-gate.ps1` / `verify-gate.sh` のフェーズ別 `LGTM` 検査、`x-level:` 直接読取、`AGENTS.md` 24,000 バイト上限の自動検査、および `verify-gate.sh` の BSD `sed` 互換化。
  3. `sdd-apply/SKILL.md`・`sdd-archive/SKILL.md` の記載整合（`qa-reviewer` 併用表記・Prettier 対象限定案内）。
- **本リポジトリでの整理・改善候補**:
  - `openspec/changes/archive/` を除外する `.prettierignore` を導入すると、誤って `"openspec/**/*.md"` を実行しても過去アーカイブが書き換わらなくなる。
  - `AGENTS.md` L97-101 および `openspec/config.yaml` L104 に残存する廃止済み `.env` / `VIDEO_DIR` 記載（`README.md` 記載の `appsettings*.json` 方式との乖離）を別変更で整理すると、AI 誤実装の防止と `AGENTS.md` のバイト余裕回復を両立できる。

---

## 5. 運用上の注意事項

- **Prettier 実行時の対象限定（重要）**:
  - `sdd-archive/SKILL.md` 等には `npx prettier --write "openspec/**/*.md"` と記載されているが、本リポジトリには `.prettierignore` がないため、そのまま実行すると `openspec/changes/archive/` 内の過去アーカイブまで一括整形される。作業時は必ず更新・新規作成したファイルのみ（例: `openspec/specs/review-framework/spec.md` や対象 `openspec/changes/archive/<date>-<name>/**/*.md`）を明示指定すること。
- **`AGENTS.md` サイズ管理（現在 22,444 B / 余裕 1,556 B）**:
  - Antigravity CLI の 24,000 バイト上限まで残り約 1.5 KB のため、今後 `AGENTS.md` 管理区間外へ固有規約を大幅追記する場合は別ファイルへ分割すること。
- **4 エージェント複製スキル（`.agents/`, `.github/`, `.opencode/`, `.hermes/` 計 44 ファイル）の片側編集禁止**:
  - いずれか 1 箇所のみを手動編集すると他エージェントとの手順乖離（Split-Brain）が起きるため、更新時は必ず上流 `AIUtils/openspec-kit/install.ps1` 等で 4 箇所を一括再同期し、SHA256 一致を確認すること。
- **`git clean -fd` 禁止・ロールバック手順**:
  - ワークスペースには Git 管理外のローカルデータ `data/` および `src/TagBasedVideoManager/thumbnails/` が存在するため、`git clean -fd` は使用禁止。
  - PR マージ後に問題が判明した場合は、Merge commit に対する `git revert -m 1 <merge-commit>` で安全に直前状態へロールバック可能（DB やアプリソースへの破壊的変更は含まない）。
- **トラブル時の即座の切り分けコマンド**:
  - `openspec/config.yaml` 反映異常が疑われる場合は `openspec instructions apply --change "<name>" --json` を実行し、stderr 警告 0 件かつ JSON 内の `context` が非空であることを確認する。
  - `AGENTS.md` の上限超過が疑われる場合は `pwsh -NoProfile -Command "[System.IO.File]::ReadAllBytes('AGENTS.md').Length"` で `24,000` バイト未満であることを確認する。
- **Windows サンドボックス補足**:
  - `openspec update`（`~/AppData/Roaming/openspec/config.json` 参照）、`npx prettier`（初回キャッシュ通信）、Git コミット（`.git/` 書き込み）、および `AIUtils/openspec-kit/install.ps1` の一部ファイル上書きはサンドボックス制限を受けるため、必要最小単位で実行権限を切り分けること。
