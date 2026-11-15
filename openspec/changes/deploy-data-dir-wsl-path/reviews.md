# レビュー記録一覧: deploy-data-dir-wsl-path

---

# レビュー記録: deploy-data-dir-wsl-path / propose / pro-user-review (Cycle 1)

- **日時**: 2026-11-16
- **フェーズ**: propose
- **タスク**: Propose フェーズ独立批判的レビュー Cycle 1（User Reviewer）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**:
  - openspec/changes/deploy-data-dir-wsl-path/proposal.md
  - openspec/changes/deploy-data-dir-wsl-path/specs/container-deployment-scripts/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/specs/docker-infrastructure/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/design.md
  - openspec/changes/deploy-data-dir-wsl-path/test_plan.md
  - openspec/changes/deploy-data-dir-wsl-path/tasks.md

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1（セクション1-1）: 仕様書のブラインド比較レビュー（異常系・境界条件の先出し突合）

- **確認内容・回答**:
  `specs/` を開く前に `proposal.md` の Why / What のみを読んで、初心者が遭遇する異常系・誤操作・境界条件を5点予測（空文字/空白/スペース入りパス、WSL未導入・`docker-desktop` 等のシステムディストロ誤認識、警告後空起動によるデータ喪失パニック、Windows 版 `docker` と `deploy.sh` パス設定混在、通常ファイル誤指定・ドライブ直下・回復導線不足）し、各成果物と突合した。結果、空文字 `DATA_DIR` の扱い、Windows `docker` 時パス、`deploy.sh` Windows パス対応における 3 重矛盾、`test_plan.md` の `/videos/...` およびコンテナ名誤記、通常ファイル・ドライブ直下・スペース入りパス検証漏れ、`wslc inspect` スキーマ根拠不足が検出された。
- **確認根拠**: `proposal.md`, `specs/container-deployment-scripts/spec.md`, `design.md`, `test_plan.md`, `tasks.md`

### 設問2（セクション1-2）: 都合の良い前提（暗黙の仮定）の告発

- **確認内容・回答**: `wslc inspect` JSON スキーマ互換の暗黙仮定、`wsl.exe -l -q` 先頭行のユーザーディストロ前提、既存パス＝ディレクトリ前提、`test_plan.md` 誤記の脳内補完前提を指摘した。
- **確認根拠**: `design.md`, `test_plan.md`, `scripts/common.ps1`, `scripts/deploy.ps1`, `scripts/deploy.sh`

### 設問3〜7（セクション3〜5）

- **確認内容・回答**: テスト計画の2大死角・モック乖離リスク、タスク 1.1〜1.3 / 2.1 / 2.3 の手戻りリスク、タスク 2.2 / 2.3 / 3.1 の完了基準明確化を指摘した。セクション4（UIモック）は「対象外（UI/UX変更なし）」。
- **確認根拠**: `test_plan.md`, `tasks.md`

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Propose フェーズのため実装前）

## 3. 指摘事項

### 指摘1

- **内容**: `specs/container-deployment-scripts/spec.md`、`design.md`、`test_plan.md` 間の3重矛盾（空文字 `DATA_DIR` のフォールバック vs 拒否、Windows 版 `docker` ランタイム時のパス形式、`deploy.sh` への Windows ドライブパス入力 `S4`）。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で全成果物を統一修正済み）
- **理由**: 仕様・設計・テスト計画の矛盾を解消しないまま Apply へ進むと実装とテストが衝突するため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘2

- **内容**: `test_plan.md` におけるコンテナマウント先パス誤記（`/videos/...` → 正しくは `/app/videos/...`）およびコンテナ名誤記（`tagbasedvideomanager` → 正しくは `tag-based-video-manager`）。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で正本表記へ修正済み）
- **理由**: 先行テスト実装時の誤判定を防ぐため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘3

