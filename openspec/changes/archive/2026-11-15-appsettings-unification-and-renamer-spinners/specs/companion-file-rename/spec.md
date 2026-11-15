# companion-file-rename Delta Specification

## RENAMED Requirements

- FROM: `### Requirement: .NET標準外部設定ファイル（appsettings.json / .env）からの各種設定値取得と既定命名規則および格納先パス`
- TO: `### Requirement: .NET標準外部設定ファイル（appsettings.json / appsettings.{Environment}.json）からの設定取得と環境別ファイルへの保存`
- FROM: `### Requirement: appsettings.json サンプルのプロジェクト直下配備`
- TO: `### Requirement: クリーンなベース設定ファイル（appsettings.json）の配備と環境別ファイル不在時の安全なビルド・起動`
- FROM: `### Requirement: 開発環境設定ファイル（appsettings.Development.json）のGit除外`
- TO: `### Requirement: 環境別設定ファイル（appsettings.Development.json / appsettings.Production.json）のGit管理完全除外`

---

## MODIFIED Requirements

### Requirement: .NET標準外部設定ファイル（appsettings.json / appsettings.{Environment}.json）からの設定取得と環境別ファイルへの保存

システムは、`.env` ファイルおよび汎用 OS 環境変数（`VIDEO_DIR`, `OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `PATH_LENGTH_THRESHOLD`）を一切参照してはならず（SHALL NOT）、.NET 標準構成思想に基づきベース設定ファイルである `appsettings.json` および環境別設定ファイル（`appsettings.{Environment}.json`：`appsettings.Development.json`、`appsettings.Production.json` 等）のみから各種設定値（`TargetDirectory`, `PathLengthThreshold`, `SelectedModel`, `ApiKey`, `Rules`）を取得・統合マージしなければならない（SHALL）。
環境名（`Environment`）は、環境変数（`DOTNET_ENVIRONMENT` または `ASPNETCORE_ENVIRONMENT`）が設定されている場合はそれを最優先（パストラバーサル文字や空白のみの値は無効として既定値へフォールバック）とし、未設定の場合はコンパイル時ビルド構成（Debug 構成時は `Development`、Release 構成時は `Production`）に自動解決しなければならない（SHALL）。
設定値の読み込みは、組み込み既定値を初期値とした上で、ベース探索階層（プロジェクトソースディレクトリ / カレントディレクトリ / 実行ディレクトリ `{AppDirectory}`）の `appsettings.json` および `appsettings.{Environment}.json` をマージし、さらに最優先オーバーライド層としてユーザー個別領域（`%APPDATA%\TagBasedVideoManager\appsettings.json` および `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json`）が存在する場合にキー単位でオーバーライドマージしなければならない（SHALL）。環境別ファイルが存在しない場合は `appsettings.json` のみ、ベースも環境別も存在しない場合は組み込み既定値のみで安全に起動しなければならない（SHALL）。
また、GUI 等で設定を永続化する際は、ベース `appsettings.json` ではなく**現在の実行環境名に対応する `appsettings.{Environment}.json`** へ保存しなければならない（SHALL）。保存先ディレクトリは、プロジェクトルート／実行ディレクトリ等に書込可能かつ既存構成が存在する場合はそのディレクトリの `appsettings.{Environment}.json`（未存在時は新規作成・既存時はキーを保持しつつ更新）、書込権限がない（Read-Only）または配置先不在の場合は `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` へ自動フォールバックしてクラッシュを防止しなければならない（SHALL）。設定ファイルに定義された命名規則リストの先頭ルールを、起動時のデフォルト命名規則として自動適用しなければならない（SHALL）。

#### Scenario: .env および VIDEO_DIR OS環境変数を参照しない純粋な appsettings マージ読み込み（移行通知付き）

- **WHEN** `.env` ファイルまたは OS 環境変数 `VIDEO_DIR` / `OPENROUTER_API_KEY` が存在する状態で AI File Renamer を起動したとき
- **THEN** `.env` および OS 環境変数の設定値は無視され、`appsettings.json` と `appsettings.{Environment}.json` のマージ結果のみが適用され、`.env` が検出された場合は廃止・移行を促す診断ログ/案内が出力されること

#### Scenario: 設定ファイル JSON 構文エラー時の安全な起動と警告通知

- **WHEN** `appsettings.json` または `appsettings.{Environment}.json` に不正な JSON 構文エラーが含まれる状態で起動したとき
- **THEN** アプリはクラッシュせず有効な下位設定層または組み込み既定値で安全に起動し、構文エラーが発生したファイルパスとエラー内容を画面バナー・ログへ警告表示すること

#### Scenario: アクティブ環境の appsettings.{Environment}.json への設定保存（実行ディレクトリ）

- **WHEN** 実行環境が `Development` の状態で、書込可能な実行ディレクトリ（またはプロジェクト設定ディレクトリ）をベースに運用しており、ユーザーが設定・命名規則・モデル変更を保存したとき
- **THEN** `appsettings.json`（ベーステンプレート）は書き換えられず、アクティブ環境に対応する `appsettings.Development.json` に保存・更新されること

#### Scenario: 書込権限がない環境での %APPDATA% 環境別ファイルへのフォールバック保存と再読込整合

- **WHEN** 実行ディレクトリが読み取り専用またはベース未配置の環境で、実行環境 `Production` としてユーザーが設定を保存したとき
- **THEN** 例外終了せず `%APPDATA%\TagBasedVideoManager\appsettings.Production.json` に設定が書き込まれ、次回起動時も同ファイルが最優先オーバーライドとして読み込まれること

#### Scenario: Development / Production 各環境でのオーバーライドマージと未定義キーのフォールバック

- **WHEN** `Development` または `Production` 環境で対応する `appsettings.{Environment}.json` に一部キーのみ定義されているとき
- **THEN** 定義されたキーのみが `appsettings.json` をオーバーライドし、未定義キーは `appsettings.json` の値が維持されること

#### Scenario: 不正な環境名指定時の安全なフォールバック

- **WHEN** `DOTNET_ENVIRONMENT` に空文字やパストラバーサル文字列（例: `../evil`）が指定されたとき
- **THEN** 不正なパス結合を行わず、安全なデフォルト環境名（Debug 時は `Development`、Release 時は `Production`）へフォールバックすること

### Requirement: クリーンなベース設定ファイル（appsettings.json）の配備と環境別ファイル不在時の安全なビルド・起動

システムは、プロジェクト直下（`src/TagBasedVideoManager.Renamer/appsettings.json`）に個人環境パスや API キー等の秘密情報を含まないクリーンなベーステンプレート `appsettings.json` を配備しなければならない（SHALL）。環境別ファイル（`appsettings.Development.json` / `appsettings.Production.json`）はプロジェクト定義上 `Condition="Exists(...)"` 付きで参照されなければならず（SHALL）、クローン直後や CI 環境など環境別実ファイルが存在しない状態でもビルド（Debug / Release 双方）およびアプリ起動がエラーなく成功しなければならない（SHALL）。

#### Scenario: 環境別ファイル不在時のビルドおよび起動成功

- **WHEN** `appsettings.Development.json` および `appsettings.Production.json` がローカルに存在せず `appsettings.json` のみが存在する状態で `dotnet build` および起動を実行したとき
- **THEN** MSBuild エラーやランタイム例外が発生せず、ベース設定 `appsettings.json` で正常に起動すること

#### Scenario: ベース設定ファイルの秘密情報・個人パス非混入

- **WHEN** リポジトリに追跡されている `src/TagBasedVideoManager.Renamer/appsettings.json` を検査したとき
- **THEN** 開発者個人のドライブパスや有効な API キーが含まれておらず、初期テンプレートとして安全な既定値のみが定義されていること

### Requirement: 環境別設定ファイル（appsettings.Development.json / appsettings.Production.json）のGit管理完全除外

システムは、ローカル環境固有のディレクトリパスや API キーを格納する環境別実ファイル（`appsettings.Development.json` および `appsettings.Production.json`）を `.gitignore`（`**/appsettings.Development.json`, `**/appsettings.Production.json`）へ登録し、Web・デスクトップ両プロジェクトにおいて Git バージョン管理対象から完全に除外しなければならない（SHALL）。また、`.env` や `.env.sample` への依存ファイルはリポジトリに保持してはならない（SHALL NOT）。

#### Scenario: appsettings.Development.json / appsettings.Production.json の Git 非追跡

- **WHEN** Web または Renamer ディレクトリに `appsettings.Development.json` または `appsettings.Production.json` を作成・編集したとき
- **THEN** 両ファイルとも Git の追跡対象・未追跡差分（`git status`）に現れず、リポジトリに誤コミットされないこと

#### Scenario: 既存追跡環境ファイルおよび .env.sample のリポジトリ除外確認

- **WHEN** `git ls-files` で追跡中ファイルを検査したとき
- **THEN** `.env.sample`、`appsettings.Development.json`、および `appsettings.Production.json` のいずれも Git インデックスに登録されていないこと

---

## ADDED Requirements

### Requirement: アクティブ構成環境および保存先ファイル名のGUI明示表示

システムは、AI File Renamer のメイン画面（ヘッダーまたはフッター領域）に、現在適用されている実行環境名（例: `DEV` / `PROD`）および設定保存先の環境別ファイル名（例: `appsettings.Development.json` / `appsettings.Production.json`）を視認性の高いバッジとして表示しなければならない（SHALL）。

#### Scenario: 画面上のアクティブ環境バッジ表示

- **WHEN** AI File Renamer を `Development` または `Production` 環境で起動したとき
- **THEN** メイン画面上に現在の環境名バッジと保存先ファイル名（`appsettings.Development.json` または `appsettings.Production.json`）が表示されること

### Requirement: スキャン・AI提案・リネーム/Undo実行中のローディングスピナーUIと確実な解除および回復導線

システムは、AI File Renamer におけるすべての非同期・時間のかかる処理および Zero-State / Error-State において、Fluent Dark テーマと調和する回転スピナーアニメーションおよび適切な回復導線を表示し、処理完了・キャンセル・エラー終了時にはスピナーを確実に解除して次の状態へ遷移させなければならない（SHALL）。

1. **初回・フォルダ未設定時の Zero-State ガイダンス**: `TargetDirectory` が未設定（空文字）である場合は、誤ってスキャンを実行して即エラーとならないよう、一覧領域にフォルダ選択を促す Zero-State ガイダンス（「対象フォルダが未設定です。上の『📁 参照...』からフォルダを選択してください」）を表示しなければならない（SHALL）。
2. **フォルダスキャン中 (`IsScanning = true`)**: 候補一覧領域に未実行プレースホルダーや Empty State を表示してはならず（SHALL NOT）、中央に回転スピナーおよび「動画ファイルをスキャン中...」メッセージを表示しなければならない（SHALL）。また、スキャン完了後の 0 件判定時は「フォルダ内に動画が 0 本であった」場合と「動画を 1 本以上検査し制限超過ファイルが 0 件であった（安全）」場合を明確に出し分けなければならない（SHALL）。
3. **AI 提案処理中 (`IsRequestingAi = true` / `c.IsAiProcessing = true`) および個別失敗回復**: フッターの進捗バッジ（「AI提案中... (x/y件完了)」）および処理中の各候補カード（`c.IsAiProcessing = true`）に回転スピナーをインライン表示しなければならない（SHALL）。また、特定ファイルの AI 提案が HTTP エラー・レート制限・タイムアウト等で個別失敗した場合は、該当カードのみ赤枠エラー状態（失敗理由および「↻ この項目を再試行」ボタン付き）として表示し、他ファイルの処理・結果を維持したまま個別再試行できなければならない（SHALL）。
4. **リネーム・Undo 実行中 (`IsRenaming = true`)**: 候補一覧領域上に半透明の操作ブロックオーバーレイと中央スピナー（「ファイル名を変更中...」または「変更を元に戻しています...」）を表示し、処理完了またはエラー発生までカード編集・条件変更・再実行を防止しなければならない（SHALL）。
5. **スキャン・リネーム中の入力排他およびエラー時確実解除**: `IsScanning = true` または `IsRenaming = true` の間はディレクトリ・閾値・ルール・モデルの変更入力を受け付けず状態整合性を保護しなければならない（SHALL）。走査エラー、AI 提案キャンセル／タイムアウト、リネーム失敗時は、いずれのビジーフラグも `false` に戻してスピナーを解除し、ユーザーが原因を確認して設定変更・再試行を行えるエラーメッセージを表示しなければならない（SHALL）。

#### Scenario: 対象フォルダ未設定時の Zero-State ガイダンス表示

- **WHEN** `TargetDirectory` が空のクリーン設定状態で AI File Renamer を起動したとき
- **THEN** 一覧領域にフォルダ未設定の案内メッセージとフォルダ選択を促す表示が現れ、未設定のまま誤ってスキャンが実行されないこと

#### Scenario: ディレクトリスキャン中のスピナー表示と Empty State 抑止・0件出し分け

- **WHEN** ユーザーが有効なディレクトリを指定して「⚡ 抽出 ＆ AI提案を実行」をクリックし、`IsScanning = true` となったとき
- **THEN** 一覧領域に未実行案内や Empty State は表示されず、中央に回転スピナーと「動画ファイルをスキャン中...」が表示されること
- **WHEN** スキャンが完了し対象候補が 0 件であったとき
- **THEN** フォルダ内に動画ファイル自体が 0 本の場合と、1 本以上を検査して全件安全基準内であった場合とでメッセージが明確に出し分けられること

#### Scenario: AI 提案処理中の行インラインスピナーおよび個別カード失敗・再試行

- **WHEN** スキャンが完了し、候補ファイルに対して非同期 AI 提案が実行されているとき（`IsRequestingAi = true`）
- **THEN** 処理中の候補カード上に回転スピナー付きで「検索・AI提案中...」が表示され、フッターにも回転スピナー付きの完了件数進捗バッジが表示されること
- **WHEN** 特定の候補カードで API エラーやタイムアウトにより AI 提案が個別失敗したとき
- **THEN** 該当カードのスピナーが解除されて失敗理由と「↻ 再試行」ボタンがカード内に表示され、他候補の成功結果は維持されたまま個別再試行できること

#### Scenario: リネームおよび Undo 実行中の操作ブロックオーバーレイスピナー表示

- **WHEN** ユーザーがリネーム実行または Undo を実行し `IsRenaming = true` となったとき
- **THEN** 候補一覧領域に半透明オーバーレイと中央スピナーが表示され、完了後にスピナーが解除されて最新リストへ遷移すること

#### Scenario: 非同期処理エラー・キャンセル時のスピナー確実解除と入力排他維持

- **WHEN** フォルダ不在エラー、API エラー、または「⏹ 中止」ボタンによるキャンセルが発生したとき
- **THEN** すべてのスピナーが即座に解除され（`IsScanning = false`, `IsRequestingAi = false`, `IsRenaming = false`）、操作ボタンが回復してエラー通知または再開可能な待機状態に戻ること
