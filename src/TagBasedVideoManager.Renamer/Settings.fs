namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open TagBasedVideoManager.Renamer

module Settings =

    let private defaultRules = [
        {
            Id = "rule-date-action"
            Name = "日付_撮影地_行動 (推奨)"
            Pattern = "{Date}_{Location}_{Activity}.mp4"
            PromptInstruction = "ファイル名から撮影日(YYYYMMDD)、撮影場所、主な行動/内容を抽出しアンダースコア繋ぎで命名してください。情報が不足している場合は親ディレクトリ名や更新日時から推測してください。"
            Order = 0
        }
        {
            Id = "rule-compact"
            Name = "短縮 (タイトルのみ)"
            Pattern = "{ShortTitle}.mp4"
            PromptInstruction = "動画の内容を表す最も重要で短いキーワード（20文字以内）で命名してください。"
            Order = 1
        }
    ]

    let private jsonOptions =
        let options = JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
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
    /// 外部ファイル（.env / companion-settings.json）および環境変数を統合ロードする
    /// 優先順位: JSON > .env > OS環境変数 > 組み込み既定値
    /// </summary>
    let loadConfiguration (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        let baseSettings = defaultSettings ()
        let envMap = parseDotEnv envPathOpt

        // 1. 環境変数 & .env からの値取得
        let getVal key =
            Map.tryFind key envMap
            |> Option.orElseWith (fun () ->
                let v = Environment.GetEnvironmentVariable(key)
                if not (String.IsNullOrWhiteSpace(v)) then Some v else None
            )

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

        // 2. companion-settings.json からの読み込み（最優先）
        let resolvedJsonPath =
            match jsonPathOpt with
            | Some p -> Some p
            | None ->
                match envPathOpt with
                | Some ep ->
                    let dir = Path.GetDirectoryName(ep)
                    if not (String.IsNullOrWhiteSpace(dir)) then
                        let candidate = Path.Combine(dir, "companion-settings.json")
                        if File.Exists(candidate) then Some candidate else None
                    else None
                | None ->
                    let current = Path.Combine(Directory.GetCurrentDirectory(), "companion-settings.json")
                    if File.Exists(current) then Some current
                    else
                        let baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion-settings.json")
                        if File.Exists(baseDir) then Some baseDir else None

        match resolvedJsonPath with
        | Some path ->
            match load path with
            | Ok loadedJson ->
                // JSONに値があればJSON優先。ただしApiKeyがNoneなら.envのキーをフォールバック
                let effectiveKey =
                    match loadedJson.ApiKey with
                    | Some k when not (String.IsNullOrWhiteSpace(k)) -> Some k
                    | _ -> interimSettings.ApiKey

                let sortedRules = loadedJson.Rules |> List.sortBy (fun r -> r.Order)

                {
                    loadedJson with
                        ApiKey = effectiveKey
                        Rules = if List.isEmpty sortedRules then interimSettings.Rules else sortedRules
                }
            | Error _ ->
                interimSettings
        | None ->
            interimSettings

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
