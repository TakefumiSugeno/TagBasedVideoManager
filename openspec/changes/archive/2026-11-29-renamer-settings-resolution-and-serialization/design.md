# Design: AI File Renamer 設定読み込み・保存の解決統一とシリアライズ修正

- **Change Name**: `renamer-settings-resolution-and-serialization`
- **Scale Level**: `L2`
- **Related Issue**: [#41](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/41)

---

## 1. アーキテクチャ方針と責務分離

本修正はデスクトップアプリ `TagBasedVideoManager.Renamer` の設定永続化境界（`Settings.fs`）、Elmish 状態管理境界（`State.fs`）、UI 表示・イベントガード境界（`Views.fs`）、およびエラー案内文言（`OpenRouterClient.fs` L124, L350 の旧 `.env` 記述整理）を対象とし、Web アプリ（`src/TagBasedVideoManager/`）には影響を与えない。

### 1.1 変更コンポーネント責務

| モジュール                            | 責務                                                   | 主な変更点                                                                                                                                                                                                                                                                                                                                                                |
| :------------------------------------ | :----------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Settings.fs`                         | 設定解決・シリアライズ・マージ・検証・部分更新保存     | `StringOptionConverter` 追加、JSONC（コメント・末尾カンマ）許容、`Settings.load` の `parsePartial` + 既定値補完、API キー検証 (`validateApiKey`)、純粋解決関数 (`resolveConfigDirectoryWithOrigins`)・2 段階共通解決、`%APPDATA%` 廃止＆残存移行ログ、`PartialApiKey` 5 状態・`loadConfigurationWithMetadataWithContext` 追加、排他制御付き Patch Save (`savePatch`) 追加 |
| `State.fs`                            | Elmish 状態保持・設定再読込・保存ロールバック・AI 検証 | `Model.ApiKeySourcePath` / `AiPipelineRunId` 追加、`ReloadSettings`（同期ロード＋`TargetDirectory` 維持）追加、起動時二重ロード廃止・`Warnings` のバナー表示、保存失敗時のロールバック＆確認ダイアログ閉鎖、共通検証ガード (`validateApiKeyForAi`) と全パイプライン通知の `runId` ガード追加                                                                              |
| `Views.fs`                            | エラー視認性改善・再読込ボタン・イベントガード         | `errorBanner` への「🔄 設定を再読込」追加、コントロールパネル常設「🔄 設定を再読込」追加、モーダル開時背面バナー非表示＋`ruleManagerModal` 上部 `errorBanner` 配置、`ComboBox` 同値チェックガード (`shouldDispatchModelSelection`) 追加                                                                                                                                   |
| `OpenRouterClient.fs`                 | 401 認証エラーメッセージ整合                           | 401 エラーメッセージ内の古い `appsettings.json または .env の apiKey` 表記を `appsettings.{Environment}.json の apiKey` へ整合                                                                                                                                                                                                                                            |
| `TagBasedVideoManager.Renamer.fsproj` | アプリバージョン管理                                   | `<Version>` を `0.2.0` → `0.3.0` へ更新（Apply 時に実施）                                                                                                                                                                                                                                                                                                                 |

---

## 2. 詳細設計

### 2.1 `ApiKey` (`string option`) の JSON シリアライズ修正と JSONC / 最小 JSON 対応 (`Settings.fs`)

- **問題**: `System.Text.Json.JsonSerializer` はデフォルトで F# `option<'T>` を `{ "value": "..." }` オブジェクトとしてシリアライズする。また手編集での `//` コメント・末尾カンマ、0 バイト空ファイル、および最小 JSON 読込時の F# レコード必須フィールド欠損による `null` 混入を防ぐ必要がある。
- **解決策**:
  1. 読込および出力用オプションを整理する：
     - `jsonOptions`: `WriteIndented = true`, `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, `Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)`, `ReadCommentHandling = JsonCommentHandling.Skip`, `AllowTrailingCommas = true`, `Converters = [ JsonStringEnumConverter(); StringOptionConverter() ]`。
     - ※ `savePatch` による再シリアライズ時は `System.Text.Json.Nodes.JsonNode` の制約上、既存ファイル内のコメント・余分な末尾カンマは保持されないことを仕様・設計上の制約として明記する。
  2. `normalizeApiKeyString` と `StringOptionConverter`（`JsonConverter<string option>`）：
     - 共通正規化ヘルパー `normalizeApiKeyString (raw: string) : string option` を定義する（`raw` が `null` または空白のみなら `None`、それ以外は `raw.Trim().Trim('"', '\'')` を取り、空文字なら `None`、非空なら `Some clean`）。これにより `"\"\""` や `" '' "` も空文字扱いとなる。
     - **Write**: `None` → `writer.WriteNullValue()` / `Some s` → `writer.WriteStringValue(s)`（`Settings.save` 時に必ず素の JSON 文字列または `null` を出力）。
     - **Read**: `JsonTokenType.Null` → `None` / `JsonTokenType.String` → `normalizeApiKeyString (reader.GetString())` / その他トークン（`StartObject` 等）→ `reader.Skip()` でスキップし `[WARN]` ログ出力の上 `None`。
  3. **`Settings.load` の最小 JSON 安全補完と `parsePartial` 統一**:
     - `Settings.load (filePath: string)` はファイル存在確認後、0 バイト空文字列や構文エラーを含めて `parsePartial` で検証する（構文エラー・ルート非 `JsonObject` の場合は `Error (SettingsError ...)`、ファイル未存在・I/O エラー時は `Error (IoError ...)`）。
     - `parsePartial` が成功した場合、その `PartialSettings` を `mergePartialsChain [ partial ] (defaultSettings ())` に渡して未指定フィールド（`TargetDirectory`, `PathLengthThreshold`, `SelectedModel`, `ApiKey`, `Rules`）に組み込み既定値を安全補完し `Ok settings` を返す。これにより最小 JSON `{ "selectedModel": "m2" }` でも F# レコードに `null` が入る `NullReferenceException` を根絶する。また `StringOptionConverter.Read` も単体利用・JSON 変換契約として正常動作するよう実装・単体テスト対象として保持する。
     - `parsePartial` の `rules` パース（現行 `Settings.fs` L167-170）は `with ex ->` で無警告握りつぶしせず、`PartialSettings.ParseWarnings: string list` に `rules 配列の構文/型が不正なため無視しました: {ex.Message}` を記録し、`Warnings` へ伝播させる。

### 2.2 API キー検証とプレースホルダー判定の統一 (`Settings.fs`)

```fsharp
type ApiKeyValidationResult =
    | ValidApiKey of cleanKey: string
    | MissingApiKey
    | PlaceholderApiKey of rawValue: string
