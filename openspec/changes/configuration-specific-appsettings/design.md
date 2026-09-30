# Design: configuration-specific-appsettings

## Context

現状、`TagBasedVideoManager.Renamer` の設定管理（`Settings.fs`）は単一の `appsettings.json` を基準に探索・読み込みを行っています。
ビルド構成（Debug / Release）に応じた分離が行われていないため、開発環境での検証用設定（ローカルフォルダパスやテスト用モデル）と、リリース配布用の設定が混在する懸念があります。
詳細は [proposal.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/configuration-specific-appsettings/proposal.md) を参照してください。

## Goals / Non-Goals

**Goals:**

- Debugビルド時とReleaseビルド時で、それぞれ対応する設定ファイル（`appsettings.Debug.json` / `appsettings.Release.json`）を優先して読み込めるようにする。
- 構成別設定ファイルが存在しない場合でも、既存の `appsettings.json` に安全にフォールバックし、後方互換性を100%維持する。
- MSBuild（`TagBasedVideoManager.Renamer.fsproj`）において、ビルド構成に応じた設定ファイルが出力ディレクトリへ確実に配置されるようにする。
- テストコードから任意のビルド構成（Debug/Release）をシミュレートして検証可能なインターフェースを提供する。

**Non-Goals:**

- 複数設定ファイルの階層的・部分的なキー単位マージ（複雑さを排し、ファイル単位の明確な優先度判定とフォールバックに留める）。
- UIからの設定保存（`Settings.save`）時に構成別ファイルへ書き込むこと（保存先は既存通り実行ディレクトリの `appsettings.json` または AppData 領域）。
- Webサーバー側（`src/TagBasedVideoManager`）の設定構造の変更。

## Decisions

### 1. 構成別ファイル命名規則と配置

- **採用方式**:
  - `src/TagBasedVideoManager.Renamer/appsettings.Debug.json`
  - `src/TagBasedVideoManager.Renamer/appsettings.Release.json`
  - `src/TagBasedVideoManager.Renamer/appsettings.json`（共通フォールバック）
- **理由**:
  .NET のビルド構成名（`$(Configuration)`: `Debug` または `Release`）と1対1で対応し、開発者にとって最も直感的かつ予測可能な構成であるため。

### 2. MSBuild 出力制御（.fsproj）

- **採用方式**:
  MSBuild の条件分岐（`Condition="'$(Configuration)' == '...'"`）を用いて、各ビルド構成に応じた構成別ファイルと、フォールバック用 `appsettings.json` の両方を出力ディレクトリにコピーする。
  ```xml
  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <Content Include="appsettings.Debug.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <Content Include="appsettings.Release.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
    <Content Include="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  ```
- **理由**:
  ビルド出力ディレクトリに必要なファイルがコピーされ、ポータブル実行時でも構成に応じたファイルとフォールバックファイルの両方が揃うため。

### 3. Settings.fs での構成判定と探索優先度

- **採用方式**:
  1. コンパイル時シンボル `#if DEBUG` による実行時既定構成の解決:
     ```fsharp
     let defaultConfigurationName =
         #if DEBUG
         "Debug"
         #else
         "Release"
         #endif
     ```
  2. 探索ヘルパー `resolveConfigurationFile`:
     指定されたディレクトリにおいて、
     `appsettings.{configuration}.json` が存在すればそれを優先採用し、
     存在しなければ `appsettings.json` を採用する。
  3. 各探索ロケーション（プロジェクトソース直下、カレント、実行ディレクトリ）に対して上記ヘルパーを適用。
  4. テスト用に関数引数で構成名をオーバーライド可能な `loadConfigurationWithConfig` を用意し、既存の `loadConfiguration` は `loadConfigurationWithConfig defaultConfigurationName` を呼び出す。
- **理由**:
  テストコードで環境を汚染することなく「Debug時の動作」「Release時の動作」を完全にテスト可能とし、本番コードではゼロオーバーヘッドでコンパイル時構成が反映されるため。

### 4. 設定値の初期値設計

- **`appsettings.Debug.json`**:
  - `targetDirectory`: テスト用ディレクトリ（`test/videos` または開発用パス）
  - `pathLengthThreshold`: 240
  - `selectedModel`: 開発・動作確認用軽量モデル
  - `rules`: 開発用推奨ルール
- **`appsettings.Release.json`**:
  - `targetDirectory`: 空文字または一般的な動画フォルダパス
  - `pathLengthThreshold`: 240
  - `selectedModel`: 本番推奨モデル
  - `rules`: 本番用推奨ルール
- **`appsettings.json`**:
  - 既存の標準フォールバック設定を維持。

## Risks / Trade-offs

- **[Risk]** テスト実行環境（`xUnit`）は通常 Debug 構成で実行されるため、Release構成の探索ロジックが実コードパスで通りにくい。
  - **[Mitigation]** 構成名を明示的に指定できる内部関数 `loadConfigurationWithConfig` を公開し、テストコードから `"Debug"` および `"Release"` を明示指定したテストケースを作成して両方のパスを網羅する。
- **[Risk]** 開発者が `appsettings.Debug.json` のみを編集し、`appsettings.Release.json` のスキーマ定義が古くなる。
  - **[Mitigation]** 両ファイルとも同一の `RenamerSettings` 型にデシリアライズ可能であることをテストで検証する。
