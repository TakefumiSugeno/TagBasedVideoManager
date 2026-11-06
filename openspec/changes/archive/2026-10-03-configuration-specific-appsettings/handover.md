# Handover: configuration-specific-appsettings (申し送り事項・技術的負債・運用メモ)

## 1. 概要

本ドキュメントは、変更 `configuration-specific-appsettings`（ASP.NET Core / .NET 標準構成思想に準拠した環境別設定ファイル `appsettings.json` / `appsettings.{Environment}.json` の階層マージおよび環境名解決）の実装完了に伴う申し送り事項・技術的負債・運用上の留意点を記録したものです。

## 2. 実装された仕様特性と既定挙動

### (1) 環境名の解決順序

- 優先度 1: OS 環境変数 `DOTNET_ENVIRONMENT`
- 優先度 2: OS 環境変数 `ASPNETCORE_ENVIRONMENT`
- 優先度 3: コンパイル時ビルド構成（Debug ビルド時: `Development`、Release ビルド時: `Production`）

### (2) 設定ファイルの階層マージ方式

- ベース設定 `appsettings.json` を読み込んだ上で、解決された環境名に対応する `appsettings.{Environment}.json`（存在する場合）の各キー（`PathLengthThreshold`, `SelectedModel`, `VideoDir`, `OpenRouterApiKey`, `Rules`）をキー単位でオーバーライドマージします。
- 環境別設定で未定義のキーは、ベース設定ファイルの値がそのまま維持されます。
- 該当する環境別設定ファイルが存在しない場合でもエラーにはならず、ベース設定ファイルのみで単独動作します。
- さらに `.env` が併存する場合、指定されたキーが最終的にオーバーライドされます。

### (3) テスト時の設定探索スコープ隔離

- `Settings.loadConfigurationWithConfig` の `envPathOpt` に明示的なディレクトリパスが渡された場合、その親ディレクトリのみから `appsettings*.json` を探索し、リポジトリルートやプロジェクト直下の実ファイルへのフォールバックを遮断します。これによりテストの一時ファイルとの完全な環境隔離が保証されています。

## 3. 将来的な改善・リファクタ候補（技術的負債の整理）

1. **`Microsoft.Extensions.Configuration` パッケージ導入の検討**:
   - 現状の実装は、外部 NuGet 依存を最小限に抑えフットプリントを軽量に保つため、.NET 組み込みの `System.Text.Json`（`JsonDocument`）を活用した自作のマージ処理（`mergePartials`）を行っています。
   - 現状のフラットな設定構造（および `Rules` 配列）では完全に堅牢に動作していますが、将来的に設定の階層が3階層以上に深くなったり、コマンドライン引数プロバイダーや Azure KeyVault プロバイダー等の多様な構成ソースを追加したくなった場合は、公式の `Microsoft.Extensions.Configuration` NuGet パッケージへの置き換えを検討してください。

2. **本番配布パッケージング時のファイル同梱ポリシー**:
   - 現状の `.fsproj` 定義では `appsettings*.json` を一括で `CopyToOutputDirectory` に指定しているため、Release ビルドの出力ディレクトリにも `appsettings.Development.json` が配置されます（動作上は Release ビルドでは `Production` が解決されるため実害はありません）。
   - 将来的に配布用インストーラー（msix / zip / exe）を作成する際は、パッケージングスクリプト等で `appsettings.Development.json` を同梱から除外するフィルタリングを導入してもよいでしょう。

## 4. Apply フェーズでの保留指摘・申し送り事項

- **保留指摘**: なし（Task 1 〜 Task 4 の全フェーズで PG Agent / QA Agent より LGTM を取得済み）。
- **テスト通過エビデンス**:
  - 全91件の単体テストが 100% Pass。
  - TRX レポート: `test/TestResults/TestRun_2026-10-03_16_12_47/TestRun_2026-10-03_16_12_47_Renamer_net10.0.trx`
  - カバレッジレポート: `test/TestResults/TestRun_2026-10-03_16_12_47/CoverageReport/index.html`
