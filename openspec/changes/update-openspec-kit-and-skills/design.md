# Design: update-openspec-kit-and-skills

## Context

背景・動機および受け入れ基準は [proposal.md](./proposal.md) を参照。
参照元 `D:\programming\repos\MyGitHubRepos\AIs\AIUtils`（`main` ブランチ / `e98b6b1`）の `openspec-kit` には、Agent Skills 化（`sdd-{propose,apply,archive}`）・Gemini 4 モデルティアおよび書き込み権限メタデータ・`AGENTS.openspec.md` スリム化が含まれている。一方、本リポジトリには以下の固有構成が存在する。

1. **既存エージェントディレクトリとエントリーファイル**: `.agents/`（`.agents/skills/.openspec-target` に `antigravity` 記録あり、ルートに `.openspec-target` は不在）、`.gemini/`, `.github/`, `.hermes/`, `.opencode/` が存在し、`.claude/` は存在しない。ルートには `CLAUDE.md` と `GEMINI.md`（いずれも `@AGENTS.md`）が存在する。
2. **プロジェクト固有ルールと管理区間内混在箇所**:
   - `AGENTS.md` 先頭 1〜116 行（9,430 バイト）に F# / Avalonia / Alpine / FFmpeg / 自動フォーマット表 / SemVer 表示仕様が定義されている。
   - **重要**: 現行 `AGENTS.md` の管理区間内（157〜166 行目）に本プロジェクト固有の **`#### セマンティックバージョニング（SemVer 2.0.0）規則と運用`（`TagBasedVideoManager` と `TagBasedVideoManager.Renamer` の X/Y/Z 桁と L3/L2/L1 規模レベル連動表）** が記述されているため、`install.ps1` 実行前に管理区間外（103〜106 行目配下）へ移設する必要がある。
   - `AGENTS.md` の `## 利用可能なSkills`（41〜48 行目）に `sdd-propose` / `sdd-apply` / `sdd-archive` を追記する。
   - `openspec/config.yaml` にも固有コンテキストおよび `chore` 許可が組み込まれている。
3. **ベースブランチ・エビデンス先**: `BaseBranch` = `develop`、`EvidenceDir` = `test/TestResults/`。
4. **事前ドライラン実測値**:
   - `openspec update --dry-run --json` 実行により、`.agents/`, `.gemini/`, `.github/`, `.hermes/`, `.opencode/` 配下の計 **53 ファイル**（および `~/AppData/Roaming/openspec/config.json`）が更新対象であり、`.claude/` は作成されないことを確認済み。
   - `AGENTS.md` は移設＋追記後（約 10.2 KB）＋新 `AGENTS.openspec.md`（11,580 B）で **約 21.9 KB**（24,000 B 未満）となる見込み。

## Goals / Non-Goals

**Goals:**

- `AGENTS.md` 管理区間内の固有 SemVer × 規模レベル連動表を管理区間外へ安全に移設し、`## 利用可能なSkills` を更新する。
- `AIUtils/openspec-kit/install.ps1` を用いて `openspec/{agents,templates,scripts}`、既存 4 エージェント（`.agents/`, `.github/`, `.opencode/`, `.hermes/`）の `skills/sdd-*`、`AGENTS.md` 管理区間を最新化する（`.claude/` 非作成、`CLAUDE.md` / `GEMINI.md` 無改変）。
- `install.ps1` が一時退避した `.agents/workflows/sdd-*.md.bak*` を削除し、AI による誤参照を防ぐ（`opsx-*.md` は保持）。
- `openspec/config.yaml` の固有設定を保全したまま、`reviews.workflows` を `reviews.skills` へ更新し、`openspec instructions` で正常パースを確認する。
- `openspec update` により CLI 生成物（事前確認済み 53 ファイル）を `1.13.2` → `1.15.0` へ更新し、更新後 `openspec update --dry-run --json` の差分 0 件を確認する。
- Prettier 整形後の `AGENTS.md` 実測バイト数（管理区間外／管理区間内／合計）を計測・記録し、`< 24,000` バイトを最終保証する。

**Non-Goals:**

- `.claude/` の新規導入。
- アプリ本体（`src/`）およびアプリテスト（`test/`）の実装・設定変更。
- `verify-gate.ps1` / `verify-gate.sh`・`install.ps1`・`run_*_review.py` 等の上流 `AIUtils` 管理スクリプトへの独自パッチ適用（`x-level:` 直接読取や `run_*_review.py` のロール別詳細ログ追加等の改善提案は `handover.md` へ申し送る）。

## Decisions

### 1. 事前前提チェックと `AGENTS.md` 固有要素の先行移設

1. 作業開始前に `D:\programming\repos\MyGitHubRepos\AIs\AIUtils\openspec-kit\install.ps1` の存在、`openspec --version`（`1.15.0`）、`pwsh`・`python`・`npx` の利用可否を確認する。
2. **`install.ps1` 実行前**に、`AGENTS.md` 管理区間内の `#### セマンティックバージョニング（SemVer 2.0.0）規則と運用`（157〜166 行目）を管理区間外（103〜106 行目配下）へ移設し、`## 利用可能なSkills`（41〜48 行目）へ `sdd-propose`・`sdd-apply`・`sdd-archive` を追記して、Propose コミット（ベースライン）と作業の境界を明確化する。

### 2. `install.ps1` 実行オプションと安全なロールバック設計

