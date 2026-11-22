namespace TagBasedVideoManager.Renamer

open System
open System.Collections.Concurrent
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open System.Text.Unicode
open System.Threading
open TagBasedVideoManager.Renamer

/// <summary>APIキーの検証結果</summary>
type ApiKeyValidationResult =
    | ValidApiKey of cleanKey: string
    | MissingApiKey
    | PlaceholderApiKey of rawValue: string

/// <summary>設定ロード処理へ注入する実行コンテキスト</summary>
type ConfigLoadContext = {
    CurrentDirectory: string
    AppBaseDirectory: string
    AppDataDirectory: string
    DotEnvPath: string option
}

/// <summary>出典パスや警告情報を含む設定ロード結果メタデータ</summary>
type LoadedConfiguration = {
    Settings: RenamerSettings
    EnvironmentName: string
    SavePath: string
    ApiKeySourcePath: string option
    LoadedFiles: string list
    Warnings: string list
    TraceLogs: string list
}

/// <summary>GUI 設定変更時の部分更新パッチ</summary>
type SettingsPatch = {
    SelectedModel: string option
    Rules: NamingRule list option
}

/// <summary>APIキー文字列の正規化をコンバータと設定ロジック間で共有するための内部ヘルパー</summary>
module private ApiKeyNormalizer =
    /// <summary>APIキー文字列から前後の空白および引用符を除去し、空または空白のみの場合は None を返す</summary>
    let normalize (raw: string) : string option =
        if String.IsNullOrWhiteSpace(raw) then
            None
        else
            let cleaned = raw.Trim().Trim('"', '\'').Trim()
            if String.IsNullOrWhiteSpace(cleaned) then None else Some cleaned

/// <summary>
/// string option を素の JSON 文字列または null として読み書きする JSON コンバータ。
/// System.Text.Json の既定動作による { "value": ... } ラッパーオブジェクト化を防ぐために使用する。
/// </summary>
type StringOptionConverter() =
    inherit JsonConverter<string option>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, _options: JsonSerializerOptions) : string option =
        match reader.TokenType with
        | JsonTokenType.Null -> None
        | JsonTokenType.String -> ApiKeyNormalizer.normalize (reader.GetString())
        | token ->
            // 旧バグ形式のオブジェクトや不正な型のトークンで起動クラッシュさせないため、ストリーム位置を壊さず安全にスキップする
            eprintfn $"[WARN] string option に対して不正な JSON トークン ({token}) が検出されたためスキップします。"
            reader.Skip()
            None

    override _.Write(writer: Utf8JsonWriter, value: string option, _options: JsonSerializerOptions) : unit =
        match value with
        | Some s -> writer.WriteStringValue(s)
        | None -> writer.WriteNullValue()

