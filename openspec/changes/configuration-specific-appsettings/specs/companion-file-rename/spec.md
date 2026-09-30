# Spec Delta: companion-file-rename

## MODIFIED Requirements

### Requirement: .NET標準外部設定ファイル（appsettings.json / .env）からの各種設定値取得と既定命名規則および格納先パス

システムは、.NETにおける一般的な標準構成ファイル名である `appsettings.json` および環境設定ファイル（`.env`）から各種設定値（`OPENROUTER_API_KEY`, `VIDEO_DIR`, `PATH_LENGTH_THRESHOLD`, `SelectedModel`, `Rules`）を自動取得・統合しなければならない（SHALL）。さらに、ビルド構成（Debug / Release）に応じた構成別設定ファイル（`appsettings.Debug.json` / `appsettings.Release.json`）が存在する場合、それを優先して探索・読み込まなければならない（SHALL）。構成別ファイルが存在しない場合は標準の `appsettings.json` へフォールバックしなければならない（SHALL）。OS環境変数は意図しない設定混入を防ぐため参照してはならず（SHALL NOT）、明示的な設定ファイルのみから読み込まなければならない（SHALL）。設定ファイルの探索・読み込みは、①ユーザー個別設定（`%APPDATA%\TagBasedVideoManager\appsettings.json`）、②作業ディレクトリ（`./appsettings.json` または構成別ファイル）、③実行ディレクトリ（`{AppDirectory}\appsettings.json` または構成別ファイル）、④プロジェクトソースディレクトリ（開発実行時）、⑤`.env`、⑥組み込み既定値の順に優先適用しなければならない（SHALL）。また、GUI等で設定を永続化する際は、実行ディレクトリに既存設定がある場合はそれを更新し、それ以外は `%APPDATA%\TagBasedVideoManager\appsettings.json` へ保存しなければならない（SHALL）。設定ファイルに定義された命名規則リストの先頭ルールを、起動時のデフォルト命名規則として自動適用しなければならない（SHALL）。

#### Scenario: appsettings.json および .env からの設定読み込みと既定ルール適用（OS環境変数の非参照）

- **WHEN** OS環境変数が設定されている場合でも、AppDataまたは実行ディレクトリの `appsettings.json` や `.env` に設定値が記述されている状態でアプリを起動したとき
- **THEN** OS環境変数は無視され、.NET標準の探索順序に従って外部設定ファイルの値が優先ロードされ、外部ファイルで定義された先頭の命名規則が起動時のデフォルト命名規則として自動選択されること

#### Scenario: 設定変更時の標準格納先への永続化

- **WHEN** ユーザーが命名規則マネージャー等で設定を変更・保存したとき
- **THEN** 実行ディレクトリに `appsettings.json` が存在する場合はそこへ、存在しない場合は `%APPDATA%\TagBasedVideoManager\appsettings.json` へ安全に保存されること

#### Scenario: Debugビルドにおける appsettings.Debug.json の優先ロード

- **WHEN** Debugビルド構成でアプリが実行され、探索パス内に `appsettings.Debug.json` が存在するとき
- **THEN** `appsettings.Debug.json` の設定値が優先して読み込まれること

#### Scenario: Releaseビルドにおける appsettings.Release.json の優先ロード

- **WHEN** Releaseビルド構成でアプリが実行され、探索パス内に `appsettings.Release.json` が存在するとき
- **THEN** `appsettings.Release.json` の設定値が優先して読み込まれること

#### Scenario: 構成別ファイル不在時の appsettings.json フォールバック

- **WHEN** 構成別設定ファイルが存在せず、`appsettings.json` のみが存在するとき
- **THEN** `appsettings.json` の設定値がフォールバックとして正常に読み込まれること

### Requirement: appsettings.json サンプルのプロジェクト直下配備

システムは、`src/TagBasedVideoManager.Renamer/` ディレクトリ直下に標準的な `appsettings.json` に加え、構成別設定ファイル（`appsettings.Debug.json` および `appsettings.Release.json`）を配備しなければならない（SHALL）。このファイル群には、代表的なフォルダパス、抽出閾値（240）、使用モデル、OpenRouter APIキー設定プレースホルダー、および既定の命名規則リストが正しく記述されなければならない（SHALL）。プロジェクトビルド時、指定されたビルド構成（Debug / Release）に応じた適切な設定ファイルが出力成果物ディレクトリに自動配置・コピーされなければならない（SHALL）。

#### Scenario: appsettings.json のサンプル配備と自動読み込み

- **WHEN** 開発者またはユーザーがプロジェクトを展開したとき
- **THEN** `src/TagBasedVideoManager.Renamer/appsettings.json` が存在し、実行時にも探索・ロード対象となること

#### Scenario: Debugビルド時の出力ディレクトリ配置

- **WHEN** プロジェクトを Debug 構成でビルドしたとき
- **THEN** 出力ディレクトリに Debug 構成の設定ファイルが配置され、アプリ実行時に読み込み可能であること

#### Scenario: Releaseビルド時の出力ディレクトリ配置

- **WHEN** プロジェクトを Release 構成でビルドしたとき
- **THEN** 出力ディレクトリに Release 構成の設定ファイルが配置され、アプリ実行時に読み込み可能であること