- **内容**: `DATA_DIR` / `VIDEO_DIRS` の通常ファイル誤指定（`metadata.db` 等）、Windows ドライブ直下/ドライブ相対（`D:\`, `D:`, `D:data`）、半角スペース入りパスの検証漏れ。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `spec.md`, `design.md`, `test_plan.md` へ追加済み）
- **理由**: ビルド・コンテナ停止前に Fail-Fast させ既存環境破壊を防ぐため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘4

- **内容**: 実機 `wslc inspect` JSON スキーマの実測根拠欠如および `wsl.exe -l -q` の `docker-desktop` 除外・WSL 未導入時回復導線の未定義。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で実機 `wslc inspect dd1e1e9654c2` および `wsl.exe -l -q` 実測結果を `design.md` に記録し仕様反映済み）
- **理由**: モックの嘘とサイレントスキップ障害を防ぐため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘5

- **内容**: 既存コンテナ `/app/data` マウント元不一致警告の具体性（旧データ非削除明記・設定復帰例・コピー例）および不正入力時エラーの代替設定例提示。
- **重要度**: Minor
- **対応方針**: 採用（Cycle 2 で警告必須 4 要素と設定例案内を `spec.md` / `design.md` / `tasks.md` へ明記済み）
- **理由**: 初心者ユーザーのデータ喪失パニック防止と自己回復性向上のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（Cycle 2 実施へ）

---

# レビュー記録: deploy-data-dir-wsl-path / propose / propose-se-review (Cycle 1)

- **日時**: 2026-11-16 04:10 +09:00
- **フェーズ**: propose
- **タスク**: 該当なし（Propose フェーズ Cycle 1 SRE/システムアーキテクトレビュー）
- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**:
  - openspec/changes/deploy-data-dir-wsl-path/proposal.md
  - openspec/changes/deploy-data-dir-wsl-path/specs/container-deployment-scripts/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/specs/docker-infrastructure/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/design.md
  - openspec/changes/deploy-data-dir-wsl-path/test_plan.md
  - openspec/changes/deploy-data-dir-wsl-path/tasks.md

## 1. 挑戦的設問への回答（故障シミュレーション・可観測性・テスト・タスク検証）

- **確認内容・回答**: `wslc inspect` / `wsl.exe` 外部依存の5秒ハング・出力文字化けシミュレーション、`$ErrorActionPreference = 'Stop'` 下での安全性、テスト計画の3重矛盾・参照ID漏れ・30秒ヘルスチェック干渉、公開 PowerShell 関数 I/F 契約（`HostPath` vs `HostAccessPath`）、`deploy.sh` における `$WSL_DISTRO_NAME` 不在時の不能判定および `realpath -m` 非互換、可観測性ログ、Task 3.1 完了基準を検証した。
- **確認根拠**: `design.md`, `test_plan.md`, `tasks.md`, `scripts/common.ps1`, `scripts/deploy.ps1`, `scripts/deploy.sh`

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Propose フェーズのため実装前）

## 3. 指摘事項

### 指摘1: `wslc inspect` 実機 JSON スキーマ未定義と `deploy.ps1` 停止リスク

- **内容**: `wslc inspect` スキーマ根拠、`$ErrorActionPreference = 'Stop'` 耐性、5000ms タイムアウト、非空不正 JSON 時診断ログの欠如。
- **重要度**: Critical
- **対応方針**: 採用（Cycle 2 で実機 JSON スキーマ記録およびプロセスタイムアウト・診断 `Write-Warning` 設計を追加済み）
- **理由**: サイレント障害とスクリプトハング・異常終了を防ぐため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘2: コンテナマウントパス（`/app/videos/` vs `/videos/`）・空 `DATA_DIR`・`deploy.sh` Windows パスの 3 重矛盾

- **内容**: `spec.md` / `design.md` / `test_plan.md` 間の3重矛盾。
- **重要度**: Critical
- **対応方針**: 採用（Cycle 2 で全成果物間で完全整合済み）
- **理由**: TDD における誤テスト・仕様違反実装を防ぐため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘3: `wsl.exe -l -q` の UTF-16LE BOM 破損・未導入 stdout 誤認・タイムアウト未設定

- **内容**: `\uFFFD` 破損対策、`WSL_UTF8=1`、5000ms タイムアウト、終了コード非0検証、`docker-desktop` 除外フィルタの不足。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `design.md` / `test_plan.md` `UT-WSL-01`〜`03` へ反映済み）
- **理由**: 実環境文字化け・システムディストロ誤選択・ハング防止のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘4: `Test-Path` のファイル誤認・WSL 停止時遅延・`Resolve-VideoMounts` と `Resolve-DataDir` の不整合

- **内容**: `-PathType Container` / `-PathType Leaf` による通常ファイル検出、`Resolve-VideoMounts` の相対パス解決基準・セグメント `..` 判定・`-TargetIsWindows` 契約明記。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `design.md` / `spec.md` / `test_plan.md` に反映済み）
- **理由**: 通常ファイル誤通過によるデプロイ中断および既存テストとの契約整合のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘5: `deploy.sh` の `$WSL_DISTRO_NAME` 不在ケースの不能判定および `realpath -m` 非互換・`VIDEO_DIRS` 検証範囲

- **内容**: `$WSL_DISTRO_NAME` 空時の不能判定除去、POSIX/Bash 互換正規化、パスセグメント単位 `..` 判定（`v1..v2` 許可）、`VIDEO_DIRS` 検証明確化。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `design.md` Decision 4 / `spec.md` / `test_plan.md` に反映済み）
- **理由**: 実装不能設計の排除と最小環境互換性確保のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘6: `test_plan.md` の未定義テスト ID 群・デシジョンテーブル漏れ・30 秒ヘルスチェック干渉問題

- **内容**: 未定義テスト ID 群および Table 4.1 `D7`/`D10` の補完、結合テストにおけるエフェメラルポート一時 HTTP リスナー＋`-Port` 指定隔離方式の導入。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `test_plan.md` 全体補完・セクション 5.1 方針追記済み）
- **理由**: テスト網羅性確保およびテスト高速化・実環境ポート非干渉化のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘7: 新設 PowerShell 関数の I/F 契約未固定（`HostPath` vs `HostAccessPath` 等）

- **内容**: `design.md`（`HostAccessPath`）と `test_plan.md`/`tasks.md`（`HostPath`）の名称統一と全公開関数 I/F 契約表の作成。
- **重要度**: Major
- **対応方針**: 採用（Cycle 2 で `design.md` に公開関数・I/F 契約表を追加し `HostPath` へ統一済み）
- **理由**: `qa-tester` と `pg-implementer` 間の契約不整合防止のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘8: 可観測性（解決前後パス・設定元ログ出力と `inspect` パース失敗時の診断ログ）

- **内容**: 起動前サマリへの詳細ログ出力と、不一致警告チェックのビルド前実行順序明確化、不正 JSON 診断ログ出力。
- **重要度**: Minor
- **対応方針**: 採用（Cycle 2 で `design.md` Decision 5 / `tasks.md` へ反映済み）
- **理由**: 障害解析の迅速化（MTTR 短縮）のため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

### 指摘9: `tasks.md` 3.1 の実機 `wslc` 非破壊確認の完了基準明確化

- **内容**: 実機確認の具体的 2 検証項目・非破壊制約・`reviews.md` 記録先の明記。
- **重要度**: Minor
- **対応方針**: 採用（Cycle 2 で `tasks.md` 3.1 / `test_plan.md` 5.3 に明記済み）
- **理由**: 完了判定を一意にするため。
- **対応コミット**: Cycle 2 Propose 成果物改訂にて解消

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（Cycle 2 実施へ）

---

# レビュー記録: deploy-data-dir-wsl-path / propose / pro-user-review-cycle2

- **日時**: 2026-11-16
- **フェーズ**: propose
- **タスク**: Propose フェーズ独立批判的レビュー Cycle 2（User Reviewer）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**:
  - openspec/changes/deploy-data-dir-wsl-path/specs/container-deployment-scripts/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/design.md
  - openspec/changes/deploy-data-dir-wsl-path/test_plan.md
  - openspec/changes/deploy-data-dir-wsl-path/tasks.md

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1（セクション1-1）: 仕様書のブラインド比較レビュー（Cycle 1 指摘との再突合）

- **確認内容・回答**: Cycle 1 で指摘した 3 重矛盾（空文字 `DATA_DIR`、`ConvertTo-ContainerMountPath` / `Resolve-VideoMounts` `-TargetIsWindows`、`deploy.sh` Windows ドライブパス `S4`）、`test_plan.md` の誤記（`/app/videos/...`・`tag-based-video-manager`）、初心者誤操作境界（通常ファイル誤指定・ドライブ直下/相対・空白入りパス・`v1..v2` 非誤拒否）のすべてが `spec.md`・`design.md`・`test_plan.md`・`tasks.md` 間で一貫して解消されたことを確認した。
- **確認根拠**: `spec.md` L8-9, L46-49, L77-111、`design.md` L60-137、`test_plan.md` L73-92, L117-160, L182, L209

### 設問2（セクション1-2）: 都合の良い前提（暗黙の仮定）の告発（Cycle 2 再確認）

- **確認内容・回答**: 実機 `wslc inspect dd1e1e9654c2` の配列 JSON 実測値および `wsl.exe -l -q` の実測値が `design.md` に記録され、5000ms タイムアウト、`docker-desktop` / `docker-desktop-data` 除外フィルタ、非空不正 JSON 診断警告が設計化されたため、楽観的前提が解消された。
- **確認根拠**: `design.md` L8-37, L83-87, L148-151, L165-167、`test_plan.md` L64-66, L102

### 設問3〜7（セクション3〜5）

- **確認内容・回答**: テスト計画の死角解消・全 24 シナリオトレーサビリティ成立、非破壊実機 `wslc` E2E 手順と記録先の明確化、警告 4 要素の具体化、タスク 1〜3 の手戻り解消と完了基準の検証可能性を確認した。セクション4は対象外（UI/UX変更なし）。
- **確認根拠**: `test_plan.md` L14, L21-46, L73-213、`tasks.md` L5-50

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Propose フェーズのため実装前）

## 3. 指摘事項

なし（理由: Cycle 1 の全指摘が仕様・設計・テスト計画・タスクへ矛盾なく反映されたことを確認済み）

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次フェーズへ（SE Reviewer の判定と合わせて Gate 1 検証へ）

---

# レビュー記録: deploy-data-dir-wsl-path / propose / propose-se-review-cycle2

- **日時**: 2026-11-16 04:15 +09:00
- **フェーズ**: propose
- **タスク**: 該当なし（Propose フェーズ Cycle 2 再レビュー）
- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**:
  - openspec/changes/deploy-data-dir-wsl-path/specs/container-deployment-scripts/spec.md
  - openspec/changes/deploy-data-dir-wsl-path/design.md
  - openspec/changes/deploy-data-dir-wsl-path/test_plan.md
  - openspec/changes/deploy-data-dir-wsl-path/tasks.md

## 1. 挑戦的設問への回答（Cycle 1 指摘解消確認）

### セクション2-設問1: 外部依存ハング・切断時の連鎖崩壊検証（`design.md`）

- **確認内容・回答**: 実機 `wslc 3.0.5.0` の配列形式 JSON 固定・`@($parsed)[0].Mounts` 抽出契約、`5000ms` タイムアウト、`$ErrorActionPreference = 'Stop'` 下でのプロセス実行、コンテナ未存在と不正 JSON 診断警告の分離、`WSL_UTF8=1`・`\0`/`\uFFFD` 除去・システムディストロ除外、`Test-Path -PathType Leaf` / `[ -f ] && [ ! -d ]` による通常ファイル拒否、`WSL_DISTRO_NAME` 非空一致判定、POSIX/Bash 互換正規化とセグメント境界 `..` 判定（`v1..v2` 許可）が適切に設計されたことを確認した。
- **確認根拠**: `design.md` L11-37, L60-72, L85-159、`spec.md` L8-9, L46-49, L77-80, L95-110

### セクション2-設問2〜5

- **確認内容・回答**: 起動前サマリの可視化ログ・警告順序、テスト計画の全 24 シナリオ網羅とエフェメラル HTTP リスナー隔離方式、公開関数 I/F 契約表による `HostPath` 統一、および Task 3.1 実機確認基準がすべて検証可能なレベルへ改善されたことを確認した。セクション4は対象外（UI/UX変更なし）。
- **確認根拠**: `design.md` L60-72, L141-159、`test_plan.md` L21-46, L64-212、`tasks.md` L9-50

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Propose フェーズのため実装前）

## 3. 指摘事項

### 指摘1: `Get-ExistingContainerDataMount` タイムアウト時診断警告と `$RepoRoot` 空文字ガード（実装時配慮）

- **内容**: (1) `Get-ExistingContainerDataMount` で `5000ms` タイムアウト発生時もコンテナ未存在（サイレントスキップ）と区別するため `Write-Warning` を出力した上で `$null` 返却すること。(2) `Resolve-VideoMounts` の相対パス解決では `$RepoRoot` が空文字 `""` で渡される可能性に備え、`??` ではなく `[string]::IsNullOrWhiteSpace($RepoRoot)` で `$PWD.Path` フォールバックすること。
- **重要度**: Suggestion
- **対応方針**: 保留（Apply 実装時に対応推奨）
- **理由**: 設計の安全性に欠陥はなく、Apply フェーズ Task 2.1 の `scripts/common.ps1` 実装時にそのまま組み込める軽微な実装配慮であるため。
- **対応コミット**: Apply フェーズ Task 2.1 実装時（または Archive `handover.md` 申し送り確認）

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘1（Suggestion: `inspect` タイムアウト時の `Write-Warning` 出力、`Resolve-VideoMounts` の `$RepoRoot` 空文字ガード）
- **次のアクション**: 次フェーズへ（Gate 1 通過後、Apply Phase 1 `qa-tester` へ進行）
