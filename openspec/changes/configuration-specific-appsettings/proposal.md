# Proposal: configuration-specific-appsettings

## Why

現在、`src/TagBasedVideoManager.Renamer` の設定ファイルは単一の `appsettings.json` のみで管理されており、開発環境（Debugビルド・ローカル開発時）と本番運用・配布環境（Releaseビルド・本番運用時）で設定内容（対象ディレクトリ、APIキー、使用モデル、パス長閾値など）を分離・オーバーライドできません。
このため、開発用のローカルパスや検証用設定がReleaseビルド成果物に誤って混入したり、Gitコミット時に開発環境固有の設定が紛れ込むリスクがあります。
Microsoft 公式の構成ガイドライン（ASP.NET Core / .NET の標準構成プロバイダー）に準拠し、`appsettings.json` をベースとしつつ環境別設定（`appsettings.Development.json` / `appsettings.Production.json`）によるオーバーライドマージを可能にすることで、開発の安全性と運用の柔軟性を向上させます。

## What Changes

- **環境別設定ファイルの配置**:
  - `src/TagBasedVideoManager.Renamer/appsettings.json`（ベース共通・既定設定）。
  - `src/TagBasedVideoManager.Renamer/appsettings.Development.json`（開発環境用オーバーライド設定）。
  - `src/TagBasedVideoManager.Renamer/appsettings.Production.json`（本番環境用オーバーライド設定）。
- **MSBuild プロジェクト定義の更新**:
  - `TagBasedVideoManager.Renamer.fsproj` において、`appsettings*.json` がビルド出力ディレクトリへ通常配置されるよう設定（条件分岐によるファイル差し替えではなく、すべての環境別ファイルを出力する .NET 標準方式）。
- **Settings ロード処理の環境別オーバーライドマージ対応**:
  - `Settings.fs` の `loadConfiguration` において、環境名（`Development` / `Production` 等）を解決し、まず `appsettings.json` を読み込んだ上で、該当する `appsettings.{Environment}.json` が存在すればその値でキー単位のオーバーライドマージを行う。
  - 環境名は、環境変数（`DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`）を優先し、未設定時はコンパイル時構成（Debug なら `Development`、Release なら `Production`）に自動フォールバック。
  - さらに `.env` が存在する場合は指定項目をオーバーライド。
- **テストの追加・更新**:
  - `SettingsTests.fs` において、`Development` / `Production` 各環境設定のオーバーライドマージ、未定義項目のベースフォールバック、環境変数指定、明示パス指定が期待通り動作することを検証する単体テストを追加・更新。

## Non-goals

- Webサーバー側（`src/TagBasedVideoManager`）の設定ファイル構造の変更（本変更は `src/TagBasedVideoManager.Renamer` に限定）。
- UIからの設定保存（`Settings.save`）時に環境別ファイル（`appsettings.Development.json` 等）へ逆書き込みする機能（保存先は既存仕様通り実行ディレクトリの `appsettings.json` または AppData 領域を対象とする）。

## Capabilities

### Modified Capabilities

- `companion-file-rename`: .NET 標準構成（`appsettings.json` + `appsettings.{Environment}.json`）によるオーバーライドマージ、およびプロジェクト配置（`appsettings.Development.json`, `appsettings.Production.json`）の要件を定義。

## Impact

- 影響コード:
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`
  - `src/TagBasedVideoManager.Renamer/Settings.fs`
  - `src/TagBasedVideoManager.Renamer/appsettings.json`
  - `src/TagBasedVideoManager.Renamer/appsettings.Development.json`（新規）
  - `src/TagBasedVideoManager.Renamer/appsettings.Production.json`（新規）
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`
- 既存機能への影響:
  - 環境別設定ファイルが存在しない場合は `appsettings.json` のみが読み込まれるため、完全な後方互換性を維持。
