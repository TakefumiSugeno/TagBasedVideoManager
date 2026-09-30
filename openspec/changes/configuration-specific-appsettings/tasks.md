# Tasks: configuration-specific-appsettings

## 1. 構成別設定ファイルの作成とビルド設定

- [x] 1.1 `src/TagBasedVideoManager.Renamer/appsettings.Debug.json` および `appsettings.Release.json` を作成し、JSON構造が `RenamerSettings` と適合することを検証する
- [ ] 1.2 `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` に MSBuild の構成別コピー定義を追加し、`dotnet build -c Debug` および `dotnet build -c Release` 実行時に各出力ディレクトリへ正しく配置されることを検証する

## 2. 構成別設定読み込みのテスト先行作成 (TDD: Red)

- [ ] 2.1 `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` に、構成名（Debug/Release）に応じた `appsettings.{Configuration}.json` 優先読み込みおよび `appsettings.json` へのフォールバックを検証する仕様妥当性検証テストを作成し、意図通りテストが失敗（Red）することを確認する
- [ ] 2.2 `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` に、既存の明示パス指定・.env読み込み・AppDataフォールバック動作が壊れていないことを保証する回帰テストを追加・整備する

## 3. Settings.fs の構成別読み込み実装とリファクタ (TDD: Green & Refactor)

- [ ] 3.1 `src/TagBasedVideoManager.Renamer/Settings.fs` にコンパイル時ビルド構成判定および構成別ファイル（`appsettings.{Configuration}.json`）の優先探索・フォールバック処理を実装し、作成したテストをすべて通過（Green）させる
- [ ] 3.2 `Settings.fs` のファイル探索処理をリファクタしてDRYを徹底し、自動フォーマット（`dotnet format`）を実行して全テストがGreenを維持することを確認する

## 4. 全体検証とエビデンス取得

- [ ] 4.1 `dotnet test` を実行して全単体テストが通過することを確認し、TRXテストレポートを出力して検証する
