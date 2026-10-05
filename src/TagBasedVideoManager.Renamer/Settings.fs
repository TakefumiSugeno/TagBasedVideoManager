namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Unicode
open TagBasedVideoManager.Renamer

module Settings =

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
        let options = JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        options.Encoder <- JavaScriptEncoder.Create(UnicodeRanges.All)
        options.Converters.Add(JsonStringEnumConverter())
        options


    /// 既定の設定値を生成
    let defaultSettings () : RenamerSettings =
        let defaultDir =
            let envVideo = Environment.GetEnvironmentVariable("VIDEO_DIR")
            if not (String.IsNullOrWhiteSpace(envVideo)) && Directory.Exists(envVideo) then envVideo
            elif Directory.Exists("test/videos") then Path.GetFullPath("test/videos")
            else Directory.GetCurrentDirectory()
        {
            TargetDirectory = defaultDir
            PathLengthThreshold = 240
            SelectedModel = "meta-llama/llama-3.3-70b-instruct:free"
            ApiKey = None
            Rules = defaultRules
        }

    /// 指定パスから設定ファイルを読み込む
    let load (filePath: string) : Result<RenamerSettings, RenamerError> =
        try
            if not (File.Exists(filePath)) then
                Error (IoError ($"設定ファイルが見つかりません: {filePath}", None))
            else
                let json = File.ReadAllText(filePath)
                try
                    let settings = JsonSerializer.Deserialize<RenamerSettings>(json, jsonOptions)
                    if box settings = null then
                        Error (SettingsError "設定ファイルのデシリアライズ結果が null です")
                    else
                        Ok settings
                with
                | :? JsonException as ex ->
                    Error (SettingsError ($"設定ファイルのJSONパースに失敗しました: {ex.Message}"))
        with
        | ex ->
            Error (IoError ($"設定ファイルの読み込み中にエラーが発生しました: {ex.Message}", Some ex))

    /// 指定パスへ設定ファイルを保存する
    let save (filePath: string) (settings: RenamerSettings) : Result<unit, RenamerError> =
        try
            let dir = Path.GetDirectoryName(filePath)
            if not (String.IsNullOrEmpty(dir)) && not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            let json = JsonSerializer.Serialize(settings, jsonOptions)
            File.WriteAllText(filePath, json)
            Ok ()
        with
        | ex ->
            Error (IoError ($"設定ファイルの保存中にエラーが発生しました: {ex.Message}", Some ex))

    /// .env ファイルを探索・パースしてキー・バリューのマップを返す
    let parseDotEnv (envPathOpt: string option) : Map<string, string> =
        let resolveEnvPath () =
            match envPathOpt with
            | Some p when File.Exists(p) -> Some p
            | _ ->
                // カレントディレクトリおよび親ディレクトリを探索
                let rec findEnv (dir: DirectoryInfo) (depth: int) =
                    if depth <= 0 || box dir = null then None
                    else
                        let candidate = Path.Combine(dir.FullName, ".env")
                        if File.Exists(candidate) then Some candidate
                        else findEnv dir.Parent (depth - 1)
                let current = DirectoryInfo(Directory.GetCurrentDirectory())
                let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                findEnv current 4 |> Option.orElseWith (fun () -> findEnv baseDir 4)

        match resolveEnvPath () with
        | None -> Map.empty
        | Some path ->
            try
                File.ReadAllLines(path)
                |> Array.choose (fun line ->
                    let trimmed = line.Trim()
                    if String.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") then None
                    else
                        let eqIdx = trimmed.IndexOf('=')
                        if eqIdx > 0 then
                            let key = trimmed.Substring(0, eqIdx).Trim()
                            let value = trimmed.Substring(eqIdx + 1).Trim().Trim('"', '\'')
                            Some (key, value)
                        else None
                )
                |> Map.ofArray
            with _ -> Map.empty

    /// <summary>
    /// 部分的な設定値を保持する内部レコード
    /// </summary>
    type private PartialSettings = {
        TargetDirectory: string option
        PathLengthThreshold: int option
        SelectedModel: string option
        ApiKey: string option option // Some (Some k) = 値指定, Some None = 明示的null, None = キー未指定
        Rules: NamingRule list option
    }

    /// <summary>
    /// 指定されたJSONファイルから部分設定を読み込む
    /// </summary>
    let private parsePartial (filePath: string) : Result<PartialSettings, string> =
        try
            if not (File.Exists(filePath)) then
                Error $"File not found: {filePath}"
            else
                use doc = JsonDocument.Parse(File.ReadAllText(filePath))
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

                    let targetDir =
                        getProp "targetDirectory"
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

                    let selectedModel =
                        getProp "selectedModel"
                        |> Option.bind (fun e ->
                            if e.ValueKind = JsonValueKind.String then Some (e.GetString())
                            else None
                        )

                    let apiKey =
                        getProp "apiKey"
                        |> Option.map (fun e ->
                            if e.ValueKind = JsonValueKind.String then
                                let s = e.GetString()
                                if String.IsNullOrWhiteSpace(s) then None
                                else Some (s.Trim().Trim('"', '\''))
                            else None // ValueKind.Null 等
                        )

                    let rules =
                        getProp "rules"
                        |> Option.bind (fun e ->
                            if e.ValueKind = JsonValueKind.Array then
                                try
                                    let parsedRules = JsonSerializer.Deserialize<NamingRule list>(e.GetRawText(), jsonOptions)
                                    Some parsedRules
                                with _ -> None
                            else None
                        )

                    Ok {
                        TargetDirectory = targetDir
                        PathLengthThreshold = threshold
                        SelectedModel = selectedModel
                        ApiKey = apiKey
                        Rules = rules
                    }
        with ex -> Error ex.Message

    /// <summary>
    /// ベース設定と環境別設定をマージする
    /// </summary>
    let private mergePartials (baseOpt: PartialSettings option) (envOpt: PartialSettings option) (fallback: RenamerSettings) : RenamerSettings =
        let pickValue envVal baseVal defVal =
            match envVal with
            | Some v -> v
            | None ->
                match baseVal with
                | Some v -> v
                | None -> defVal

        let targetDir =
            pickValue
                (envOpt |> Option.bind (fun e -> e.TargetDirectory))
                (baseOpt |> Option.bind (fun b -> b.TargetDirectory))
                fallback.TargetDirectory

        let threshold =
            pickValue
                (envOpt |> Option.bind (fun e -> e.PathLengthThreshold))
                (baseOpt |> Option.bind (fun b -> b.PathLengthThreshold))
                fallback.PathLengthThreshold

        let model =
            pickValue
                (envOpt |> Option.bind (fun e -> e.SelectedModel))
                (baseOpt |> Option.bind (fun b -> b.SelectedModel))
                fallback.SelectedModel

        let apiKey =
            let envKey = envOpt |> Option.bind (fun e -> e.ApiKey)
            let baseKey = baseOpt |> Option.bind (fun b -> b.ApiKey)
            match envKey with
            | Some (Some k) -> Some k
            | Some None -> None // 明示的 null 上書き
            | None ->
                match baseKey with
                | Some (Some k) -> Some k
                | Some None -> fallback.ApiKey
                | None -> fallback.ApiKey

        let rules =
            let envRules = envOpt |> Option.bind (fun e -> e.Rules) |> Option.filter (fun r -> not (List.isEmpty r))
            let baseRules = baseOpt |> Option.bind (fun b -> b.Rules) |> Option.filter (fun r -> not (List.isEmpty r))
            let candidateRules =
                match envRules with
                | Some r -> r
                | None ->
                    match baseRules with
                    | Some r -> r
                    | None -> fallback.Rules
            let sorted = candidateRules |> List.sortBy (fun r -> r.Order)
            if List.isEmpty sorted then fallback.Rules else sorted

        {
            TargetDirectory = targetDir
            PathLengthThreshold = threshold
            SelectedModel = model
            ApiKey = apiKey
            Rules = rules
        }

    /// <summary>
    /// 実行時の環境名（Development / Production 等）を解決する
    /// 優先順位: DOTNET_ENVIRONMENT > ASPNETCORE_ENVIRONMENT > コンパイル時シンボル (#if DEBUG なら Development, それ以外は Production)
    /// </summary>
    let resolveEnvironmentName () : string =
        let envDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        let envAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        if not (String.IsNullOrWhiteSpace(envDotnet)) then
            envDotnet.Trim()
        elif not (String.IsNullOrWhiteSpace(envAspnet)) then
            envAspnet.Trim()
        else
