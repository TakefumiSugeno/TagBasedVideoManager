# Tasks: 設定ファイルの一本化（.env 廃止・appsettings.{env}.json 統一）および AI File Renamer ローディングスピナー UI 導入

- **Change Name**: `appsettings-unification-and-renamer-spinners`
- **Scale Level**: `L2`
- **TDD 方針**: `[QA Tester]` による先行失敗テスト（Red）作成 → `[PG Implementer]` による最小実装（Green）＆ リファクタリング → `[PG/QA Reviewers]` による批判的レビュー（最大3サイクル）

---

## Phase 1: Test First — 先行失敗テスト (Red) `[QA Tester]`

> 完了基準: 追加・更新したすべての新規仕様テストが、現行実装に対して期待通り失敗（Red）または未定義検出されることを確認する（プロダクションコードは一切編集しない）。

- [x] **1.1 Git 除外・クリーンテンプレート検証テストの作成** (`test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`)
  - `src/TagBasedVideoManager/appsettings.json` および `src/TagBasedVideoManager.Renamer/appsettings.json` に個人固有パスや API キー等の秘密情報が含まれないことの検査テストを追加
  - `.gitignore`（`**/appsettings.Development.json`, `**/appsettings.Production.json`）および `git ls-files` に `.env.sample`・環境別 JSON が含まれないことの検証テストを追加
- [x] **1.2 Renamer `.env` 廃止・環境別保存 (`appsettings.{env}.json`) 単体テストの作成** (`test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`)
  - `.env` / OS環境変数（`VIDEO_DIR`, `OPENROUTER_API_KEY`）を完全に無視し、`appsettings.json` + `appsettings.{Environment}.json` のみ読み込むテストを作成
  - `resolveSavePathForEnv` が `Development` / `Production` に応じて `appsettings.{Environment}.json` を返し、ベース `appsettings.json` を書き換えないテストを作成
  - ReadOnly 基底ディレクトリ時の `%APPDATA%/TagBasedVideoManager/appsettings.{Environment}.json` フォールバック保存および次回読み込みでの最優先適用テストを作成
  - 不正な環境名（空文字、`../evil`）指定時の安全フォールバックテストを作成
- [x] **1.3 Renamer スピナー状態・排他制御・個別再試行の `StateTests` 追加** (`test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`)
  - `HasScanned`・`ScannedVideoCount`（「動画0本」vs「全件安全」区別）、`ActiveEnvironment` / `SettingsSavePath` 保持、`Settings.save` エラー時の `ErrorMessage` 通知テストを作成
  - `ExecuteScanAndPropose` → `IsScanning = true`（未設定空文字時は誤スキャンせずガイド保持）、`IsScanning`/`IsRenaming` 中の `TargetDirectoryChanged`/`RuleSelected`/`ModelSelected` 入力割り込み無視ガードテストを作成
  - 個別候補の AI 提案失敗時 `AiError` 設定と `RetryCandidateAiProposal` 単体再試行、キャンセル/タイムアウト時の `IsRequestingAi`・`IsAiProcessing` 完全解除テストを作成
  - `IsRenaming`（通常リネーム・Undo 実行・失敗時解除）および `IsDockerBusy`（完了・`TimeoutError` 時解除）遷移テストを作成
- [x] **1.4 Web `IConfiguration` 統合・`.env` 廃止テストの作成** (`test/TagBasedVideoManager.Tests/Unit/ProgramTests.fs`, `Integration/VersionApiTests.fs`)
  - `Program.loadDotEnv` 廃止および `.env` ファイルが存在しても Web 設定値に影響しないことのテストを作成
  - `IConfiguration`（`appsettings.json` + `appsettings.{env}.json` + 環境変数オーバーライド）で `PORT`, `VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH` が解決される結合/単体テストを作成
  - Web バージョン `0.3.0`（`/api/version`）検証テストを更新
- [x] **1.5 デプロイスクリプト（`deploy.ps1` / `deploy.sh`）`.env` 廃止・`appsettings*.json` 読込テストの作成** (`test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`)
  - `.env` 解析除去・`.env` 非依存、および `appsettings.json` + `appsettings.Production.json` からの `VIDEO_DIRS`（セミコロン含む複数パス）・`PORT`・`DATA_DIR` 安全読み込みテストを作成
  - CLI引数・シェル環境変数による `appsettings*.json` オーバーライド、不正 JSON 構文時の早期非 0 終了テストを作成

---

## Phase 2: Implementation (Green & Refactor) `[PG Implementer]`

> 完了基準: Phase 1 の Red テストをすべて Green にし、`dotnet format`・`Prettier` を通過させる。

