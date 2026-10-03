# Spec Delta: companion-file-rename

## MODIFIED Requirements

### Requirement: .NET標準外部設定ファイル（appsettings.json / .env）からの各種設定値取得と既定命名規則および格納先パス

システムは、.NETにおける標準構成思想に基づき、ベース設定ファイルである `appsettings.json` および環境別設定ファイル（`appsettings.{Environment}.json`：`appsettings.Development.json` や `appsettings.Production.json` 等）、ならびに環境設定ファイル（`.env`）から各種設定値（`OPENROUTER_API_KEY`, `VIDEO_DIR`, `PATH_LENGTH_THRESHOLD`, `SelectedModel`, `Rules`）を自動取得・統合マージしなければならない（SHALL）。環境名（`Environment`）は、OS環境変数（`DOTNET_ENVIRONMENT` または `ASPNETCORE_ENVIRONMENT`）が設定されている場合はそれを最優先とし、未設定の場合はコンパイル時ビルド構成（Debug構成時は `Development`、Release構成時は `Production`）に自動解決しなければならない（SHALL）。設定値の読み込みは、まず `appsettings.json` をロードした上で、該当環境の `appsettings.{Environment}.json` が存在する場合に各設定プロパティ（対象ディレクトリ、モデル、APIキー、閾値、命名規則等）をキー単位でオーバーライドマージしなければならない（SHALL）。環境別ファイルで未定義のキーは `appsettings.json` の値が維持されなければならない（SHALL）。環境別ファイルが存在しない場合は標準の `appsettings.json` のみで正常動作しなければならない（SHALL）。さらに `.env` が存在する場合は指定された項目をオーバーライドしなければならない（SHALL）。設定ファイルの探索・読み込みは、①ユーザー個別設定（`%APPDATA%\TagBasedVideoManager\appsettings.json`）、②作業ディレクトリ（`./` 配下のベースおよび環境別ファイル）、③実行ディレクトリ（`{AppDirectory}` 配下のベースおよび環境別ファイル）、④プロジェクトソースディレクトリ（開発実行時）、⑤`.env`、⑥組み込み既定値の順に優先適用しなければならない（SHALL）。また、GUI等で設定を永続化する際は、実行ディレクトリに既存設定がある場合はそれを更新し、それ以外は `%APPDATA%\TagBasedVideoManager\appsettings.json` へ保存しなければならない（SHALL）。設定ファイルに定義された命名規則リストの先頭ルールを、起動時のデフォルト命名規則として自動適用しなければならない（SHALL）。

#### Scenario: appsettings.json および .env からの設定読み込みと既定ルール適用（OS環境変数の非参照）

- **WHEN** AppDataまたは実行ディレクトリの `appsettings.json` や `.env` に設定値が記述されている状態でアプリを起動したとき
- **THEN** .NET標準の探索順序に従って外部設定ファイルの値が優先ロードされ、外部ファイルで定義された先頭の命名規則が起動時のデフォルト命名規則として自動選択されること

#### Scenario: 設定変更時の標準格納先への永続化

- **WHEN** ユーザーが命名規則マネージャー等で設定を変更・保存したとき
- **THEN** 実行ディレクトリに `appsettings.json` が存在する場合はそこへ、存在しない場合は `%APPDATA%\TagBasedVideoManager\appsettings.json` へ安全に保存されること

#### Scenario: Development環境における appsettings.Development.json によるオーバーライドマージ

- **WHEN** 実行環境が Development（Debugビルド時または環境変数指定時）で実行され、`appsettings.Development.json` が存在するとき
- **THEN** `appsettings.json` のベース設定が読み込まれた上で、`appsettings.Development.json` に定義された各設定キーがオーバーライドマージされ、未定義キーは `appsettings.json` の値が維持されること

#### Scenario: Production環境における appsettings.Production.json によるオーバーライドマージ

- **WHEN** 実行環境が Production（Releaseビルド時または環境変数指定時）で実行され、`appsettings.Production.json` が存在するとき
- **THEN** `appsettings.json` のベース設定が読み込まれた上で、`appsettings.Production.json` に定義された各設定キーがオーバーライドマージされ、未定義キーは `appsettings.json` の値が維持されること

#### Scenario: 環境別ファイル不在時の appsettings.json 単独動作

- **WHEN** 該当する環境別設定ファイル（`appsettings.{Environment}.json`）が存在せず、`appsettings.json` のみが存在するとき
- **THEN** エラーなく `appsettings.json` のベース設定値のみが正常に読み込まれること

#### Scenario: 環境変数による環境名の明示的切り替え

- **WHEN** `DOTNET_ENVIRONMENT` または `ASPNETCORE_ENVIRONMENT` に特定の環境名（例: `Production`）が設定されているとき
- **THEN** ビルド構成に関わらず指定された環境名に対応する `appsettings.{Environment}.json` が優先してマージされること

### Requirement: appsettings.json サンプルのプロジェクト直下配備

システムは、`src/TagBasedVideoManager.Renamer/` ディレクトリ直下に標準的なベース設定ファイル `appsettings.json` に加え、環境別設定ファイル（`appsettings.Development.json` および `appsettings.Production.json`）を配備しなければならない（SHALL）。このファイル群には、代表的なフォルダパス、抽出閾値（240）、使用モデル、OpenRouter APIキー設定プレースホルダー、および既定の命名規則リストが正しく記述されなければならない（SHALL）。プロジェクトビルド時、すべての環境設定ファイル（`appsettings*.json`）が出力成果物ディレクトリに自動配置・コピーされなければならない（SHALL）。

#### Scenario: appsettings.json のサンプル配備と自動読み込み

- **WHEN** 開発者またはユーザーがプロジェクトを展開したとき
- **THEN** `src/TagBasedVideoManager.Renamer/` 直下に `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` が存在し、実行時にも探索・ロード対象となること

#### Scenario: ビルド時の出力ディレクトリへの環境設定ファイル群配置

- **WHEN** プロジェクトをビルド（Debug / Release いずれも）したとき
- **THEN** 出力ディレクトリに `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` がすべて配置され、実行時に任意の環境で読み込み可能であること
