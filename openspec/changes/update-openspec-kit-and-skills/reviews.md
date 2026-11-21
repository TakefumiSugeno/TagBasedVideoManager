# Reviews: update-openspec-kit-and-skills

---

# レビュー記録: update-openspec-kit-and-skills / propose / user (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズレビュー Cycle 1）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: `openspec/changes/update-openspec-kit-and-skills/proposal.md`, `openspec/changes/update-openspec-kit-and-skills/specs/review-framework/spec.md`, `openspec/changes/update-openspec-kit-and-skills/design.md`, `openspec/changes/update-openspec-kit-and-skills/test_plan.md`, `openspec/changes/update-openspec-kit-and-skills/tasks.md`

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー — 異常系・境界条件の先出し突合

- **確認内容・回答**:
  `specs/` を読む前に `proposal.md` のみから異常系・境界条件を 5 点先出し予測し、`specs/review-framework/spec.md` と突合した。
  1. 対象エージェント数（4 vs 5）と `.gemini/`・`CLAUDE.md`・`GEMINI.md` 境界不整合 → `proposal.md` と `spec.md` の表現ズレおよび `.gemini/`・`CLAUDE.md` 境界の未定義を検出。
  2. `AGENTS.md` 24,000B 境界と管理区間内固有記述消失 → 現行 `AGENTS.md` 管理区間内（157〜166 行目）に固有の `SemVer 2.0.0` × 規模レベル連動表が存在し、置換で消失する致命的漏れを検出。また 24KB 超過・マーカー破損異常系および `## 利用可能なSkills` 更新要否が未定義。
  3. `install.ps1` と `openspec update` の冪等性・`config.yaml` 手動マージ誤操作と復旧手順の欠如を検出。
  4. `run_{propose,archive}_review.py` の `args` 不正時や並列一部失敗時の境界不足を検出。
  5. 外部依存（`AIUtils` ローカルパス・OpenSpec CLI `1.15.0` 等）不在時の停止条件未定義を検出。
- **確認根拠**: `proposal.md`, `specs/review-framework/spec.md`, `AGENTS.md` (103-106, 118-166 行目), `CLAUDE.md`, `GEMINI.md`

### 設問2: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー — 都合の良い前提（暗黙の仮定）の告発

- **確認内容・回答**: ① 管理区間外だけ守れば固有ルールは消えない誤前提、② メタデータだけで権限が自動強制される前提、③ 24KB 上限を継続監視する強制機構不在、④ 完了形表現（`退避された旧〜`）と `E2E-03` ワークツリー汚染リスクを指摘。
- **確認根拠**: `AGENTS.md` (118-166 行目), `specs/review-framework/spec.md`, `proposal.md`, `test_plan.md`

### 設問3: UIモック（`mockup.html`）の初心者ペルソナ検証（Zero-State / Error-State）

- **確認内容・回答**: 対象外（理由: `src/wwwroot/` および `TagBasedVideoManager.Renamer` の UI/UX 変更を伴わない開発基盤更新 `chore` のため）
- **確認根拠**: `proposal.md`, `design.md`

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Propose フェーズレビューのため）

## 3. 指摘事項

### 指摘1

- **内容**: `AGENTS.md` 管理区間内（157〜166 行目）の固有「セマンティックバージョニング（SemVer 2.0.0）規則と運用」の先行移設・保全が仕様から漏れている。
- **重要度**: Critical
- **対応方針**: 採用
- **理由**: 置換により L3/L2/L1 規模レベル連動表がサイレント消失するため。
- **対応コミット**: Cycle 2 で全計画成果物へ反映解消

### 指摘2

- **内容**: 4 vs 5 エージェント記述矛盾、および `.gemini/`・`CLAUDE.md`・`GEMINI.md` 境界の未定義。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 配布対象 4 ディレクトリと非対象 `.gemini/` / 未存在 `.claude/` の境界を明確にするため。
- **対応コミット**: Cycle 2 で反映解消

### 指摘3

- **内容**: `AGENTS.md` 24,000B 超過・マーカー破損異常系・Prettier 整形条件および `## 利用可能なSkills` 更新の未定義。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 24KB 上限維持と異常系ハンドリングを実効ある要件にするため。
- **対応コミット**: Cycle 2 で反映解消

### 指摘4

- **内容**: 外部前提確認、再実行冪等性、`config.yaml` 誤操作復旧手順の欠如。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 環境依存や手動マージ時の障害復旧を定義するため。
- **対応コミット**: Cycle 2 で反映解消

### 指摘5

