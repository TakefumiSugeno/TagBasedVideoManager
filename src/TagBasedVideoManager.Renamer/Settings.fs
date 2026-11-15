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

    /// 既定の設定値を生成（OS環境変数は参照しない）
    let defaultSettings () : RenamerSettings =
        {
            TargetDirectory = ""
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

    /// 指定パスへ設定ファイルをアトミックに保存する（GUID一意一時ファイル経由）
    let save (filePath: string) (settings: RenamerSettings) : Result<unit, RenamerError> =
        let tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp"
        try
            let dir = Path.GetDirectoryName(filePath)
            if not (String.IsNullOrEmpty(dir)) && not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            let json = JsonSerializer.Serialize(settings, jsonOptions)
            File.WriteAllText(tmpPath, json)

            // 並行保存時の競合（File.Move overwrite）に備えて最大10回リトライ（Windowsでの一時的なロック/アクセス拒否対策）
            let rec moveWithRetry attempts =
                try
                    File.Move(tmpPath, filePath, overwrite = true)
                with
                | (:? IOException | :? UnauthorizedAccessException) when attempts > 0 ->
                    System.Threading.Thread.Sleep(25)
                    moveWithRetry (attempts - 1)
                | _ -> reraise ()

            moveWithRetry 10
            Ok ()
        with
        | ex ->
            try
                if File.Exists(tmpPath) then File.Delete(tmpPath)
            with _ -> ()
            Error (IoError ($"設定ファイルの保存中にエラーが発生しました: {ex.Message}", Some ex))

    /// 部分的な設定値を保持する内部レコード
    type private PartialSettings = {
        TargetDirectory: string option
        PathLengthThreshold: int option
        SelectedModel: string option
        ApiKey: string option option // Some (Some k) = 値指定, Some None = 明示的null, None = キー未指定
        Rules: NamingRule list option // Some rules = 明示配列指定（空配列含む）, None = 未指定
    }

    /// 指定されたJSONファイルから部分設定を読み込む
    let private parsePartial (filePath: string) : Result<PartialSettings, string> =
        try
            if not (File.Exists(filePath)) then
                Error $"File not found: {filePath}"
            else
                let text = File.ReadAllText(filePath)
                use doc = JsonDocument.Parse(text)
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

    /// 複数レイヤーの部分設定を順に重ねてマージする（後のレイヤーが優先）
    let private mergePartialsChain (partials: PartialSettings list) (fallback: RenamerSettings) : RenamerSettings =
        let rec pickValue picker defVal = function
            | [] -> defVal
            | p :: rest ->
                match picker p with
                | Some v -> v
                | None -> pickValue picker defVal rest

        let revPartials = List.rev partials // 優先度高い順（最新が先頭）

        let targetDir = pickValue (fun p -> p.TargetDirectory) fallback.TargetDirectory revPartials
        let threshold = pickValue (fun p -> p.PathLengthThreshold) fallback.PathLengthThreshold revPartials
        let model = pickValue (fun p -> p.SelectedModel) fallback.SelectedModel revPartials

        let rec pickApiKey = function
            | [] -> fallback.ApiKey
            | p :: rest ->
                match p.ApiKey with
                | Some (Some k) -> Some k
                | Some None -> None // 明示的 null 上書き
                | None -> pickApiKey rest

        let apiKey = pickApiKey revPartials

        let rec pickRules = function
            | [] -> fallback.Rules
            | p :: rest ->
                match p.Rules with
                | Some r -> r |> List.sortBy (fun item -> item.Order)
                | None -> pickRules rest

        let rules = pickRules revPartials

        {
            TargetDirectory = targetDir
            PathLengthThreshold = threshold
            SelectedModel = model
            ApiKey = apiKey
            Rules = rules
        }

    /// 実行時の環境名（Development / Production 等）を解決しサニタイズする
    /// 優先順位: DOTNET_ENVIRONMENT > ASPNETCORE_ENVIRONMENT > コンパイル時シンボル (#if DEBUG なら Development, それ以外は Production)
    let resolveEnvironmentName () : string =
        let sanitize (s: string) =
            if String.IsNullOrWhiteSpace(s) then None
            else
                let trimmed = s.Trim()
                let invalidChars = Path.GetInvalidFileNameChars()
                if trimmed.Contains("/") || trimmed.Contains("\\") || trimmed.Contains("..") || trimmed.IndexOfAny(invalidChars) >= 0 then
                    eprintfn $"[WARN] 不正な環境名 '{trimmed}' が指定されたため安全フォールバックします。"
                    None
                else
                    Some trimmed

        let envDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") |> Option.ofObj |> Option.bind sanitize
        let envAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") |> Option.ofObj |> Option.bind sanitize

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

    /// 後方互換性のための既定の構成名
    let defaultConfigurationName = resolveEnvironmentName ()

    /// 設定ファイル（appsettings.{Environment}.json）の保存先パスを決定する
    /// 1. アプリ実行ディレクトリ（または指定baseDir）に既に appsettings.json または appsettings.{env}.json が存在し書込可能ならそこへ保存
    /// 2. 存在しない、または書込不可の場合はユーザー標準設定ディレクトリ（%APPDATA%\TagBasedVideoManager\appsettings.{env}.json）へ保存
    let resolveSavePath (baseDirOpt: string option) : string =
        let envName = resolveEnvironmentName ()
        let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
        let appDataTarget = Path.Combine(appDataDir, $"appsettings.{envName}.json")
        let baseDir = baseDirOpt |> Option.defaultValue AppDomain.CurrentDomain.BaseDirectory

        let baseCandidate = Path.Combine(baseDir, "appsettings.json")
        let envCandidate = Path.Combine(baseDir, $"appsettings.{envName}.json")
        if (File.Exists(baseCandidate) || File.Exists(envCandidate)) then
            let testFile = Path.Combine(baseDir, $".perm_test_{Guid.NewGuid():N}.tmp")
            try
                File.WriteAllText(testFile, "test")
                File.Delete(testFile)
                envCandidate
            with _ ->
                if not (Directory.Exists(appDataDir)) then
                    try Directory.CreateDirectory(appDataDir) |> ignore with _ -> ()
                appDataTarget
        else
            if not (Directory.Exists(appDataDir)) then
                try Directory.CreateDirectory(appDataDir) |> ignore with _ -> ()
            appDataTarget

    /// 既定の保存先パス
    let defaultSavePath () : string = resolveSavePath None

    /// ビルド構成名を指定して外部ファイル（appsettings.json + appsettings.{Environment}.json）を統合ロードする
    /// 優先順位: 明示パス > (%APPDATA% appsettings.json -> appsettings.{env}.json) > (ベース層 appsettings.json -> appsettings.{env}.json) > 組み込み既定値
    /// ※ .env は読み込まず無視（検出時は警告出力）
    let loadConfigurationWithConfig (configNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        let baseSettings = defaultSettings ()

        // .env 残存検出時の警告ログ（読み込みはしない）
        let checkDotEnvWarn (pathOpt: string option) =
            let candidate =
                match pathOpt with
                | Some p when File.Exists(p) -> Some p
                | _ ->
                    let cur = Path.Combine(Directory.GetCurrentDirectory(), ".env")
                    if File.Exists(cur) then Some cur else None
            match candidate with
            | Some p ->
                eprintfn $"[WARN] .env ファイル ({p}) が検出されましたが、設定読み込みは廃止されました。appsettings.{{Environment}}.json をご利用ください。"
            | None -> ()

        checkDotEnvWarn envPathOpt

        // 1. 明示パス指定がある場合は単一ファイルを最優先ロード
        match jsonPathOpt with
        | Some path when File.Exists(path) ->
            match parsePartial path with
            | Ok partial -> mergePartialsChain [ partial ] baseSettings
            | Error _ ->
                match load path with
                | Ok loaded -> loaded
                | Error _ -> baseSettings
        | _ ->
            let envName =
                match configNameOpt with
                | Some cfg when not (String.IsNullOrWhiteSpace(cfg)) ->
                    let sanitize (s: string) =
                        let trimmed = s.Trim()
                        let invalidChars = Path.GetInvalidFileNameChars()
                        if trimmed.Contains("/") || trimmed.Contains("\\") || trimmed.Contains("..") || trimmed.IndexOfAny(invalidChars) >= 0 then
                            resolveEnvironmentName ()
                        else trimmed
                    sanitize cfg
                | _ -> resolveEnvironmentName ()

            // ディレクトリ内の appsettings.json と appsettings.{env}.json を取得するヘルパー
            let getDirPartials (dirPath: string) : PartialSettings list =
                if String.IsNullOrWhiteSpace(dirPath) || not (Directory.Exists(dirPath)) then []
                else
                    let basePath = Path.Combine(dirPath, "appsettings.json")
                    let envPath = Path.Combine(dirPath, $"appsettings.{envName}.json")
                    let tryLoadPartial p =
                        if File.Exists(p) then
                            match parsePartial p with
                            | Ok partial -> Some partial
                            | Error err ->
                                eprintfn $"[WARN] 設定ファイル ({p}) の構文解析に失敗しました: {err}"
                                None
                        else None
                    let pBase = tryLoadPartial basePath
                    let pEnv = tryLoadPartial envPath
                    [ pBase; pEnv ] |> List.choose id

            let isTestOrExplicitDir =
                match envPathOpt with
                | Some ep when not (String.IsNullOrWhiteSpace(ep)) -> Some (Path.GetDirectoryName(ep))
                | _ -> None

            match isTestOrExplicitDir with
            | Some explicitDir ->
                let dirPartials = getDirPartials explicitDir
                mergePartialsChain dirPartials baseSettings
            | None ->
                // 通常起動時: ベース探索階層 + %APPDATA%
                let findBaseDirPartials () =
                    let findProjectDir () =
                        let rec findUp (dir: DirectoryInfo) (depth: int) =
                            if depth <= 0 || box dir = null then None
                            else
                                let candidateDir = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer")
                                let partials = getDirPartials candidateDir
                                if not (List.isEmpty partials) then Some partials
                                else findUp dir.Parent (depth - 1)
                        let current = DirectoryInfo(Directory.GetCurrentDirectory())
                        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                        findUp current 8 |> Option.orElseWith (fun () -> findUp baseDir 8)

                    let findCurrentDir () =
                        let p = getDirPartials (Directory.GetCurrentDirectory())
                        if not (List.isEmpty p) then Some p else None

                    let findAppBaseDir () =
                        let p = getDirPartials AppDomain.CurrentDomain.BaseDirectory
                        if not (List.isEmpty p) then Some p else None

                    findProjectDir ()
                    |> Option.orElseWith findCurrentDir
                    |> Option.orElseWith findAppBaseDir
                    |> Option.defaultValue []

                let basePartials = findBaseDirPartials ()
                let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
                let appDataPartials = getDirPartials appDataDir

                // ベース探索階層（basePartials）の上に、最優先オーバーライド層として AppData（appDataPartials）を重ねる
                mergePartialsChain (basePartials @ appDataPartials) baseSettings

    /// 外部ファイルを統合ロードする（既定のビルド構成を使用）
    let loadConfiguration (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        loadConfigurationWithConfig (Some defaultConfigurationName) jsonPathOpt envPathOpt

    /// 命名ルールの並び順を更新する
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
