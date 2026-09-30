# Proposal: configuration-specific-appsettings

## Why

現在、`src/TagBasedVideoManager.Renamer` の設定ファイルは単一の `appsettings.json` のみで管理されており、Debugビルド（開発・テスト・デバッグ時）とReleaseビルド（本番運用・配布時）で設定内容（対象ディレクトリ、APIキー、使用モデル、パス長閾値など）を分離できません。
このため、開発用のローカルパスや検証用設定がReleaseビルド成果物に誤って混入したり、Gitコミット時に開発環境固有の設定が紛れ込むリスクがあります。ビルド構成（Debug / Release）に応じて設定ファイルを分離・切り替えられるようにすることで、開発の安全性と運用の利便性を向上させます。

## What Changes

- **構成別設定ファイルの配置**:
  - `src/TagBasedVideoManager.Renamer/appsettings.Debug.json` を新設（開発・テスト用設定）。
  - `src/TagBasedVideoManager.Renamer/appsettings.Release.json` を新設（本番・配布用既定設定）。
  - 既存の `appsettings.json` は共通既定値およびフォールバック用として維持。
- **MSBuild プロジェクト定義の更新**:
  - `TagBasedVideoManager.Renamer.fsproj` において、`$(Configuration)` に応じてビルド出力ディレクトリへ対応する設定ファイルをコピー（または `appsettings.json` として配置）する設定を追加。
- **Settings ロード処理の構成対応**:
  - `Settings.fs` の `loadConfiguration` において、ビルド構成（Debug / Release）に応じた設定ファイル（`appsettings.Debug.json` / `appsettings.Release.json`）を優先して探索し、存在しない場合は `appsettings.json` へフォールバックするロジックを導入。
- **テストの追加・更新**:
  - `SettingsTests.fs` において、構成別設定ファイルの優先読み込みおよびフォールバック動作を検証する単体テストを追加。

## Non-goals

- Webサーバー側（`src/TagBasedVideoManager`）の設定ファイル構造の変更（本変更は `src/TagBasedVideoManager.Renamer` に限定）。
- JSON階層マージエンジンの導入（複雑な部分上書きではなく、ファイル単位の構成別切り替えおよびフォールバックでシンプルに実現）。
- UIからの設定保存（`Settings.save`）時に構成別ファイル（`appsettings.Debug.json` 等）へ逆書き込みする機能（保存先は既存仕様通り実行ディレクトリの `appsettings.json` または AppData 領域を対象とする）。

## Capabilities

### Modified Capabilities

- `companion-file-rename`: Debug/Releaseビルド構成に応じた設定ファイル（`appsettings.Debug.json` / `appsettings.Release.json`）の探索・読み込みおよびプロジェクト配置の要件を追加。

## Impact

- 影響コード:
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`
  - `src/TagBasedVideoManager.Renamer/Settings.fs`
  - `src/TagBasedVideoManager.Renamer/appsettings.Debug.json`（新規）
  - `src/TagBasedVideoManager.Renamer/appsettings.Release.json`（新規）
  - `src/TagBasedVideoManager.Renamer/appsettings.json`（更新）
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`
- 既存機能への影響:
  - 構成別設定ファイルが存在しない場合は既存の `appsettings.json` を読み込むため、完全な後方互換性を維持。
