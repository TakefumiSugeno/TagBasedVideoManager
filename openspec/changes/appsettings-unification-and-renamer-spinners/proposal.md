# Proposal: 設定ファイルの一本化（.env 廃止・appsettings.{env}.json 統一）および AI File Renamer ローディングスピナー UI 導入

## Metadata

- **Change Name**: `appsettings-unification-and-renamer-spinners`
- **Scale Level**: `L2`（通常変更: 仕様変更・機能改善・UI/UX改善）
- **Target Applications**:
  - Web アプリケーション (`TagBasedVideoManager`): `0.2.0` → `0.3.0`（Minor インクリメント）
  - デスクトップアプリケーション (`TagBasedVideoManager.Renamer`): `0.1.0` → `0.2.0`（Minor インクリメント）
- **Related Issues**:
  - [#32](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/32)（AI File Renamer の設定管理・UI 改善）
  - [#33](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/33)（Step 2: デスクトップ側ローディング UI スピナー対応）

---

## Why

1. **`.env` と `appsettings.json` の二重管理・誤コミットリスクの解消**:
   - 現在、Web アプリ (`TagBasedVideoManager`)・デプロイスクリプト (`scripts/deploy.ps1`, `scripts/deploy.sh`) は `.env`（および `.env.sample`）に依存している一方、Desktop アプリ (`TagBasedVideoManager.Renamer`) は `appsettings.json` + `appsettings.{Environment}.json` + `.env` を混在参照しており、設定管理が二重化している。
   - さらに、`src/TagBasedVideoManager/appsettings.Development.json` と `src/TagBasedVideoManager.Renamer/appsettings.Production.json` が Git 追跡下に残っており、`.gitignore` も `**/appsettings.Production.json` を除外していないため、ローカル固有パスや API キーの誤コミットリスクが高い。
   - また、Renamer の UI 設定保存先（`Settings.resolveSavePath`）が `appsettings.json` 固定となっており、ユーザー要望通り現在のアクティブ環境（`appsettings.Development.json` / `appsettings.Production.json`）へ書き戻されない。
2. **AI File Renamer 非同期処理中のローディング視覚フィードバック不足（Step 2）**:
   - Step 1（`web-loading-spinners`）で Web UI にスピナーを導入した一方、AI File Renamer（Avalonia デスクトップアプリ）では、ディレクトリ走査（`IsScanning`）、ファイル単位の AI 提案（`IsRequestingAi` / `IsAiProcessing`）、リネーム・Undo 実行（`IsRenaming`）、コンテナ操作（`IsDockerBusy`）の各非同期処理中に回転スピナーが表示されず、無反応または未実行状態に見えてしまう UX 上の課題がある。

---

## What Changes

### 1. `.env` 完全廃止と `appsettings.json` / `appsettings.{Environment}.json` への一本化

- **`.env` / `.env.sample` の廃止**:
  - `.env.sample` をリポジトリから削除し、Web（`Program.loadDotEnv`）・Renamer（`Settings.parseDotEnv`）・デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）から `.env` 読み込みロジックを撤廃する。
  - Renamer における `VIDEO_DIR` OS 環境変数参照も廃止する（`DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` のみ環境識別用に参照）。
- **環境別設定ファイルの実態管理と Git 除外**:
  - ベーステンプレートとして `src/TagBasedVideoManager/appsettings.json` と `src/TagBasedVideoManager.Renamer/appsettings.json` のみを Git 管理対象（秘密値・個人ローカルパスを含まないクリーンな初期値）とする。
  - `.gitignore` に `**/appsettings.Development.json` および `**/appsettings.Production.json` を登録し、既に誤追跡されている `src/TagBasedVideoManager/appsettings.Development.json` と `src/TagBasedVideoManager.Renamer/appsettings.Production.json` は `git rm --cached` で追跡解除する（開発者のローカル実ファイルは削除しない）。
  - `TagBasedVideoManager.Renamer.fsproj` の `appsettings.Development.json` / `appsettings.Production.json` 双方に `Condition="Exists(...)"` を設定し、環境別ファイル未配置状態でのビルドエラーを防ぐ。
- **Web アプリ & デプロイスクリプトの `IConfiguration` / `appsettings` 統合**:
  - Web アプリ（`Program.fs`, `HttpHandlers.fs`）で `WebApplication.CreateBuilder` の `IConfiguration` を通じて `PORT`, `VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH` を取得する（コンテナ環境変数が `appsettings.{Environment}.json` / `appsettings.json` をオーバーライドする .NET 標準階層）。
  - `scripts/deploy.ps1` / `scripts/deploy.sh` は `.env` の代わりに `src/TagBasedVideoManager/appsettings.json` および環境別 JSON（既定 `Production`: `appsettings.Production.json`、`-Environment` 引数または `ASPNETCORE_ENVIRONMENT` で切替可）から `VIDEO_DIRS`, `PORT`, `DATA_DIR` 等を読み取る（優先順位: CLI引数 > 環境変数 > `appsettings.{env}.json` > `appsettings.json` > 既定値）。
- **Renamer 設定保存先の環境別化 (`appsettings.{Environment}.json`) と探索優先順位の整合**:
  - `Settings.resolveSavePath` を改修し、現在の実行環境（`Development` / `Production` 等）に応じた `appsettings.{Environment}.json` へ保存するように変更する。
  - 実行ディレクトリ（またはプロジェクトルート実行時は `src/TagBasedVideoManager.Renamer/`）に書込可能ならそこへ保存し、書込不可またはベース不在時は `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` へ安全にフォールバックする。
  - これに伴い、`Settings.loadConfigurationWithConfig` の読み込み優先順位に `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` を最上位オーバーライド層として組み込み、フォールバック保存した設定が次回起動時に必ず再読込されるように整合させる。
  - Renamer 画面ヘッダー／フッター付近にアクティブ環境バッジ（例: `ENV: Development` / `保存先: appsettings.Development.json`）を表示する。

### 2. AI File Renamer ローディングスピナー UI 導入（Fluent Dark 準拠）

- **共通回転スピナーコンポーネントの導入 (`Views.fs` / スピナー描画)**:
  - Avalonia 上でタイマー（`DispatcherTimer` 等）により滑らかに回転するリング/アークスピナーを用意し、各非同期状態に応じて表示する。
- **4 つの非同期処理におけるスピナー・状態遷移整理**:
  1. **スキャン中 (`IsScanning = true`)**: 一覧領域に中央スピナーオーバーレイ（「動画ファイルをスキャン中...」）を表示し、未実行プレースホルダーや 0 件 Empty State の誤表示を抑止する。
  2. **AI 提案中 (`IsRequestingAi = true` / `c.IsAiProcessing = true`)**: 各処理中候補行のバッジ（`🌐 検索中`）およびフッター進捗バッジに回転スピナーを表示し、進行中であることを直感的に可視化する。
  3. **リネーム・Undo 実行中 (`IsRenaming = true`)**: 一覧領域に半透明操作ブロックオーバーレイ＋中央スピナー（「ファイル名を変更中...」／「元に戻しています...」）を表示し、多重操作を防止する。
  4. **Web コンテナ操作中 (`IsDockerBusy = true`)**: 上部コンテナステータスバーに小型スピナー＋処理中ラベルを表示し、ボタンを非活性化する。
- **異常終了・タイムアウト・キャンセル時の確実なスピナー解除と回復導線**:
  - すべての非同期分岐（成功・0件・エラー・キャンセル）で各ビジーフラグが必ず `false` に戻り、スピナー残留（無限スピナー）を起こさず再試行または設定修正が可能なエラーメッセージを表示する。

---

## Non-goals（やらないこと）

- Web UI（HTML/JS）側のローディングスピナー追加改修（Step 1 `web-loading-spinners` で完了済み）
- `.devcontainer/devcontainer.json` のマウント定義の廃止（Web 開発コンテナ起動時の `PORT: "5621"` やマウント設定は維持）
- Windows 環境向けコンテナでのハードコード `docker-compose.yml` 復活（単一コンテナ運用は継続）
- サードパーティ NuGet ライブラリの大量追加による過度なアニメーションフレームワーク導入

---

## Capabilities

### Modified Capabilities

- `companion-file-rename`: `.env` / OS環境変数参照の廃止、`appsettings.{Environment}.json` への保存先変更と `%APPDATA%` オーバーライド整合、`appsettings.Development.json` / `appsettings.Production.json` の Git 管理外化、アクティブ環境バッジ表示、およびスキャン・AI提案・リネーム・Undo 処理中のローディングスピナー UI 要件を追加。
- `companion-docker-control`: コンテナ制御中（`IsDockerBusy`）のステータスバー小型スピナー表示および完了・異常時確実解除の要件を追加。
- `docker-infrastructure`: Web アプリケーションにおける `.env` / `.env.sample` の廃止、`appsettings.json` / `appsettings.{Environment}.json` とコンテナ環境変数の `IConfiguration` 一本化要件を追加。
- `container-deployment-scripts`: `scripts/deploy.ps1` / `scripts/deploy.sh` の `.env` 読み込み廃止と `src/TagBasedVideoManager/appsettings*.json` 読み込み仕様へ更新。
- `application-versioning`: L2 規模変更に伴う Web（`0.2.0` → `0.3.0`）および Renamer（`0.1.0` → `0.2.0`）のバージョン更新を反映。

---

## Impact

- **影響コード・設定・ドキュメント**:
  - `.gitignore`, `.env.sample`（削除）
  - `src/TagBasedVideoManager/TagBasedVideoManager.fsproj` (`0.3.0`), `Program.fs`, `HttpHandlers.fs`, `appsettings.json`
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` (`0.2.0`), `Settings.fs`, `State.fs`, `Views.fs`, `appsettings.json`
  - `scripts/deploy.ps1`, `scripts/deploy.sh`, `scripts/common.ps1`
  - `README.md`, `PRODUCTION_ENVIRONMENT.md`
  - テストコード: `test/TagBasedVideoManager.Tests/Unit/ProgramTests.fs` 他 Web 設定テスト、`test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`, `StateTests.fs`, `DeployScriptTests.fs`, GUI 視覚レンダリング E2E テスト
- **既存利用への影響**:
  - 既存ユーザーで `.env` に設定を書いていた場合は、各アプリの `appsettings.Production.json` または `appsettings.Development.json` へ設定を転記する移行が必要（`README.md` / `PRODUCTION_ENVIRONMENT.md` に移行手順を明記）。