#if DEBUG
            "Development"
#else
            "Production"
#endif

    /// <summary>
    /// 後方互換性のための既定の構成名
    /// </summary>
    let defaultConfigurationName = resolveEnvironmentName ()

    /// <summary>
    /// ビルド構成名を指定して外部ファイル（appsettings.json + appsettings.{Environment}.json / .env）を統合ロードする
    /// 優先順位: 明示パス > (appsettings.json + appsettings.{Environment}.json) > .env > 組み込み既定値
    /// </summary>
    let loadConfigurationWithConfig (configNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        let baseSettings = defaultSettings ()
        let envMap = parseDotEnv envPathOpt

        // 1. .env からの値取得
        let getVal key =
            Map.tryFind key envMap
            |> Option.bind (fun v -> if not (String.IsNullOrWhiteSpace(v)) then Some v else None)

        let envTargetDir = getVal "VIDEO_DIR" |> Option.defaultValue baseSettings.TargetDirectory
        let envApiKey = getVal "OPENROUTER_API_KEY"
        let envModel = getVal "OPENROUTER_MODEL" |> Option.defaultValue baseSettings.SelectedModel
        let envThreshold =
            getVal "PATH_LENGTH_THRESHOLD"
            |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
            |> Option.defaultValue baseSettings.PathLengthThreshold

        let interimSettings = {
            baseSettings with
                TargetDirectory = envTargetDir
                ApiKey = envApiKey
                SelectedModel = envModel
                PathLengthThreshold = envThreshold
        }

        // 2. 明示パス指定がある場合は単一ファイルを最優先ロード（マージなし）
        match jsonPathOpt with
        | Some path when File.Exists(path) ->
            match load path with
            | Ok loadedJson ->
                let effectiveKey =
                    match loadedJson.ApiKey with
                    | Some k when not (String.IsNullOrWhiteSpace(k)) ->
                        let trimmed = k.Trim().Trim('"', '\'')
                        if String.IsNullOrWhiteSpace(trimmed) then interimSettings.ApiKey
                        else Some trimmed
                    | _ -> interimSettings.ApiKey

                let sortedRules = loadedJson.Rules |> List.sortBy (fun r -> r.Order)

                {
                    loadedJson with
                        ApiKey = effectiveKey
                        Rules = if List.isEmpty sortedRules then interimSettings.Rules else sortedRules
                }
            | Error _ -> interimSettings
        | _ ->
            // 3. 環境名解決
            let envName =
                match configNameOpt with
                | Some cfg when not (String.IsNullOrWhiteSpace(cfg)) -> cfg.Trim()
                | _ -> resolveEnvironmentName ()

            // 4. 設定ディレクトリの解決（ベースまたは環境別ファイルが存在するディレクトリを探索）
            let resolveConfigDir (dirPath: string) : (string option * string option) option =
                if String.IsNullOrWhiteSpace(dirPath) || not (Directory.Exists(dirPath)) then None
                else
                    let basePath = Path.Combine(dirPath, "appsettings.json")
                    let envPath = Path.Combine(dirPath, $"appsettings.{envName}.json")
                    let hasBase = File.Exists(basePath)
                    let hasEnv = File.Exists(envPath)
                    if hasBase || hasEnv then
                        Some (
                            (if hasBase then Some basePath else None),
                            (if hasEnv then Some envPath else None)
                        )
                    else None

            let resolvedFilesOpt =
                match envPathOpt with
                | Some ep when not (String.IsNullOrWhiteSpace(ep)) ->
                    let dir = Path.GetDirectoryName(ep)
                    resolveConfigDir dir
                | _ ->
                    // 1. プロジェクトソース直下
                    let findProjectDir () =
                        let rec findUp (dir: DirectoryInfo) (depth: int) =
                            if depth <= 0 || box dir = null then None
                            else
                                let candidateDir = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer")
                                match resolveConfigDir candidateDir with
                                | Some files -> Some files
                                | None -> findUp dir.Parent (depth - 1)
                        let current = DirectoryInfo(Directory.GetCurrentDirectory())
                        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                        findUp current 8 |> Option.orElseWith (fun () -> findUp baseDir 8)

                    // 2. カレントディレクトリ
                    let findCurrentDir () =
                        resolveConfigDir (Directory.GetCurrentDirectory())

                    // 3. 実行ディレクトリ
                    let findBaseDir () =
                        resolveConfigDir AppDomain.CurrentDomain.BaseDirectory

                    // 4. AppData 領域
                    let findAppDataDir () =
                        let appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
                        resolveConfigDir appDataPath

                    findProjectDir ()
                    |> Option.orElseWith findCurrentDir
                    |> Option.orElseWith findBaseDir
                    |> Option.orElseWith findAppDataDir

            match resolvedFilesOpt with
            | Some (basePathOpt, envPathOpt) ->
                let basePartial =
                    basePathOpt
                    |> Option.bind (fun p ->
                        match parsePartial p with
                        | Ok pt -> Some pt
                        | Error _ -> None
                    )
                let envPartial =
                    envPathOpt
                    |> Option.bind (fun p ->
                        match parsePartial p with
                        | Ok pt -> Some pt
                        | Error _ -> None
                    )

                mergePartials basePartial envPartial interimSettings
            | None ->
                interimSettings

    /// <summary>
    /// 外部ファイル（appsettings.json / .env）を統合ロードする（既定のビルド構成を使用）
    /// 優先順位: appsettings.{Configuration}.json > appsettings.json > .env > 組み込み既定値
    /// </summary>
    let loadConfiguration (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        loadConfigurationWithConfig (Some defaultConfigurationName) jsonPathOpt envPathOpt

    /// <summary>
    /// 設定ファイル（appsettings.json）の保存先パスを決定する
    /// 1. アプリ実行ディレクトリ（BaseDirectory）に既に appsettings.json が存在する場合はそこへ上書き（ポータブル優先）
    /// 2. 存在しない場合はユーザー標準設定ディレクトリ（%APPDATA%\TagBasedVideoManager\appsettings.json）へ保存
    /// </summary>
    let resolveSavePath (baseDirOpt: string option) : string =
        let baseDir = baseDirOpt |> Option.defaultValue AppDomain.CurrentDomain.BaseDirectory
        let baseCandidate = Path.Combine(baseDir, "appsettings.json")
        if File.Exists(baseCandidate) then
            baseCandidate
        else
            let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
            if not (Directory.Exists(appDataDir)) then
                try Directory.CreateDirectory(appDataDir) |> ignore with _ -> ()
            Path.Combine(appDataDir, "appsettings.json")

    /// 既定の保存先パス
    let defaultSavePath () : string = resolveSavePath None

    /// 命名ルールの並び順を更新する
    let reorderRules (ruleIdsInOrder: string list) (settings: RenamerSettings) : RenamerSettings =
        let ruleMap = settings.Rules |> List.map (fun r -> r.Id, r) |> Map.ofList
        let orderedRules =
            ruleIdsInOrder
            |> List.choose (fun id -> Map.tryFind id ruleMap)
            |> List.mapi (fun idx rule -> { rule with Order = idx })

        // リストに含まれていなかった残りのルールがあれば末尾に追加
        let includedIds = set ruleIdsInOrder
        let remainingRules =
            settings.Rules
            |> List.filter (fun r -> not (Set.contains r.Id includedIds))
            |> List.mapi (fun idx rule -> { rule with Order = orderedRules.Length + idx })

        { settings with Rules = orderedRules @ remainingRules }