```text
[Gate 1 通過済みベースラインコミット]
  |
  +--> 1. AGENTS.md 管理区間外の移設・追記
  +--> 2. AIUtils/openspec-kit/install.ps1
  |      -TargetPath . -BaseBranch develop -EvidenceDir test/TestResults/
  |      -AgentTargets auto -Force -SkipConfig (-WhatIf 確認後本実行)
  +--> 3. .agents/workflows/sdd-*.md.bak* 削除 (opsx-*.md 保持)
  +--> 4. openspec/config.yaml: reviews.workflows -> reviews.skills 更新
  +--> 5. openspec update (53ファイル 1.15.0 同期)
  +--> 6. 変更対象ファイル限定の Prettier 整形
  +--> 7. 最終計測・検証 (AGENTS.md < 24KB・4エージェント一致・CLI/Gate検証)
```

- **非原子性への対策（Split-Brain 回避・安全復旧）**:
  - `install.ps1` は複数ディレクトリへ順次書き込みを行うため、途中でファイルロック等により中断した場合は新旧ファイルが混在し得る。
  - **禁止事項**: リポジトリルートには Git 管理外のローカル実行データ `data/` や `src/TagBasedVideoManager/thumbnails/` が存在するため、**広域の `git clean -fd` は絶対に使用してはならない**。
  - **安全な復旧手順**: 途中失敗時は `git status --short` で差分を特定し、① 原因（ファイルロック等）解消後に `install.ps1 -Force -SkipConfig` を再実行する（冪等リカバリ）、または ② 巻き戻す場合は `git restore --source=HEAD -- AGENTS.md openspec/ .agents/ .gemini/ .github/ .hermes/ .opencode/` と、新規作成された限定パス（`.*/skills/sdd-*`, `.agents/workflows/sdd-*.md.bak*`, `openspec/config.kit.yaml`）の個別削除のみでクリーン状態へ戻す。

### 3. `.openspec.yaml` の規模レベル表記と `verify-gate` 運用

- `openspec` CLI `1.15.0` は `.openspec.yaml` 内の未知キー `level:` に対してスキーマ制約があるため、`x-level: L2` を `.openspec.yaml` に設定し、あわせて `proposal.md` に `- **Level**: L2` を明記する。
- `verify-gate.ps1` / `verify-gate.sh` は `level:` の次に `proposal.md` 本文の `\b(L[123])\b` フォールバックで `L2` を正しく解決する（`test_plan.md` で境界検証する）。`verify-gate` 側での `x-level:` 直接読取対応は `handover.md` にて上流 `AIUtils` へ申し送る。

### 4. Prettier 対象範囲の限定と実行順序固定

- リポジトリルートに `package.json` / `.prettierrc` がないため、`npx prettier --write` を `"openspec/**/*.md"` のような広域グロブで実行すると、`openspec/changes/archive/` の過去アーカイブまで一括整形され大量の無関係差分が生じる恐れがある。
- よって Prettier は **今回更新・生成した対象ファイルのみに限定** して実行する：
  - `AGENTS.md`
  - `openspec/agents/*.md`
  - `openspec/templates/*.md`
  - `openspec/changes/update-openspec-kit-and-skills/**/*.md`
  - `.agents/skills/sdd-*/**/*.md`（および `.github/`, `.opencode/`, `.hermes/` 配下の `skills/sdd-*/**/*.md`）
- **実行順序**: 必ず **Prettier 整形 → 最終計測・全検証** の順で実行し、整形後の状態で `AGENTS.md` の UTF-8（BOM なし）実バイト数（管理区間外／管理区間内／合計 `< 24,000`）を確定させる。

### 5. 非コード `chore` 変更における Apply 分業・Gate 2 運用方針

- 本変更は `src/` および `test/`（F# / Avalonia / Web アプリ）を変更しない開発運用基盤の `chore` 変更であるため、Apply フェーズでは `qa-tester` / `pg-implementer` によるコード実装 Red→Green サイクルは適用対象外とする。
- 作業者がタスク 1.1〜3.2 を順に実施して `test_plan.md` の検証コマンド・計測を記録した上で、タスク 3.3 において独立した批判的レビュアー（`pg-reviewer` + `qa-reviewer`）による Apply レビューおよび `verify-gate.ps1 -Gate apply`（Gate 2）を通過させ、タスクチェック更新とコミットを行う。

## Risks / Trade-offs

- **[Risk]** `npx prettier --write` がネットワーク切断等で外部キャッシュ取得に失敗する → **[Mitigation]** オフライン／キャッシュ不在で `npx prettier` が失敗する場合は、整形スキップ不可の前提エラーとして明示停止し環境確認後に再実行する（ローカルに Prettier キャッシュがあればそのまま動作）。
- **[Risk]** `openspec update` がサンドボックス内で `~/AppData/Roaming/openspec/config.json` 参照時に `EPERM` で失敗する → **[Mitigation]** 先に標準サンドボックスで実行し、`EPERM` 発生時のみ `openspec update` 単体を `BypassSandbox: true` で実行する。
- **[Risk]** `AGENTS.md` が将来の追記で 24,000 B を再超過しても `verify-gate.ps1` は自動検出し得ない → **[Mitigation]** 本変更の検証で管理区間外・内の実測内訳を記録して余裕幅（目標 約 2 KB 以上）を確認し、上限監視強化は上流 `AIUtils` への改善候補として `handover.md` に記録する。
