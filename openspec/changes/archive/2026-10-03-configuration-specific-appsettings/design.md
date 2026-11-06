# Design: configuration-specific-appsettings

## Context

現在、`TagBasedVideoManager.Renamer` の設定管理（`Settings.fs`）では単一の `appsettings.json` を読み込んでいます。
開発環境（ローカル開発・Debugビルド）と本番・運用環境（Releaseビルド・配布）において、対象ディレクトリや使用モデル、APIキーなどの設定を分離・オーバーライドすることができませんでした。
Microsoft 公式の構成ガイドライン（[ASP.NET Core の構成](https://learn.microsoft.com/ja-jp/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)）に準拠し、`appsettings.json` をベースとしつつ、環境名（`Development` / `Production` 等）に応じた `appsettings.{Environment}.json` によるキー単位のオーバーライドマージをサポートします。

## Goals / Non-Goals

**Goals:**

- .NET 標準の構成プロバイダー思想に基づき、ベース設定（`appsettings.json`）に対し環境別設定（`appsettings.Development.json` / `appsettings.Production.json`）をキー単位でオーバーライドマージする。
- 環境別ファイルで定義されていないキーは、ベース `appsettings.json` の値が維持される（部分オーバーライド）。
- 環境別ファイルが存在しない場合でも、ベース `appsettings.json` のみでエラーなく動作する（後方互換性100%）。
- 環境名は、OS環境変数（`DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`）を最優先とし、未設定時はコンパイル時シンボル（`#if DEBUG` なら `"Development"`、それ以外は `"Production"`）にフォールバックする。
- MSBuild（`TagBasedVideoManager.Renamer.fsproj`）において、すべての環境設定ファイル（`appsettings*.json`）が出力ディレクトリへ確実に配置されるようにする。
- テストコードから任意の環境名（`"Development"` / `"Production"` / 任意名）や特定ファイルを指定してマージ動作を検証可能なインターフェースを提供する。

**Non-Goals:**

- UIからの設定保存（`Settings.save`）時に環境別ファイルへ逆書き込みすること（保存先は既存通り実行ディレクトリの `appsettings.json` または AppData 領域）。
- Webサーバー側（`src/TagBasedVideoManager`）の設定構造の変更。

## Decisions

### 1. 設定ファイル群の構成と配置

- **配置先**: `src/TagBasedVideoManager.Renamer/`
  - `appsettings.json`: 共通のベース既定値
  - `appsettings.Development.json`: 開発環境用オーバーライド（例: `targetDirectory` に `"test/videos"`、開発用モデルなど）
  - `appsettings.Production.json`: 本番配布用オーバーライド（例: `targetDirectory` を空文字、本番推奨モデル、`apiKey` は null など）
- **MSBuild 出力制御（.fsproj）**:
  条件分岐コピーではなく、すべての `appsettings*.json` を出力ディレクトリに配置する（.NET 標準方式）。
  ```xml
  <ItemGroup>
    <Content Include="appsettings*.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  ```

### 2. 環境名（Environment）の解決順序

実行時の環境名は以下の優先順位で決定する:

1. 環境変数 `DOTNET_ENVIRONMENT`（最優先）
2. 環境変数 `ASPNETCORE_ENVIRONMENT`
3. コンパイル時シンボルによるフォールバック:
   ```fsharp
   let defaultEnvironmentName =
       let envDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
       let envAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
       if not (String.IsNullOrWhiteSpace(envDotnet)) then envDotnet.Trim()
       elif not (String.IsNullOrWhiteSpace(envAspnet)) then envAspnet.Trim()
       else
           #if DEBUG
           "Development"
           #else
           "Production"
           #endif
   ```

### 3. オーバーライドマージのメカニズム

Microsoft Learn の構成思想に則り、以下の順序で設定を重ね合わせる（マージ）:

1. **組み込み既定値**（`defaultSettings ()`）を初期値とする。
2. **ベース設定（`appsettings.json`）の読み込み**:
   指定された探索ディレクトリにおいて `appsettings.json` が存在すれば読み込み、設定値を適用する。
3. **環境別設定（`appsettings.{Environment}.json`）のオーバーライド**:
   探索ディレクトリにおいて `appsettings.{Environment}.json` が存在すれば読み込み、定義されている各キー（`targetDirectory`, `pathLengthThreshold`, `selectedModel`, `apiKey`, `rules`）を上書き（オーバーライド）する。環境別ファイルで未指定のキーはベース値が維持される。
4. **環境設定（`.env`）の適用**:
   `.env` に定義された値（`OPENROUTER_API_KEY`, `VIDEO_DIR` 等）を補完・適用する。

#### マージ処理のデータ表現

JSONのキー単位オーバーライドを型安全に実現するため、内部で `SettingsDto`（全フィールドが `option`）を用いてデシリアライズする。

```fsharp
[<CLIMutable>]
type SettingsDto = {
    TargetDirectory: string option
    PathLengthThreshold: Nullable<int>
    SelectedModel: string option
    ApiKey: string option
    Rules: NamingRule list option
}
```

ベース設定の DTO と環境別設定の DTO を結合し、環境側の `Some`（または非null）でベース側をオーバーライドした後に最終的な `RenamerSettings` を生成する。

### 4. テスト性の確保

- `loadConfigurationWithConfig (envNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option)`:
  環境名 `envNameOpt`（`Some "Development"` / `Some "Production"` 等）を直接渡せるようにし、テスト実行時の環境変数の汚染なしに両環境のマージ動作を決定論的に検証可能にする。
- 既存の `loadConfiguration jsonPathOpt envPathOpt` は、`loadConfigurationWithConfig (Some defaultEnvironmentName) jsonPathOpt envPathOpt` を呼び出し、完全な互換性を保つ。

## Risks / Trade-offs

- **[Risk]** 環境別JSONファイルが不正な構文だった場合にアプリが起動不能になるリスク。
  - **[Mitigation]** 環境別ファイルのロードでパースエラーが発生した場合、エラーログ（または Result エラー）として検出し、ベース `appsettings.json` の設定でフォールバック継続できる安全弁を設ける。
- **[Risk]** 開発者が `appsettings.json` と `appsettings.Development.json` のキー定義を不整合にするリスク。
  - **[Mitigation]** 単体テストにて、`appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` のすべてのファイルが正常にデシリアライズおよびマージ可能であることを自動検証する。