```

- **正規化**: `raw.Trim().Trim('"', '\'')`
- **プレースホルダー判定条件 (`isPlaceholderApiKey`)**:
  1. 空文字または空白のみ（`MissingApiKey` 扱い）
  2. 正規化後の文字列長が `10` 未満
  3. 大文字小文字非区別で既知ダミー値に一致: `"xxx"`, `"your-api-key"`, `"your_api_key"`, `"api_key"`, `"apikey"`, `"changeme"`, `"replace_me"`, `"placeholder"`, `"dummy"`, `"none"`, `"null"`
  4. `'['` で始まり `']'` で終わるテンプレート表記（例: `"[API_KEY]"`, `"[YOUR_OPENROUTER_API_KEY]"`）
  5. `'x'` / `'X'` のみで構成される文字列
  6. 連続ドット `"..."` を含む文字列（例: `"sk-or-v1-..."`）
  7. `"sk-or-v1-"`（大文字小文字非区別）で始まる場合、プレフィックス除去後の残余が `'x'`, `'X'`, `'0'`, `'.'`, `'*'`, `'_'`, `'-'` のみで構成されている文字列（例: `"sk-or-v1-xxxxxxxxxxxxxxxx"`）
- 上記に該当せず長さ 10 文字以上の場合は `ValidApiKey cleanKey` を返す。

### 2.3 読み込み・書き込み共通の 2 段階ディレクトリ解決と探索トレース (`Settings.fs`)

- **探索深度定義の統一**: 起点ディレクトリ自身（`step = 0`）から親方向へ最大 8 階層上（`step = 8`）まで、**計 9 ステップ（`step 0..8`）** を探索する。

```fsharp
/// 起点ディレクトリを明示注入可能な純粋ディレクトリ解決関数（設定ディレクトリ * 探索トレースログ）
let resolveConfigDirectoryWithOrigins
    (currentDir: string)
    (appBaseDir: string)
    (envName: string)
    : string * string list

/// 2段階共通ディレクトリ解決（baseDirOpt = Some dir 時は注入ディレクトリを即採用）
let resolveConfigDirectory (baseDirOpt: string option) (envName: string) : string

