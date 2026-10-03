# Tasks: configuration-specific-appsettings

## 1. 環境別設定ファイルの作成とビルド設定

- [x] 1.1 `src/TagBasedVideoManager.Renamer/appsettings.Development.json` および `appsettings.Production.json` を作成し、旧Debug/Release設定から移行・削除して JSON 構造を適合させる
- [x] 1.2 `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` のビルド定義を修正し、`appsettings*.json` がビルド時にすべて出力ディレクトリへ配置されるようにする

## 2. 階層オーバーライドマージのテスト先行作成 (TDD: Red)

- [x] 2.1 `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` に、`Development` / `Production` 各環境での `appsettings.{Environment}.json` によるキー単位オーバーライドマージ、未定義キーのベース値維持、環境別ファイル不在時の単独動作を検証する仕様妥当性テストを作成し、Red を確認する
- [x] 2.2 `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` に、環境変数（`DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`）解決や明示パス指定、.env補完の回帰テストを追加・整備する

## 3. Settings.fs の階層オーバーライドマージ実装とリファクタ (TDD: Green & Refactor)

- [x] 3.1 `src/TagBasedVideoManager.Renamer/Settings.fs` に環境名解決および `appsettings.json` + `appsettings.{Environment}.json` のキー単位オーバーライドマージロジックを実装し、テストをすべて通過（Green）させる
- [x] 3.2 コードのリファクタと自動フォーマット（`dotnet format` / `npx prettier`）を実施し、全テスト Green を維持する

## 4. 全体検証とエビデンス取得

- [x] 4.1 全単体テストを実行して 100% 通過することを確認し、TRXテストレポートおよびカバレッジレポートを出力して検証する