- [x] **2.1 リポジトリ設定整理・`.env` 資産削除・`.gitignore` 更新**
  - `.gitignore` に `**/appsettings.Production.json` を追加
  - `.env.sample` を削除し、`git rm --cached` で `src/TagBasedVideoManager/appsettings.Development.json` と `src/TagBasedVideoManager.Renamer/appsettings.Production.json` を Git 管理から外す（ローカル実ファイルは保持し、環境別ファイル不在クローン環境でもビルドが通ることを別途確認）
  - `src/TagBasedVideoManager/appsettings.json`（`PORT`, `VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH`, `DATA_DIR`）と `src/TagBasedVideoManager.Renamer/appsettings.json`（秘密値・個人パス除去済みテンプレート）を整える
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` の `appsettings.Development.json` / `appsettings.Production.json` 双方に `Condition="Exists(...)"` を付与する
- [x] **2.2 Renamer 設定システム改修 (`src/TagBasedVideoManager.Renamer/Settings.fs`)**
  - `parseDotEnv` および `defaultSettings ()` 内の `VIDEO_DIR` 環境変数参照を削除（`.env` 残存時は読み込まず警告ログ／移行案内を記録）
  - `resolveEnvironmentName` にパストラバーサル/不正文字サニタイズを追加
  - `loadConfigurationWithConfig` の探索・統合順序を「ベース階層 (`ProjectDir` / `CurrentDir` / `BaseDir`) の `appsettings.json` → `appsettings.{env}.json`」＋「`%APPDATA%` の `appsettings.json` → `appsettings.{env}.json` 上書き層」へ改修（JSON構文エラー時のフォールバック警告通知、明示空 `rules` 非復元対応含む）
  - `resolveSavePath` をアクティブ環境名に応じた `appsettings.{Environment}.json`（書込可否判定＋不可時 `%APPDATA%` フォールバック＋GUID一意一時ファイル `.tmp` 経由のアトミック置換）へ改修
- [x] **2.3 Renamer State & ローディングスピナー UI 実装 (`Domain.fs`, `State.fs`, `Views.fs`, `Program.fs`, `.fsproj`)**
  - `TagBasedVideoManager.Renamer.fsproj` の `<Version>` を `0.1.0` → `0.2.0` に更新
  - `Domain.fs` / `State.fs`: `HasScanned`, `ScannedVideoCount`, `ActiveEnvironment`, `SettingsSavePath`, `AiError: string option`, `RetryCandidateAiProposal` を実装し、`IsScanning`/`IsRenaming` 中の入力変更割り込み排他ガードと `Settings.save` 失敗時エラー通知を追加
  - `Views.fs`: Avalonia 11 ネイティブ回転アニメーションに基づく単一 `SpinnerControl`（`Path` 円弧 + `Animation`/`RotateTransform.AngleProperty`）を実装し、フォルダ未設定 Zero-State、`IsScanning` 中央スピナー（動画0本/全件安全出し分け）、`IsAiProcessing` 行スピナー＋個別カード失敗赤枠＆「↻ 再試行」＋フッター進捗スピナー、`IsRenaming` 操作ブロック半透明オーバーレイ＋スピナー、`IsDockerBusy` 小型スピナー、アクティブ環境・保存先バッジを `mockup.html` に忠実準拠して実装
- [x] **2.4 Web アプリケーション設定一本化 (`TagBasedVideoManager`)**
  - `TagBasedVideoManager.fsproj` の `<Version>` を `0.2.0` → `0.3.0` に更新
  - `Program.fs`: `loadDotEnv ()` を削除し、`WebApplication.CreateBuilder` の `IConfiguration` から `PORT`, `THUMBNAIL_DIR`, `DATABASE_PATH`, `VIDEO_DIRS` を取得、`DATABASE_PATH` 親ディレクトリ自動作成、および環境・`VIDEO_DIRS` 含む構成診断ログを出力
  - `HttpHandlers.fs`: `triggerScan` が `IConfiguration` から `VIDEO_DIRS`, `THUMBNAIL_DIR` を解決＋解決パス一覧ログを出力するよう更新
- [x] **2.5 デプロイスクリプト・ドキュメントの更新 (`scripts/deploy.ps1`, `deploy.sh`, `common.ps1`, `README.md`, `PRODUCTION_ENVIRONMENT.md`)**
  - `scripts/common.ps1`, `scripts/deploy.ps1`: `.env` 読み込みを削除し、`src/TagBasedVideoManager/appsettings.json` ＋ `appsettings.{Environment}.json`（`ConvertFrom-Json`、不正JSON早期エラー）読み込みを実装
  - `scripts/deploy.sh`: `.env` 読み込みを削除し、`python3` または `jq` のみを用いた安全な JSON 解析（双方不在または構文エラー時は推測フォールバックせず即座に非 0 Fail-Fast 終了）を実装
  - `README.md` および `PRODUCTION_ENVIRONMENT.md` から `.env` 前提手順を削除し、`appsettings.{env}.json` ベースの設定・移行手順に更新

---

## Phase 3: E2E Verification, Coverage & Documentation `[QA Tester + PG Implementer]`

- [x] **3.1 実ブラウザ・実画面レンダリング E2E テストとエビデンス出力**
  - Web E2E（Playwright）テスト（`VersionE2ETests.fs` 等）を実行し、`v0.3.0` 表示・一覧描画・異常無しのスクリーンショット確認
  - Renamer 画面描画検証（起動・各スピナー状態・環境バッジの視覚検証およびスクリーンショット出力、`test/TestResults/` へ TRX + HTML カバレッジレポート出力）
- [x] **3.2 カバレッジ基準・フォーマット・批判的レビュー確認**
  - 単体・結合・E2E 全テスト Pass、新規差分 C0 90%+ / C1 85%+ 達成確認
  - `dotnet format` および `npx prettier --write` 実行
  - `[PG Reviewer]`・`[QA Reviewer]`・`[SE Reviewer]` による Apply レビュー実施（`LGTM: true`）→ `verify-gate.ps1 -Gate apply` 通過