/// <summary>AI File Renamer の設定読み込み・解決・検証・保存を担当するモジュール</summary>
module Settings =

    /// <summary>最小 API キー文字数（10文字未満はテスト用や不正入力とみなす）</summary>
    [<Literal>]
    let private MinApiKeyLength = 10

    /// <summary>親ディレクトリ探索の最大遡り階層数（step 0..8 の計9ステップ）</summary>
    [<Literal>]
    let private MaxParentSearchDepth = 8

    /// <summary>既知のプレースホルダー文字列（小文字比較用）</summary>
    let private knownPlaceholderValues =
        set [
            "xxx"
            "your-api-key"
            "your_api_key"
            "api_key"
            "apikey"
            "changeme"
            "replace_me"
            "placeholder"
            "dummy"
            "none"
            "null"
        ]

    /// <summary>ファイルパス単位の部分保存排他ロック（Read-Modify-Write のロストアップデート防止用）</summary>
    let private patchFileLocks = ConcurrentDictionary<string, obj>(StringComparer.OrdinalIgnoreCase)

    /// <summary>BOM なし UTF-8 エンコーディング</summary>
    let private utf8NoBom = UTF8Encoding(false)

    let private defaultRules = [
        {
            Id = "rule-date-action"
            Name = "日付_撮影地_行動 (推奨)"
            Pattern = "{Date}_{Location}_{Activity}.mp4"
            PromptInstruction = "ファイル名から撮影日(YYYYMMDD)、撮影場所、主な行動/内容を抽出しアンダースコア繋ぎで命名してください。情報が不足している場合は親ディレクトリ名や更新日時から推測してください。"
            Order = 0
            EnableWebSearch = false
        }
        {
            Id = "rule-compact"
            Name = "短縮 (タイトルのみ)"
            Pattern = "{ShortTitle}.mp4"
            PromptInstruction = "動画の内容を表す最も重要で短いキーワード（20文字以内）で命名してください。"
            Order = 1
            EnableWebSearch = false
        }
    ]

    let private jsonOptions =
        let options =
            JsonSerializerOptions(
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            )
        options.Encoder <- JavaScriptEncoder.Create(UnicodeRanges.All)
        options.Converters.Add(JsonStringEnumConverter())
        options.Converters.Add(StringOptionConverter())
        options

    let private jsonDocumentOptions =
        JsonDocumentOptions(
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        )

    /// <summary>既定の設定値を生成する（OS環境変数は参照しない）</summary>
    let defaultSettings () : RenamerSettings =
        {
            TargetDirectory = ""
            PathLengthThreshold = 240
            SelectedModel = "meta-llama/llama-3.3-70b-instruct:free"
            ApiKey = None
            Rules = defaultRules
        }

    /// <summary>APIキー文字列の前後空白・引用符を正規化する</summary>
    let normalizeApiKeyString (raw: string) : string option =
        ApiKeyNormalizer.normalize raw

    /// <summary>正規化済み API キー文字列がプレースホルダーやダミー値であるかを判定する</summary>
    let private isPlaceholderApiKey (cleanKey: string) : bool =
        if String.IsNullOrWhiteSpace(cleanKey) || cleanKey.Length < MinApiKeyLength then
            true
        else
            let lower = cleanKey.ToLowerInvariant()
            let isKnownDummy = knownPlaceholderValues.Contains(lower)
            let isBracketTemplate = cleanKey.StartsWith('[') && cleanKey.EndsWith(']')
            let isOnlyX = cleanKey |> Seq.forall (fun c -> c = 'x' || c = 'X')
            let hasRepeatedDots = cleanKey.Contains("...")
            let isSkOrDummy =
                if lower.StartsWith("sk-or-v1-", StringComparison.Ordinal) then
                    let rest = cleanKey.Substring("sk-or-v1-".Length)
                    rest |> Seq.forall (fun c ->
                        c = 'x' || c = 'X' || c = '0' || c = '.' || c = '*' || c = '_' || c = '-')
                else
                    false
            isKnownDummy || isBracketTemplate || isOnlyX || hasRepeatedDots || isSkOrDummy

    /// <summary>APIキーの有効性・未設定・プレースホルダーを判定する</summary>
    let validateApiKey (apiKeyOpt: string option) : ApiKeyValidationResult =
        match apiKeyOpt |> Option.bind normalizeApiKeyString with
        | None -> MissingApiKey
        | Some cleanKey ->
            if isPlaceholderApiKey cleanKey then
                PlaceholderApiKey cleanKey
            else
                ValidApiKey cleanKey

    /// <summary>一時ファイルを安全に削除する（クリーンアップ失敗は警告ログのみ出力し呼び出し元エラーを隠蔽しない）</summary>
    let private tryDeleteTempFile (tmpPath: string) : unit =
        try
            if File.Exists(tmpPath) then File.Delete(tmpPath)
        with ex ->
            eprintfn "[WARN] 一時設定ファイルの削除に失敗しました (%s): %s" tmpPath ex.Message

    /// <summary>一時的なファイルロック競合（IOException / UnauthorizedAccessException）に対してリトライ付きで I/O 操作を実行する</summary>
    let private retryIo (maxAttempts: int) (retryDelayMs: int) (action: unit -> 'T) : Result<'T, exn> =
        let totalAttempts = max 1 maxAttempts
        let rec loop attempt =
            try
                Ok (action ())
            with
            | (:? IOException | :? UnauthorizedAccessException) when attempt < totalAttempts ->
                Thread.Sleep(retryDelayMs)
                loop (attempt + 1)
            | ex ->
                Error ex
        loop 1

    /// <summary>ディレクトリ作成・一時ファイル書込・リトライ付きアトミック移動を一貫して実行する</summary>
    let private writeAtomicWithRetry
        (moveAttempts: int)
        (moveDelayMs: int)
        (fullPath: string)
        (content: string)
        : Result<unit, RenamerError> =

        let tmpPath = $"{fullPath}.{Guid.NewGuid():N}.tmp"
        let dir = Path.GetDirectoryName(fullPath)
        let mkIoErr stage attempts (ex: exn) =
            Error (IoError ($"設定ファイルの保存中にエラーが発生しました '{fullPath}' (段階: {stage}, 試行回数: {attempts}回): {ex.Message}", Some ex))

        let dirRes =
            try
                if not (String.IsNullOrEmpty(dir)) && not (Directory.Exists(dir)) then
                    Directory.CreateDirectory(dir) |> ignore
                Ok ()
            with ex ->
                mkIoErr "CreateDirectory" 1 ex

        dirRes
        |> Result.bind (fun () ->
            try
                File.WriteAllText(tmpPath, content, utf8NoBom)
                Ok ()
            with ex ->
                tryDeleteTempFile tmpPath
                mkIoErr "WriteTempFile" 1 ex
        )
        |> Result.bind (fun () ->
            match retryIo moveAttempts moveDelayMs (fun () -> File.Move(tmpPath, fullPath, overwrite = true)) with
            | Ok () -> Ok ()
            | Error ex ->
                tryDeleteTempFile tmpPath
                mkIoErr "AtomicMove" (max 1 moveAttempts) ex
        )

    /// <summary>マージ時に上位と下位の意図を区別するための API キー状態</summary>
    type private PartialApiKey =
        | ApiKeyOmitted
        | ApiKeyExplicitNull
        | ApiKeyEmptyString
        | ApiKeyValue of string
        | ApiKeyInvalidFormat of string

    /// <summary>部分的な設定値を保持する内部レコード</summary>
    type private PartialSettings = {
        SourcePath: string
        IsBaseLayer: bool
        TargetDirectory: string option
        PathLengthThreshold: int option
        SelectedModel: string option
        ApiKey: PartialApiKey
        Rules: NamingRule list option
        ParseWarnings: string list
    }

    /// <summary>指定されたJSONファイルから部分設定を読み込む（JSONC許容・I/Oリトライ・rules要素厳密検証・0バイト保護付き）</summary>
    let private parsePartial (isBaseLayer: bool) (filePath: string) : Result<PartialSettings, string> =
        try
            if String.IsNullOrWhiteSpace(filePath) || not (File.Exists(filePath)) then
                Error $"File not found: {filePath}"
            else
                match retryIo 3 10 (fun () -> File.ReadAllText(filePath, Encoding.UTF8)) with
                | Error ex ->
                    Error ex.Message
                | Ok text when String.IsNullOrWhiteSpace(text) ->
                    Error "設定ファイルが空（0バイトまたは空白のみ）です。"
                | Ok text ->
                    use doc = JsonDocument.Parse(text, jsonDocumentOptions)
                    let root = doc.RootElement
                    if root.ValueKind <> JsonValueKind.Object then
                        Error "Root is not a JSON object"
                    else
                        let getProp (name: string) =
                            let mutable elem = Unchecked.defaultof<JsonElement>
                            if root.TryGetProperty(name, &elem) then Some elem
                            else
                                let pascal =
                                    if String.IsNullOrEmpty(name) then name
                                    else Char.ToUpperInvariant(name.[0]).ToString() + name.Substring(1)
                                if root.TryGetProperty(pascal, &elem) then Some elem
                                else None

                        let getStringProp (name: string) =
                            getProp name
                            |> Option.bind (fun e ->
                                if e.ValueKind = JsonValueKind.String then Some (e.GetString())
                                else None
                            )

                        let threshold =
                            getProp "pathLengthThreshold"
                            |> Option.bind (fun e ->
                                match e.ValueKind with
                                | JsonValueKind.Number ->
                                    let mutable v = 0
                                    if e.TryGetInt32(&v) then Some v else None
                                | _ -> None
                            )

                        let apiKey =
                            match getProp "apiKey" with
                            | None -> ApiKeyOmitted
                            | Some e ->
                                match e.ValueKind with
                                | JsonValueKind.Null -> ApiKeyExplicitNull
                                | JsonValueKind.String ->
                                    match normalizeApiKeyString (e.GetString()) with
                                    | None -> ApiKeyEmptyString
                                    | Some clean -> ApiKeyValue clean
                                | kind -> ApiKeyInvalidFormat (kind.ToString())

                        let rules, parseWarnings =
                            match getProp "rules" with
                            | None -> None, []
                            | Some e when e.ValueKind <> JsonValueKind.Array ->
                                None, [ $"設定ファイル ({filePath}) の rules は配列形式である必要があります（検出された型: {e.ValueKind}）。既定値を維持します。" ]
                            | Some e ->
                                // null 要素や非オブジェクト要素・必須文字列欠損レコードによる後続クラッシュを防ぐため厳密に検証する
                                let hasNonObjectItem =
                                    e.EnumerateArray() |> Seq.exists (fun item -> item.ValueKind <> JsonValueKind.Object)
                                if hasNonObjectItem then
                                    None, [ $"設定ファイル ({filePath}) の rules 配列にオブジェクト以外の要素（null 等）が含まれているため無視しました。" ]
                                else
                                    try
                                        let parsedRules = JsonSerializer.Deserialize<NamingRule list>(e.GetRawText(), jsonOptions)
                                        let isInvalidRule (r: NamingRule) =
                                            isNull (box r)
                                            || isNull r.Id
                                            || isNull r.Name
                                            || isNull r.Pattern
                                            || isNull r.PromptInstruction
                                        if isNull (box parsedRules) || (parsedRules |> List.exists isInvalidRule) then
                                            None, [ $"設定ファイル ({filePath}) の rules 配列に必須フィールド (id, name, pattern, promptInstruction) が欠損した要素があるため無視しました。" ]
                                        else
                                            Some parsedRules, []
                                    with ex ->
                                        None, [ $"設定ファイル ({filePath}) の rules 配列の構文/型が不正なため無視しました: {ex.Message}" ]

                        Ok {
                            SourcePath = filePath
                            IsBaseLayer = isBaseLayer
                            TargetDirectory = getStringProp "targetDirectory"
                            PathLengthThreshold = threshold
                            SelectedModel = getStringProp "selectedModel"
                            ApiKey = apiKey
                            Rules = rules
                            ParseWarnings = parseWarnings
                        }
        with ex ->
            Error ex.Message

    /// <summary>複数レイヤーの部分設定を下位から順にマージし、最終設定・APIキー出典パス・警告リストを返す</summary>
    let private mergePartialsChain
        (partials: PartialSettings list)
        (fallback: RenamerSettings)
        : RenamerSettings * string option * string list =

        let revPartials = List.rev partials
        let targetDir = revPartials |> List.tryPick (fun p -> p.TargetDirectory) |> Option.defaultValue fallback.TargetDirectory
        let threshold = revPartials |> List.tryPick (fun p -> p.PathLengthThreshold) |> Option.defaultValue fallback.PathLengthThreshold
        let model = revPartials |> List.tryPick (fun p -> p.SelectedModel) |> Option.defaultValue fallback.SelectedModel
        let rules =
            revPartials
            |> List.tryPick (fun p -> p.Rules)
            |> Option.map (List.sortBy (fun item -> item.Order))
            |> Option.defaultValue fallback.Rules

        // 下位から上位へ順に API キー状態を畳み込むことで、プレースホルダーや空文字による下位有効キーの上書きを防ぐ
        let mergeApiKeyStep (curKey: string option, curSource: string option, warningsRev: string list) (p: PartialSettings) =
            let warningsWithParse = (List.rev p.ParseWarnings) @ warningsRev
            let hasValidLower =
                match validateApiKey curKey with
                | ValidApiKey _ -> true
                | _ -> false

            let sourceDesc = curSource |> Option.defaultValue ""
            let preserveWithReason (reason: string) =
                let warn = $"設定ファイル ({p.SourcePath}) の apiKey が{reason}のため、下位の有効な apiKey ({sourceDesc}) を維持しました。"
                (curKey, curSource, warn :: warningsWithParse)

            let fallbackEmptyOrNull () =
                if p.IsBaseLayer then (None, None, warningsWithParse)
                else (None, Some p.SourcePath, warningsWithParse)

            match p.ApiKey with
            | ApiKeyOmitted ->
                (curKey, curSource, warningsWithParse)
            | ApiKeyExplicitNull ->
                // リポジトリ既定の appsettings.json テンプレートの "apiKey": null は初期未設定扱い（誤案内防止）、環境別は意図的無効化
                if p.IsBaseLayer then (None, None, warningsWithParse)
                else (None, Some p.SourcePath, warningsWithParse)
            | ApiKeyEmptyString ->
                if hasValidLower then preserveWithReason "空文字"
                else fallbackEmptyOrNull ()
            | ApiKeyInvalidFormat kind ->
                let warn =
                    $"""設定ファイル ({p.SourcePath}) の apiKey が不正な形式 ({kind}) になっています。文字列形式 "apiKey": "sk-or-v1-..." へ修正してください。"""
                if curKey.IsSome then (curKey, curSource, warn :: warningsWithParse)
                else (None, Some p.SourcePath, warn :: warningsWithParse)
            | ApiKeyValue upperKey ->
                // parsePartial で正規化済みの非空文字列のため、プレースホルダー判定のみを行う
                if isPlaceholderApiKey upperKey then
                    if hasValidLower then preserveWithReason "プレースホルダー値"
                    else (Some upperKey, Some p.SourcePath, warningsWithParse)
                else
                    (Some upperKey, Some p.SourcePath, warningsWithParse)

        let finalKey, finalSource, warningsRev =
            partials |> List.fold mergeApiKeyStep (fallback.ApiKey, None, [])

        {
            TargetDirectory = targetDir
            PathLengthThreshold = threshold
            SelectedModel = model
            ApiKey = finalKey
            Rules = rules
        }, finalSource, List.rev warningsRev

    /// <summary>指定パスから設定ファイルを読み込む（最小JSONは既定値補完し、0バイト/不正JSONは SettingsError を返す）</summary>
    let load (filePath: string) : Result<RenamerSettings, RenamerError> =
        try
            if String.IsNullOrWhiteSpace(filePath) || not (File.Exists(filePath)) then
                Error (IoError ($"設定ファイルが見つかりません: {filePath}", None))
            else
                match parsePartial true filePath with
                | Ok partial ->
                    let settings, _, warnings = mergePartialsChain [ partial ] (defaultSettings ())
                    for w in warnings do
                        eprintfn $"[WARN] {w}"
                    Ok settings
                | Error errMsg ->
                    Error (SettingsError ($"設定ファイルのJSONパースに失敗しました: {errMsg}"))
        with ex ->
            Error (IoError ($"設定ファイルの読み込み中にエラーが発生しました: {ex.Message}", Some ex))

    /// <summary>指定パスへ設定ファイルをアトミックに保存する（GUID一意一時ファイル経由）</summary>
    let save (filePath: string) (settings: RenamerSettings) : Result<unit, RenamerError> =
        if String.IsNullOrWhiteSpace(filePath) then
            Error (IoError ("設定ファイルの保存先パスが空です。", None))
        else
            try
                let fullPath = Path.GetFullPath(filePath)
                let json = JsonSerializer.Serialize(settings, jsonOptions)
                writeAtomicWithRetry 11 25 fullPath json
            with ex ->
                Error (IoError ($"設定ファイルの保存中にエラーが発生しました '{filePath}': {ex.Message}", Some ex))

    /// <summary>環境名文字列をサニタイズし、パストラバーサルや不正文字を含む場合は None を返す</summary>
    let private sanitizeEnvironmentName (raw: string) : string option =
        if String.IsNullOrWhiteSpace(raw) then
            None
        else
            let trimmed = raw.Trim()
            let invalidChars = Path.GetInvalidFileNameChars()
            let hasForbiddenChar =
                trimmed.Contains("/")
                || trimmed.Contains("\\")
                || trimmed.Contains("..")
                || trimmed.IndexOfAny(invalidChars) >= 0
                || trimmed.IndexOfAny([| '*'; '?'; ':'; '<'; '>'; '|' |]) >= 0
            if hasForbiddenChar then
                eprintfn $"[WARN] 不正な環境名 '{trimmed}' が指定されたため安全フォールバックします。"
                None
            else
                Some trimmed

    /// <summary>
    /// 実行時の環境名（Development / Production 等）を解決しサニタイズする。
    /// 優先順位: DOTNET_ENVIRONMENT &gt; ASPNETCORE_ENVIRONMENT &gt; コンパイル時シンボル (#if DEBUG なら Development, それ以外は Production)
    /// </summary>
    let resolveEnvironmentName () : string =
        let envDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") |> Option.ofObj |> Option.bind sanitizeEnvironmentName
        let envAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") |> Option.ofObj |> Option.bind sanitizeEnvironmentName

        match envDotnet with
        | Some e -> e
        | None ->
            match envAspnet with
            | Some e -> e
            | None ->
#if DEBUG
                "Development"
#else
                "Production"
#endif

    /// <summary>後方互換性のための既定の構成名</summary>
    let defaultConfigurationName = resolveEnvironmentName ()

    /// <summary>
    /// 起点ディレクトリを明示注入可能な純粋 2 段階ディレクトリ解決関数。
    /// Stage 1: currentDir → appBaseDir の順に親最大 8 階層 (step 0..8) までの src/TagBasedVideoManager.Renamer/ を探索。
    /// Stage 2: 見つからない場合は appBaseDir を採用する（%APPDATA% 参照は完全廃止）。
    /// </summary>
    let resolveConfigDirectoryWithOrigins
        (currentDir: string)
        (appBaseDir: string)
        (envName: string)
        : string * string list =

        let safeEnvName = sanitizeEnvironmentName envName |> Option.defaultWith resolveEnvironmentName
        let safeAppBaseDir = if String.IsNullOrWhiteSpace(appBaseDir) then "" else appBaseDir
        let traces = ResizeArray<string>()

        let searchFromOrigin (originLabel: string) (startDir: string) : string option =
            if String.IsNullOrWhiteSpace(startDir) then
                traces.Add($"[TRACE] {originLabel}: 起点ディレクトリが空のため探索をスキップしました。")
                None
            else
                let rec stepLoop (dir: DirectoryInfo) (step: int) =
                    if isNull dir then
                        traces.Add($"[TRACE] {originLabel}: ルートディレクトリに到達したため探索を終了しました (step {step})。")
                        None
                    elif step > MaxParentSearchDepth then
                        traces.Add($"[TRACE] {originLabel}: 親探索上限 ({MaxParentSearchDepth} 階層) に到達したため探索を打ち切りました。")
                        None
                    else
                        let candidateDir = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer")
                        if Directory.Exists(candidateDir) then
                            let baseExists = File.Exists(Path.Combine(candidateDir, "appsettings.json"))
                            let envExists = File.Exists(Path.Combine(candidateDir, $"appsettings.{safeEnvName}.json"))
                            if baseExists || envExists then
                                traces.Add($"[TRACE] {originLabel} (step {step}): 設定ディレクトリを採用しました -> {candidateDir} (appsettings.json={baseExists}, appsettings.{safeEnvName}.json={envExists})")
                                Some candidateDir
                            else
                                traces.Add($"[TRACE] {originLabel} (step {step}): {candidateDir} は存在しますが設定ファイルが 0 件のため不採用としました。")
                                stepLoop dir.Parent (step + 1)
                        else
                            stepLoop dir.Parent (step + 1)

                stepLoop (DirectoryInfo(startDir)) 0

        match searchFromOrigin "CurrentDirectory" currentDir with
        | Some resolved -> resolved, Seq.toList traces
        | None ->
            match searchFromOrigin "AppBaseDirectory" safeAppBaseDir with
            | Some resolved -> resolved, Seq.toList traces
            | None ->
                traces.Add($"[TRACE] 第2段階フォールバック: 実行ディレクトリ ({safeAppBaseDir}) を共通設定ディレクトリとして採用します。")
                safeAppBaseDir, Seq.toList traces

    /// <summary>2段階共通ディレクトリ解決（baseDirOpt = Some dir 時は注入ディレクトリを即採用）</summary>
    let resolveConfigDirectory (baseDirOpt: string option) (envName: string) : string =
        let safeEnvName = sanitizeEnvironmentName envName |> Option.defaultWith resolveEnvironmentName
        match baseDirOpt with
        | Some dir when not (String.IsNullOrWhiteSpace(dir)) -> dir
        | _ ->
            let resolved, _ =
                resolveConfigDirectoryWithOrigins
                    (Directory.GetCurrentDirectory())
                    AppDomain.CurrentDomain.BaseDirectory
                    safeEnvName
            resolved

    /// <summary>設定ファイル（appsettings.{Environment}.json）の保存先パスを決定する（%APPDATA% フォールバックなし）</summary>
    let resolveSavePath (baseDirOpt: string option) : string =
        let envName = resolveEnvironmentName ()
        let targetDir = resolveConfigDirectory baseDirOpt envName
        Path.Combine(targetDir, $"appsettings.{envName}.json")

    /// <summary>既定の保存先パス</summary>
    let defaultSavePath () : string = resolveSavePath None

    /// <summary>ディレクトリ内の appsettings.json と appsettings.{envName}.json を読み込み、部分設定・読込済パス・警告一覧を返す</summary>
    let private loadDirectoryLayers (dirPath: string) (envName: string) : PartialSettings list * string list * string list =
        if String.IsNullOrWhiteSpace(dirPath) || not (Directory.Exists(dirPath)) then
            [], [], []
        else
            let candidates = [
                true, Path.Combine(dirPath, "appsettings.json")
                false, Path.Combine(dirPath, $"appsettings.{envName}.json")
            ]

            let partialsRev, loadedRev, warningsRev =
                candidates
                |> List.fold (fun (pAcc, lAcc, wAcc) (isBase, path) ->
                    if File.Exists(path) then
                        match parsePartial isBase path with
                        | Ok partial -> (partial :: pAcc, path :: lAcc, wAcc)
                        | Error err ->
                            let msg = $"設定ファイル ({path}) の読み込みまたは構文解析に失敗しました: {err}"
                            eprintfn $"[WARN] {msg}"
                            (pAcc, lAcc, msg :: wAcc)
                    else
                        (pAcc, lAcc, wAcc)
                ) ([], [], [])

            List.rev partialsRev, List.rev loadedRev, List.rev warningsRev

    /// <summary>コンテキスト注入対応のメタデータ付き設定ロード（2段階共通解決・%APPDATA% 廃止・出典パス保持）</summary>
    let loadConfigurationWithMetadataWithContext
        (ctx: ConfigLoadContext)
        (configNameOpt: string option)
        (jsonPathOpt: string option)
        (explicitDirOpt: string option)
        : LoadedConfiguration =

        let baseSettings = defaultSettings ()
        let safeCurrentDir = if String.IsNullOrWhiteSpace(ctx.CurrentDirectory) then "" else ctx.CurrentDirectory
        let safeAppBaseDir = if String.IsNullOrWhiteSpace(ctx.AppBaseDirectory) then "" else ctx.AppBaseDirectory
        let safeAppDataDir = if String.IsNullOrWhiteSpace(ctx.AppDataDirectory) then "" else ctx.AppDataDirectory
        let traceLogs = ResizeArray<string>()

        let envName =
            match configNameOpt |> Option.bind sanitizeEnvironmentName with
            | Some sanitized -> sanitized
            | None -> resolveEnvironmentName ()

        // .env 残存検出時の廃止通知ログ（読み込みはしない）
        let dotEnvCandidate =
            match ctx.DotEnvPath with
            | Some p when not (String.IsNullOrWhiteSpace(p)) && File.Exists(p) -> Some p
            | _ when not (String.IsNullOrWhiteSpace(safeCurrentDir)) ->
                let cur = Path.Combine(safeCurrentDir, ".env")
                if File.Exists(cur) then Some cur else None
            | _ -> None

        match dotEnvCandidate with
        | Some p ->
            let msg = $"[WARN] .env ファイル ({p}) が検出されましたが、設定読み込みは廃止されました。appsettings.{envName}.json をご利用ください。"
            eprintfn "%s" msg
            traceLogs.Add(msg)
        | None -> ()

        match jsonPathOpt with
        | Some path when not (String.IsNullOrWhiteSpace(path)) && File.Exists(path) ->
            let fullPath = Path.GetFullPath(path)
            let isBase = String.Equals(Path.GetFileName(fullPath), "appsettings.json", StringComparison.OrdinalIgnoreCase)
            let dir = Path.GetDirectoryName(fullPath)
            let savePath =
                if String.IsNullOrWhiteSpace(dir) then fullPath
                else Path.Combine(dir, $"appsettings.{envName}.json")

            traceLogs.Add($"[TRACE] 明示パス指定により単一設定ファイルを読み込みます: {fullPath}")

            match parsePartial isBase fullPath with
            | Ok partial ->
                let merged, apiKeySource, mergeWarnings = mergePartialsChain [ partial ] baseSettings
                for w in mergeWarnings do eprintfn $"[WARN] {w}"
                {
                    Settings = merged
                    EnvironmentName = envName
                    SavePath = savePath
                    ApiKeySourcePath = apiKeySource
                    LoadedFiles = [ fullPath ]
                    Warnings = mergeWarnings
                    TraceLogs = Seq.toList traceLogs
                }
            | Error err ->
                let warnMsg = $"設定ファイル ({fullPath}) の読み込みまたは構文解析に失敗しました: {err}"
                eprintfn $"[WARN] {warnMsg}"
                {
                    Settings = baseSettings
                    EnvironmentName = envName
                    SavePath = savePath
                    ApiKeySourcePath = None
                    LoadedFiles = []
                    Warnings = [ warnMsg ]
                    TraceLogs = Seq.toList traceLogs
                }
        | _ ->
            let resolvedDir, isNormalMode =
                match explicitDirOpt with
                | Some dir when not (String.IsNullOrWhiteSpace(dir)) ->
                    traceLogs.Add($"[TRACE] 明示ディレクトリ指定を採用しました: {dir}")
                    dir, false
                | _ ->
                    let dir, originTraces = resolveConfigDirectoryWithOrigins safeCurrentDir safeAppBaseDir envName
                    for t in originTraces do traceLogs.Add(t)
                    dir, true

            let savePath =
                if String.IsNullOrWhiteSpace(resolvedDir) then $"appsettings.{envName}.json"
                else Path.Combine(resolvedDir, $"appsettings.{envName}.json")

            // 通常起動時は旧 %APPDATA%\TagBasedVideoManager 残存ファイルを読み込まず移行案内ログのみ出力する
            if isNormalMode && not (String.IsNullOrWhiteSpace(safeAppDataDir)) && Directory.Exists(safeAppDataDir) then
                let legacyBase = Path.Combine(safeAppDataDir, "appsettings.json")
                let legacyEnv = Path.Combine(safeAppDataDir, $"appsettings.{envName}.json")
                if File.Exists(legacyBase) || File.Exists(legacyEnv) then
                    let migrateMsg =
                        $"[WARN] 旧設定ディレクトリ ({safeAppDataDir}) に設定ファイルが残存していますが、参照は廃止されました。必要な設定は {savePath} へ移行してください。"
                    eprintfn "%s" migrateMsg
                    traceLogs.Add(migrateMsg)

            let partials, loadedFiles, parseErrors = loadDirectoryLayers resolvedDir envName
            let merged, apiKeySource, mergeWarnings = mergePartialsChain partials baseSettings
            let allWarnings = parseErrors @ mergeWarnings
            for w in allWarnings do eprintfn $"[WARN] {w}"

            {
                Settings = merged
                EnvironmentName = envName
                SavePath = savePath
                ApiKeySourcePath = apiKeySource
                LoadedFiles = loadedFiles
                Warnings = allWarnings
                TraceLogs = Seq.toList traceLogs
            }

    /// <summary>メタデータ付き設定ロード（既定コンテキストで実行）</summary>
    let loadConfigurationWithMetadata
        (configNameOpt: string option)
        (jsonPathOpt: string option)
        (envPathOpt: string option)
        : LoadedConfiguration =

        let ctx = {
            CurrentDirectory = Directory.GetCurrentDirectory()
            AppBaseDirectory = AppDomain.CurrentDomain.BaseDirectory
            AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
            DotEnvPath = envPathOpt
        }

        let explicitDirOpt =
            envPathOpt
            |> Option.bind (fun ep ->
                if String.IsNullOrWhiteSpace(ep) then None
                else
                    let dir = Path.GetDirectoryName(ep)
                    if String.IsNullOrWhiteSpace(dir) then None else Some dir
            )

        loadConfigurationWithMetadataWithContext ctx configNameOpt jsonPathOpt explicitDirOpt

    /// <summary>ビルド構成名を指定して外部ファイル（appsettings.json + appsettings.{Environment}.json）を統合ロードする</summary>
    let loadConfigurationWithConfig (configNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        (loadConfigurationWithMetadata configNameOpt jsonPathOpt envPathOpt).Settings

    /// <summary>外部ファイルを統合ロードする（現在の解決環境名を使用）</summary>
    let loadConfiguration (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        loadConfigurationWithConfig (Some (resolveEnvironmentName ())) jsonPathOpt envPathOpt

    /// <summary>JsonElement を重複キー後勝ちで安全に JsonNode へ変換する（JsonNode.Parse の重複キー ArgumentException を回避）</summary>
    let rec private jsonElementToNode (elem: JsonElement) : JsonNode =
        match elem.ValueKind with
        | JsonValueKind.Object ->
            let obj = JsonObject()
            for prop in elem.EnumerateObject() do
                obj.[prop.Name] <- jsonElementToNode prop.Value
            obj :> JsonNode
        | JsonValueKind.Array ->
            let arr = JsonArray()
            for item in elem.EnumerateArray() do
                arr.Add(jsonElementToNode item)
            arr :> JsonNode
        | JsonValueKind.String ->
            JsonValue.Create(elem.GetString()) :> JsonNode
        | JsonValueKind.True ->
            JsonValue.Create(true) :> JsonNode
        | JsonValueKind.False ->
            JsonValue.Create(false) :> JsonNode
        | JsonValueKind.Number ->
            JsonNode.Parse(elem.GetRawText())
        | _ ->
            null

    /// <summary>既存設定ファイルを保護しつつ指定キーのみを部分更新保存する（排他ロック・段階別診断・重複キー安全化付き）</summary>
    let savePatch (filePath: string) (patch: SettingsPatch) : Result<unit, RenamerError> =
        if patch.SelectedModel.IsNone && patch.Rules.IsNone then
            Ok ()
        elif String.IsNullOrWhiteSpace(filePath) then
            Error (IoError ("設定ファイルの保存先パスが空です。", None))
        else
            try
                let fullPath = Path.GetFullPath(filePath)
                let lockObj = patchFileLocks.GetOrAdd(fullPath, fun _ -> obj())

                lock lockObj (fun () ->
                    // 1. 段階 ReadExisting: 既存ファイルが存在する場合のみリトライ付き読み込み
                    let readResult =
                        if File.Exists(fullPath) then
                            match retryIo 5 10 (fun () -> File.ReadAllText(fullPath, Encoding.UTF8)) with
                            | Ok content -> Ok (Some content)
                            | Error ex ->
                                Error (IoError ($"設定ファイル '{fullPath}' の読み込みに失敗しました (段階: ReadExisting, 試行回数: 5回): {ex.Message}", Some ex))
                        else
                            Ok None

                    // 2. 段階 ParseJson: 0バイト/構文エラー/非オブジェクト時は元ファイルを破壊せず即中止（重複キーは後勝ち正規化）
                    let parseResult =
                        readResult
                        |> Result.bind (function
                            | None -> Ok (JsonObject())
                            | Some rawText when String.IsNullOrWhiteSpace(rawText) ->
                                Error (SettingsError $"設定ファイル '{fullPath}' は空（0バイトまたは空白のみ）のため部分保存を中止しました (段階: ParseJson, Line 1, Pos 0)。")
                            | Some rawText ->
                                try
                                    use doc = JsonDocument.Parse(rawText, jsonDocumentOptions)
                                    if doc.RootElement.ValueKind <> JsonValueKind.Object then
                                        Error (SettingsError $"設定ファイル '{fullPath}' のルートがJSONオブジェクトではないため部分保存を中止しました (段階: ParseJson, Line 1, Pos 0)。")
                                    else
                                        Ok (jsonElementToNode doc.RootElement :?> JsonObject)
                                with :? JsonException as ex ->
                                    let line = if ex.LineNumber.HasValue then ex.LineNumber.Value + 1L else 1L
                                    let pos = if ex.BytePositionInLine.HasValue then ex.BytePositionInLine.Value else 0L
                                    Error (SettingsError $"設定ファイル '{fullPath}' のJSON形式が不正なため部分保存を中止しました (段階: ParseJson, Line {line}, Pos {pos}): {ex.Message}")
                        )

                    parseResult
                    |> Result.bind (fun root ->
                        // パッチ適用（PascalCase 重複キーは除去し camelCase へ統一）
                        patch.SelectedModel
                        |> Option.iter (fun modelName ->
                            root.Remove("SelectedModel") |> ignore
                            root.["selectedModel"] <- JsonValue.Create(modelName)
                        )

                        patch.Rules
                        |> Option.iter (fun rules ->
                            root.Remove("Rules") |> ignore
                            root.["rules"] <- JsonSerializer.SerializeToNode(rules, jsonOptions)
                        )

                        // 3. 段階 CreateDirectory -> WriteTempFile -> AtomicMove
                        writeAtomicWithRetry 5 10 fullPath (root.ToJsonString(jsonOptions))
                    )
                )
            with ex ->
                Error (IoError ($"設定ファイルの保存中にエラーが発生しました '{filePath}': {ex.Message}", Some ex))

    /// <summary>命名ルールの並び順を更新する</summary>
    let reorderRules (ruleIdsInOrder: string list) (settings: RenamerSettings) : RenamerSettings =
        let ruleMap = settings.Rules |> List.map (fun r -> r.Id, r) |> Map.ofList
        let orderedRules =
            ruleIdsInOrder
            |> List.choose (fun id -> Map.tryFind id ruleMap)
            |> List.mapi (fun idx rule -> { rule with Order = idx })

        let includedIds = set ruleIdsInOrder
        let remainingRules =
            settings.Rules
            |> List.filter (fun r -> not (Set.contains r.Id includedIds))
            |> List.mapi (fun idx rule -> { rule with Order = orderedRules.Length + idx })

        { settings with Rules = orderedRules @ remainingRules }