- **内容**: `proposal.md` 24 行目の時制表現、`E2E-03` 非汚染化、`run_*_review.py` エラー境界補足。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 作業者の誤認と作業ツリー汚染を防ぐため。
- **対応コミット**: Cycle 2 で反映解消

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし
- **指摘サマリ**: Critical: 1 件 / Major: 3 件 / Minor: 1 件 / Suggestion: 0 件
- **承認者**: user-reviewer
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: update-openspec-kit-and-skills / propose / se (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズレビュー Cycle 1）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: `openspec/changes/update-openspec-kit-and-skills/{proposal.md,specs/review-framework/spec.md,design.md,test_plan.md,tasks.md,.openspec.yaml}`

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1（設計書 §2.1）: 外部依存ハング・切断時の連鎖崩壊検証

- **確認内容・回答**: ① `install.ps1` 非原子性に伴う Split-Brain と広域 `git clean` による未追跡ファイル消失危険、② `package.json`/`.prettierrc` 不在環境での `npx prettier --write "openspec/**/*.md"` 広域書換リスク、③ `.openspec.yaml` 規模レベル未記載と `verify-gate` の `x-level:` 非読取を指摘。
- **確認根拠**: `AIUtils/openspec-kit/install.ps1`, `design.md`, `.openspec.yaml`, `verify-gate.ps1`

### 設問2（設計書 §2.2）: 可観測性（調査可能性）の検証

- **確認内容・回答**: `.openspec-target` 所在（ルート不在・`.agents/skills/.openspec-target` 実在）の正確な把握、`run_*_review.py` ロール別診断ログ・案内パスの固定、`AGENTS.md` 管理区間内外・Prettier 前後の計測内訳不足を指摘。
- **確認根拠**: `design.md`, `run_propose_review.py`, `run_archive_review.py`, `install.ps1`

### 設問3（テスト計画 §3.1）: 100%パスしても抜けるバグ（最大の死角トップ2）

- **確認内容・回答**: 死角トップ 1（非 frontmatter ファイル `.gemini/commands/opsx/*.toml` の更新判定不能、Prettier 後の `AGENTS.md` 再計測抜け、`openspec instructions` パース確認抜け）、死角トップ 2（`.github`/`.opencode`/`.hermes` 展開物 4 箇所の SHA256・プレースホルダ・8 個の `.py` 検証抜けと `verify-gate.sh` 検証抜け）を指摘。
- **確認根拠**: `test_plan.md`, `.gemini/commands/opsx/propose.toml`, `verify-gate.ps1`/`.sh`

### 設問4（テスト計画 §3.2）: モックの嘘（偽の安心感）の検証

- **確認内容・回答**: `UT-04` スタブ検証の限界明記不足、および `AIUtils` 側 Pester テストパス表記誤り（`openspec-kit/tests/` ではなく `tests/openspec-kit.*.Tests.ps1`）と外部依存丸投げを指摘。
- **確認根拠**: `test_plan.md`, `AIUtils/tests/`

### 設問5・6（タスクリスト §5.1・§5.2）: 手戻りリスクと完了基準の検証可能性

- **確認内容・回答**: タスク 2.2（`openspec update` 非対話確認/`EPERM`）・1.1（前提コミット境界）・3.1（Prettier 順序混在）、および `opsx-*` 判定基準・L2 非コード変更の Gate 2 適用方針を指摘。
- **確認根拠**: `tasks.md`, `openspec/config.yaml`

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Propose フェーズレビューのため）

## 3. 指摘事項

### 指摘1

- **内容**: `install.ps1` 非原子中断時の安全な限定パス復旧手順の欠如（広域 `git clean` 禁止の明確化）。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 未追跡データ `data/` 等や計画成果物の誤削除を防ぐため。
- **対応コミット**: Cycle 2 で `design.md`・`tasks.md` へ反映解消

### 指摘2

- **内容**: `.openspec.yaml` への `x-level: L2` 設定と `verify-gate` レベル解決・`verify-gate.sh` 検証の明確化。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: Gate 判定の信頼性担保のため。
- **対応コミット**: Cycle 2 で `.openspec.yaml`・`design.md`・`test_plan.md` へ反映解消

### 指摘3

- **内容**: `test_plan.md` の検証空白（4 エージェント SHA256/プレースホルダ/全 8 `.py`、`openspec update --dry-run` `changes: []` 判定、`openspec instructions` 確認、スタブ限界明記、一時環境 `E2E-03`）。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 外部テスト丸投げを排し実物検証を完結させるため。
- **対応コミット**: Cycle 2 で `test_plan.md` へ反映解消

### 指摘4

- **内容**: `tasks.md` の実行順序固定・Prettier 対象限定・L2 非コード `chore` 分業と Gate 2 運用方針の明文化。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 過去アーカイブ巻き込み書換と検証後の手戻りを防ぐため。
- **対応コミット**: Cycle 2 で `design.md`・`tasks.md` へ反映解消

### 指摘5

- **内容**: 可観測性（`run_*_review.py` 詳細ログ、`AGENTS.md` バイト数内訳計測）。
- **重要度**: Minor
- **対応方針**: 採用・保留併用（計測はタスク 3.2 で採用、上流 `AIUtils` 改修は `handover.md` へ保留申し送り）
- **理由**: 上流リポジトリ管理資産の独自改変を避けつつ本変更内の観測性を担保するため。
- **対応コミット**: Cycle 2 で反映・申し送り整理

### 指摘6

- **内容**: `.agents/skills/.openspec-target` および `CLAUDE.md` 実態の正確化。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 前提の誤認を防ぐため。
- **対応コミット**: Cycle 2 で `design.md` へ反映解消

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし
- **指摘サマリ**: Critical: 0 件 / Major: 4 件 / Minor: 2 件 / Suggestion: 0 件
- **承認者**: se-reviewer
- **保留指摘（申し送り）**: 指摘 5 の上流改善事項を `handover.md` へ転記
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: update-openspec-kit-and-skills / propose / user (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 2 再レビュー）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: `openspec/changes/update-openspec-kit-and-skills/{proposal.md,specs/review-framework/spec.md,design.md,test_plan.md,tasks.md,.openspec.yaml}`

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー — Cycle 1 指摘解消確認

- **確認内容・回答**:
  Cycle 1 の Critical 1 件・Major 3 件・Minor 1 件が全成果物に反映されたことを再確認した。
  1. `AGENTS.md` 管理区間内（157〜166 行目）の固有 SemVer × 規模レベル連動表の管理区間外移設と `## 利用可能なSkills` 追記を `proposal.md`, `specs/`, `design.md`, `test_plan.md` (`UT-06`), `tasks.md` (1.1) で一貫定義。管理区間内に他の固有ルール混在がないことも再確認済み。
  2. 5 エージェント互換規格と本変更での既存 4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）配布統一、`.gemini/` 非展開・共存、`.claude/` 非作成、`CLAUDE.md`/`GEMINI.md` 無改変を確定。
  3. `AGENTS.md` 24,000B 境界について Prettier 整形後計測・超過時分割回復・マーカー破損時安全停止を `specs/`・`test_plan.md` に定義。
  4. 外部前提確認・冪等性・`openspec instructions apply` パース検証・`git clean -fd` 禁止の限定パス復旧を定義。
  5. 時制修正・`$env:TEMP` 一時複製上の `E2E-03`・`run_*_review.py` エラー境界を反映。
- **確認根拠**: `proposal.md`, `specs/review-framework/spec.md`, `design.md`, `test_plan.md`, `tasks.md`, `.openspec.yaml`, `AGENTS.md`

### 設問2: 都合の良い前提（暗黙の仮定）の再確認

- **確認内容・回答**: Cycle 1 の 4 つの前提リスクはすべて解消。`design.md` L83 と `tasks.md` L3 注記の旧採番（`1.1〜3.1` / `3.2`）のみ Propose 完了前に `1.1〜3.2` / `3.3` へ整合させた。
- **確認根拠**: `design.md` L83, `tasks.md` L3, L18-20

### 設問3: UIモック（`mockup.html`）の初心者ペルソナ検証

- **確認内容・回答**: 対象外（理由: UI/UX 変更を伴わない開発運用基盤更新 `chore` のため）
- **確認根拠**: `proposal.md`, `design.md`

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Propose フェーズレビューのため）

## 3. 指摘事項

### 指摘1

- **内容**: `design.md` L83 および `tasks.md` L3 注記のタスク番号参照ズレ（`1.1〜3.1` / `3.2` → `1.1〜3.2` / `3.3`）。
- **重要度**: Minor
- **対応方針**: 採用（Propose 完了コミット前に反映済み）
- **理由**: タスク本文の番号と一致させ誤読を防ぐため。
- **対応コミット**: 本 Propose 成果物修正にて反映済み

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **指摘サマリ**: Critical: 0 件 / Major: 0 件 / Minor: 1 件（反映済み） / Suggestion: 0 件
- **承認者**: user-reviewer
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次フェーズへ

---

# レビュー記録: update-openspec-kit-and-skills / propose / se (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 2 再レビュー）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: `openspec/changes/update-openspec-kit-and-skills/{proposal.md,specs/review-framework/spec.md,design.md,test_plan.md,tasks.md,.openspec.yaml}`

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1（設計書 §2.1）: 外部依存ハング・切断時の連鎖崩壊検証

- **確認内容・回答**: Cycle 1 の連鎖崩壊リスク 3 点が解消されたことを確認。① `git clean -fd` 禁止と Gate 1 コミットを基準にした `-Force` 再実行／限定パス `git restore` 復旧、② SemVer 連動表の先行移設と対象限定 Prettier（3.1 → 3.2 順序固定）、③ `.openspec.yaml` の `x-level: L2` と `proposal.md` フォールバック解決が適切に設計されている。
- **確認根拠**: `design.md` L11, L41, L59-78, L87, `.openspec.yaml`, `proposal.md` L5, `tasks.md` L7-9, L18-19

### 設問2（設計書 §2.2）: 可観測性（調査可能性）の検証

- **確認内容・回答**: `.agents/skills/.openspec-target`・`CLAUDE.md`/`GEMINI.md` 実態の正確化、`openspec update --dry-run --json` 事前 53 件→事後 `changes: []` 比較、`AGENTS.md` 管理区間内外と Prettier 前後バイト数内訳計測により可観測性が担保された。上流改善項目は Non-Goals として `handover.md` へ分離されている。
- **確認根拠**: `design.md` L8, L16-17, L27-28, L34, L78, L88-89, `.agents/skills/.openspec-target`

### 設問3・4（テスト計画 §3.1・§3.2）: 死角トップ 2 およびモックの嘘解消確認

- **確認内容・回答**: `IT-02`（`changes: []` 検証）、`UT-05`/`UT-06`/`E2E-01`（整形後計測）、`IT-03`（`openspec instructions apply`・`verify-gate.ps1` / `.sh`）、4 エージェント SHA256 一致・8 個の `.py` 直接実行 `exit 2`、`AIUtils/tests/openspec-kit.*.Tests.ps1` 訂正と直接検証化、`UT-04` スタブ検証限界の明記、`E2E-03` 一時ディレクトリ化により死角と偽の安心感が解消された。
- **確認根拠**: `test_plan.md` L11-16, L41-45, L54, L62-68

### 設問5・6（タスクリスト §5.1・§5.2）: 手戻りリスク・完了基準確認

- **確認内容・回答**: 実行順と非コード `chore` の Gate 2 運用方針が明確化されたことを確認。なお指摘 1 の旧採番ズレ（`1.1〜3.1`/`3.2` → `1.1〜3.2`/`3.3`）は Propose 完了前に修正反映済み。
- **確認根拠**: `tasks.md` L3, L7-20, `design.md` L82-83

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Propose フェーズレビューのため）

## 3. 指摘事項

### 指摘1

- **内容**: `design.md` L83 および `tasks.md` L3 注記のタスク番号参照ズレ（`1.1〜3.1` / `3.2` → `1.1〜3.2` / `3.3`）。
- **重要度**: Minor
- **対応方針**: 採用（Propose 完了前に修正反映済み）
- **理由**: タスク本文の番号と統一するため。
- **対応コミット**: 本 Propose 成果物修正にて反映済み

### 指摘2

- **内容**: `test_plan.md` `IT-03` の `verify-gate.sh` について、Windows 環境上での実行可否判定（Git Bash 等が利用可能な場合は実行し、不可時は理由を記録する条件）の明記。
- **重要度**: Minor
- **対応方針**: 保留（Apply 実施時の記録および Archive 時 `handover.md` へ申し送り）
- **理由**: 環境依存の補助確認であり、実行可否の証跡記録で十分担保できるため。
- **対応コミット**: 保留（`handover.md` へ転記予定）

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（指摘 1 は反映済み、指摘 2 および上流 `AIUtils` 改善候補は `handover.md` 申し送り）
- **指摘サマリ**: Critical: 0 件 / Major: 0 件 / Minor: 2 件 / Suggestion: 0 件
- **承認者**: se-reviewer
- **保留指摘（申し送り）**: 指摘 2（`verify-gate.sh` 実行環境記録）、上流 `AIUtils` 向け改善候補（`verify-gate` の `x-level:` 直接読取・`AGENTS.md` 24KB 自動チェック・`run_*_review.py` のロール別進捗ログ・案内パス動的化）を `handover.md` へ転記
- **次のアクション**: 次フェーズへ