/// 保存先ファイルパス解決（%APPDATA% フォールバックなし）
let resolveSavePath (baseDirOpt: string option) : string
```

- **解決アルゴリズム (`resolveConfigDirectoryWithOrigins`)**:
  1. `currentDir` を起点に `step = 0..8` で親ディレクトリを遡り、`Path.Combine(ancestor.FullName, "src", "TagBasedVideoManager.Renamer")` に `appsettings.json` または `appsettings.{envName}.json` が存在するか判定する。見つかれば採用し、採用理由を `TraceLogs` に記録。
  2. `currentDir` 起点で見つからなければ、`appBaseDir` を起点に同様に `step = 0..8` で探索する（不採用理由「上限8階層到達 / ディレクトリ存在するが設定ファイル不在」も `TraceLogs` に記録）。
  3. いずれも見つからない場合は第 2 段階として `appBaseDir` を採用する。
- **`resolveSavePath`**: `Path.Combine(resolveConfigDirectory baseDirOpt envName, $"appsettings.{envName}.json")` を返す（`.perm_test_*.tmp` や `%APPDATA%` フォルダ作成は行わない）。

### 2.4 設定マージ・`PartialApiKey` 5 状態・出典パス保持・コンテキスト注入 (`Settings.fs`)

- **テスト・呼び出しコンテキスト型と返却メタデータ型**:
  ```fsharp
  type ConfigLoadContext = {
      CurrentDirectory: string
      AppBaseDirectory: string
      AppDataDirectory: string
      DotEnvPath: string option
  }

  type LoadedConfiguration = {
      Settings: RenamerSettings
      EnvironmentName: string
      SavePath: string
      ApiKeySourcePath: string option
      LoadedFiles: string list
      Warnings: string list
      TraceLogs: string list
  }
  ```
- **5 状態の `PartialApiKey`**:
  ```fsharp
  type private PartialApiKey =
      | ApiKeyOmitted
      | ApiKeyExplicitNull
      | ApiKeyEmptyString
      | ApiKeyValue of string
      | ApiKeyInvalidFormat of string
  ```
- **`parsePartial` での `apiKey` 分類**:
  - `JsonValueKind.Null` → `ApiKeyExplicitNull`
  - `JsonValueKind.String` → `normalizeApiKeyString (e.GetString())` が `None` なら `ApiKeyEmptyString`（`"\"\""` や `" '' "` も含む）、`Some clean` なら `ApiKeyValue clean`
  - その他トークン型 → `ApiKeyInvalidFormat (e.ValueKind.ToString())`
- **`mergePartialsChain` の `ApiKey` マージ規則（実環境 `appsettings.json` の `"apiKey": null` 誤案内防止含む）**:
  - 各 `PartialSettings` に `IsBaseLayer: bool`（`appsettings.json` なら `true`、環境別オーバーライド層なら `false`）を持たせる。
  1. `ApiKeyValue upperKey`:
     - `ValidApiKey cleanKey` → 上位採用（`ApiKey = Some cleanKey`, `ApiKeySourcePath = Some upper.SourcePath`）。
     - `PlaceholderApiKey _` → 下位に有効キーがあれば下位の有効キーと出典パスを維持し `Warnings` 追加。下位にも有効キーがなければプレースホルダー値と `Some upper.SourcePath` を保持。
  2. `ApiKeyExplicitNull`:
     - **環境別層（`IsBaseLayer = false`）**: 意図的無効化として `ApiKey = None`, `ApiKeySourcePath = Some upper.SourcePath`（下位に有効キーがあっても上書き）。
     - **ベース層（`IsBaseLayer = true`）**: リポジトリのクリーンテンプレート `appsettings.json`（`"apiKey": null`）に該当するため、初期未設定（`ApiKey = None`, `ApiKeySourcePath = None`）とする。
  3. `ApiKeyEmptyString`:
     - 下位に有効キーがある場合は**上書きせず下位の有効キーと出典パスを維持**し、`Warnings` へ警告追加。下位に有効キーがない場合は、`IsBaseLayer = true` なら `ApiKey = None, ApiKeySourcePath = None`、`IsBaseLayer = false` なら `ApiKey = None, ApiKeySourcePath = Some upper.SourcePath` とする（`DT-MERGE-08` と一致）。
  4. `ApiKeyInvalidFormat kind`:
     - 下位に有効キー（またはプレースホルダー）があれば維持し、ない場合は `None`, `ApiKeySourcePath = Some upper.SourcePath` とした上で、`Warnings` へ「設定ファイル ({upper.SourcePath}) の apiKey が不正な形式 ({kind}) になっています。文字列形式 `"apiKey": "sk-or-v1-..."` へ修正してください」を記録。
  5. `ApiKeyOmitted`: 下位レイヤーの値を継承。
- **旧 `%APPDATA%` 残存移行ログ**: 通常モード時（`jsonPathOpt = None && explicitDirOpt = None`）、`ctx.AppDataDirectory` 配下に `appsettings.json` または `appsettings.{envName}.json` が存在する場合、**内容は読み込まず**、`eprintfn` および `TraceLogs` へ「`[WARN] 旧設定ディレクトリ ({ctx.AppDataDirectory}) に設定ファイルが残存していますが、参照は廃止されました。必要な設定は {savePath} へ移行してください。`」を出力する。
- **公開 API**:
  - `loadConfigurationWithMetadataWithContext (ctx: ConfigLoadContext) (configNameOpt: string option) (jsonPathOpt: string option) (explicitDirOpt: string option) : LoadedConfiguration` を実装し、並列テスト時は `ctx` に一時ディレクトリ群を注入して本番と同一の 2 段階探索・`.env` / `%APPDATA%` 検知経路を検証する。
  - `loadConfigurationWithMetadata (configNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option) : LoadedConfiguration` は既定の `ConfigLoadContext`（`envPathOpt` 指定時はテスト互換のため `explicitDirOpt` へディレクトリ変換）で上記を呼ぶ。
  - `loadConfigurationWithConfig` / `loadConfiguration` は後方互換のため `.Settings` を返す。

### 2.5 GUI 設定保存の部分更新（Patch Save）と排他制御・段階別診断 (`Settings.fs`)

- **パス単位のプロセス内排他ロック**: `ConcurrentDictionary<string, obj>(StringComparer.OrdinalIgnoreCase)`（キー: `Path.GetFullPath(filePath)`）で Read-Modify-Write のロストアップデートを防止。
- **処理フロー (`savePatch (filePath: string) (patch: SettingsPatch) : Result<unit, RenamerError>`)**:
  1. `SelectedModel = None && Rules = None` の場合は即 `Ok ()`（ロック・書込なし）。
  2. ロック内で実行：
     - **段階 `ReadExisting`**: 既存ファイルがあれば最大 5 回 × 10ms リトライ付きで `File.ReadAllText`（0 バイト空ファイルの場合は空として `ParseJson` で不正形式扱いし保護）。
     - **段階 `ParseJson`**: `JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)` 付き `JsonNode.Parse`。構文エラー（`JsonException` の `LineNumber`, `BytePositionInLine` 取得）またはルートが非 `JsonObject` の場合は**一切書き込まず `Error (SettingsError $"設定ファイル '{filePath}' のJSON形式が不正なため部分保存を中止しました (段階: ParseJson, Line {line}, Pos {pos}): ...")` を返す**。
     - 未存在時は空の `JsonObject()` を生成（未変更キーに `null` を出さない）。
     - パッチ適用：`SelectedModel` / `Rules` の PascalCase 重複を除去し camelCase キーを更新。
     - **段階 `CreateDirectory` / `WriteTempFile` / `AtomicMove`**: 親ディレクトリ作成 → `root.ToJsonString(jsonOptions)`（`WriteIndented = true` かつ `JavaScriptEncoder.Create(UnicodeRanges.All)` により日本語非エスケープ維持）を GUID 一意 `.tmp` へ書込 → 最大 5 回 × 10ms（計最大 50ms）リトライ付き `File.Move(tmpPath, filePath, overwrite = true)`。
     - 失敗時は `.tmp` を削除し、失敗段階・試行回数・保存先フルパス・例外理由を含む `Error (IoError ...)` を返す（`%APPDATA%` フォールバック禁止）。

### 2.6 `State.fs` / `Views.fs` の状態統合・再読込導線・レース防止・ロールバック

1. **`Model`・`Msg` およびコンテキスト注入境界の拡張 (`State.fs`)**:
   - `Model` に `ApiKeySourcePath: string option` と `AiPipelineRunId: Guid option` を追加。
   - `Msg` に `ReloadSettings` および `SettingsLoadedWithMeta of Result<LoadedConfiguration, RenamerError>` を追加。
   - パイプライン進捗通知を `runId: Guid` 付きに統一：`CandidateAiProcessing of runId: Guid * fullPath: string`, `CandidateAiProposed of runId: Guid * RenameProposal`, `AllAiProposalsCompleted of runId: Guid`。
   - **コンテキスト注入関数**: `State.initWithContext (ctxOpt: ConfigLoadContext option) : Model * Cmd<Msg>` および `State.updateWithContext (ctxOpt: ConfigLoadContext option) (msg: Msg) (model: Model) : Model * Cmd<Msg>` を導入する。既存の `State.init ()` と `State.update` は `ctxOpt = None` を渡す本体共有ラッパーとして実装し、Elmish 本体とテスト (`ST-INIT-*`, `ST-RELOAD-*`, `E2E-01`, `E2E-03`) が完全に同一の更新ロジックを通るようにする。
2. **`initWithContext` の二重ロード解消と起動時警告バナー表示**:
   - `initWithContext` は `ctxOpt` に応じて同期で `Settings.loadConfigurationWithMetadataWithContext`（`None` 時は `loadConfigurationWithMetadata None None None`）を呼び出し、`Settings`, `ActiveEnvironment`, `SettingsSavePath`, `ApiKeySourcePath` を確定する（冗長な起動時 `loadSettingsCmd` は廃止）。
   - `meta.Warnings` が非空の場合、`eprintfn` 出力に加えて `String.Join("\n", meta.Warnings)` を `Model.ErrorMessage` に設定し起動直後からバナー表示する。
3. **`ReloadSettings`（同期再読込・選択中 `TargetDirectory` 維持・ビジー排他）**:
   - `IsScanning || IsRenaming` 中は `ReloadSettings` を無視する（`Views.fs` でも同条件でボタン非活性）。
   - 実行時は `ctxOpt` に応じて同期で `Settings.loadConfigurationWithMetadataWithContext`（または `loadConfigurationWithMetadata None None None`）を呼び出し、1 回の結果から `ActiveEnvironment`, `SettingsSavePath`, `ApiKeySourcePath` を一括更新する：
     - `TargetDirectory`: `loaded.Settings.TargetDirectory` が非空白なら採用、空白なら現在の `model.Settings.TargetDirectory` を保持する（最小 JSON `{ "apiKey": "..." }` 作成後の再読込で画面の選択フォルダが消えないようにする）。
     - `SelectedRuleId`: 新 `Rules` に現 `model.SelectedRuleId` があれば維持、なければ新先頭ルール ID。
     - `ErrorMessage`: `loaded.Warnings` が非空なら警告表示、空なら `Some $"💡 設定ファイルを再読込しました ({Path.GetFileName(loaded.SavePath)})"` を表示して即座に次の操作（スキャン / 再試行）へ進める。
4. **AI パイプライン CTS 管理・`runId` ガード・ゾンビ状態防止 (`ExecuteScanAndPropose` / `ScanCompleted` / `RetryCandidateAiProposal`)**:
   - `ExecuteScanAndPropose`, `ScanCompleted`（有効キー時）, `RetryCandidateAiProposal`（有効キー時）のすべてで共通ヘルパーを使い、先行 `model.AiCancellationCts` を `Cancel()` & `Dispose()` した上で新 `cts` と新 `runId = Guid.NewGuid()` を発行・格納する。
   - **スピナーゾンビ化の構造的防止**: `ExecuteScanAndPropose` 実行時（`TargetDirectory` 空白による早期リターンおよびスキャン開始時の双方）、ならびに `ScanCompleted` の「対象 0 件 (`Ok []`)」「スキャンエラー (`Error _`)」「API キー検証失敗 (`MissingApiKey` / `PlaceholderApiKey`)」の**全非 AI 分岐で `IsRequestingAi = false`、`AiPipelineRunId = None`、`AiCancellationCts = None` を明示リセット**する。これにより `IsRequestingAi = true` 中に再スキャンして 0 件／エラー／キー不備へ遷移しても、無視される旧 `AllAiProposalsCompleted oldRunId` 待ちの永久ロックが起きない。
   - `CandidateAiProcessing (runId, _)`, `CandidateAiProposed (runId, _)`, `AllAiProposalsCompleted runId` すべてで `model.AiPipelineRunId = Some runId` をチェックし、不一致（古いパイプラインの遅延通知）は無視する。
5. **保存失敗時のロールバック・ダイアログ閉鎖・モーダル内バナー表示・クロージャ安全ガード (`State.fs` / `Views.fs`)**:
   - `saveSettingsPatchWithFeedback (savePath: string) (patch: SettingsPatch)` は空でない `savePath`（テスト注入時は一時パス、未指定時は `Settings.resolveSavePath None`）へ `Settings.savePatch` を実行する。
   - `ModelSelected`, `SaveEditingRule`, `SaveRule`, `DeleteRule`, `MoveRuleOrder` において `savePatch` が `Error errMsg` を返した場合：
     - **`model.Settings`（`SelectedModel` / `Rules`）を更新せずロールバック**し、
     - `SaveEditingRule` 失敗時は `SelectedRuleId` も変更せず `EditingRule` をそのまま保持、
     - `DeleteRule` 失敗時は **`ConfirmDialog = None` に閉鎖**し、`EditingRule` を保持、
     - `ErrorMessage = Some errMsg` を設定する。
   - **`Views.fs` の描画整合**:
     - メイン領域 `DockPanel` 冒頭は `if not model.IsRuleManagerOpen then errorBanner model dispatch` とし、モーダル開時の背面バナー重複・テスト誤認を防ぐ。
     - `ruleManagerModal` 上部（ヘッダー直下 `Dock.Top`）に同一の `errorBanner model dispatch` を配置する。
     - `errorBanner` の右側アクション群は `🔄 設定を再読込`（`ReloadSettings`）・`📋 コピー`・`✕`（`DismissError`）の 3 ボタンとし、さらにコントロールパネルにも常設の `🔄 設定を再読込` ボタン（`IsScanning || IsRenaming` 中は非活性）を配置する（`mockup.html` と完全一致）。
     - モデル選択 `ComboBox` は `Views.shouldDispatchModelSelection (currentModel: string) (selectedItemOpt: string option) : string option` を定義し、`ComboBox.onSelectedIndexChanged` 内で**同一クロージャ内の `availableModels |> List.tryItem idx` を `shouldDispatchModelSelection model.Settings.SelectedModel` へ渡す**ことで、FuncUI 仮想 DOM 差分更新中の古いクロージャ比較や無関係イベントによる意図しない `ModelSelected` 発火を防止する。
6. **API キー検証ガードの統一 (`validateApiKeyForAi`)**:
   - `ScanCompleted` と `RetryCandidateAiProposal` で同一ヘルパーを呼び出す。
   - **`MissingApiKey` 時**: 案内先パスはベース `appsettings.json` ではなく**常に `model.SettingsSavePath`**（環境別設定ファイル）を明示し、最小 JSON 書式例 `{ "apiKey": "sk-or-v1-..." }` と「設定ファイルを修正後、『🔄 設定を再読込』をクリックしてください。」を案内する。
   - **`PlaceholderApiKey rawVal` 時**: 出典ファイルパス（`model.ApiKeySourcePath |> Option.defaultValue model.SettingsSavePath`）を明示し、同様に有効キー設定＋「🔄 設定を再読込」を案内する。

---

## 3. UI モックアップ (`mockup.html`)

`openspec/changes/renamer-settings-resolution-and-serialization/mockup.html` を Single Source of Design とし、アプリウィンドウ内 UI（コントロールパネル常設 `🔄 設定を再読込`、`errorBanner` の 3 ボタン統一、モーダル開時背面バナー非表示、モーダル上部 `errorBanner`）を実装と 1:1 一致させる。モック上の状態切替シミュレーションボタン（権限回復再保存など）はアプリ枠外の最上部コントロールバーにのみ配置する。

---

## 4. 可観測性・テスト隔離・ドキュメント整合

- **可観測性**: 起動時に採用ディレクトリ・探索トレース（`TraceLogs`）・読込成功ファイル・`apiKey` 出典パス（キー値はログ・UI 非出力）・`%APPDATA%` 残存移行案内を出力し、`Warnings` は画面バナーへ表示する。`savePatch` 失敗時は段階・試行回数・行/列位置・保存先パスを通知する。
- **テスト隔離（実ファイル保護）**: Task 1.1 の段階から実ファイル保護ガードおよび一時 `SettingsSavePath` 注入を行い、Task 2.1 の `Settings.fs` 実装時には `State.fs` の `saveSettingsWithFeedback` も空でない `model.SettingsSavePath` を使用する最小安全切替を併せて行うことで、全フェーズを通じて開発者の実 `appsettings.Development.json` を完全に保護する。
- **ドキュメント・案内整合**: `README.md` の `%APPDATA%` 記述削除・キー名統一・最小 JSON 例・2 段階解決/Patch Save 記載、`OpenRouterClient.fs` の 401 案内文言整理を行う。
