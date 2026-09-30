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
    /// コンパイル時シンボルに基づく既定の構成名（Debug または Release）
    /// </summary>
    let defaultConfigurationName =
#if DEBUG
        "Debug"
#else
        "Release"
#endif

    /// <summary>
    /// ビルド構成名を指定して外部ファイル（appsettings.{Configuration}.json / appsettings.json / .env）を統合ロードする
    /// 優先順位: appsettings.{Configuration}.json > appsettings.json > .env > 組み込み既定値
    /// </summary>
    let loadConfigurationWithConfig (configNameOpt: string option) (jsonPathOpt: string option) (envPathOpt: string option) : RenamerSettings =
        let baseSettings = defaultSettings ()
        let envMap = parseDotEnv envPathOpt

        // 1. .env からの値取得（※OS環境変数は意図しない混入を防ぐため参照しない）
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

        // 2. appsettings.json からの読み込み（最優先）
        // 探索優先度:
        //   明示指定 (jsonPathOpt)
        //   > envPathOpt 親ディレクトリ
        //   > プロジェクト直下 src/TagBasedVideoManager.Renamer/appsettings.json
        //   > カレントディレクトリ ./appsettings.json
        //   > 実行ディレクトリ {BaseDirectory}\appsettings.json
        //   > %APPDATA%\TagBasedVideoManager\appsettings.json (フォールバック)
        let resolvedJsonPath =
            match jsonPathOpt with
            | Some p -> Some p
            | None ->
                match envPathOpt with
                | Some ep ->
                    let dir = Path.GetDirectoryName(ep)
                    if not (String.IsNullOrWhiteSpace(dir)) then
                        let candidate = Path.Combine(dir, "appsettings.json")
                        if File.Exists(candidate) then Some candidate else None
                    else None
                | None ->
                    // 1. プロジェクトソース直下の appsettings.json（開発環境・リポジトリ内実行）
                    let findProjectJson () =
                        let rec findUp (dir: DirectoryInfo) (depth: int) =
                            if depth <= 0 || box dir = null then None
                            else
                                let candidate = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.json")
                                if File.Exists(candidate) then Some candidate
                                else findUp dir.Parent (depth - 1)
                        let current = DirectoryInfo(Directory.GetCurrentDirectory())
                        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                        findUp current 8 |> Option.orElseWith (fun () -> findUp baseDir 8)

                    // 2. カレントディレクトリ直下の appsettings.json
                    let findCurrentJson () =
                        let current = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
                        if File.Exists(current) then Some current else None

                    // 3. アプリケーション実行ディレクトリ直下の appsettings.json（バイナリ配布・ポータブル実行）
                    let findBaseJson () =
                        let baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json")
                        if File.Exists(baseDir) then Some baseDir else None

                    // 4. ユーザープロファイル領域（ローカルに一切存在しない場合のフォールバック）
                    let findAppDataJson () =
                        let appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager", "appsettings.json")
                        if File.Exists(appDataPath) then Some appDataPath else None

                    findProjectJson ()
                    |> Option.orElseWith findCurrentJson
                    |> Option.orElseWith findBaseJson
                    |> Option.orElseWith findAppDataJson

        match resolvedJsonPath with
        | Some path ->
            match load path with
            | Ok loadedJson ->
                // JSONに値があればJSON優先。ただしApiKeyがNoneなら.envのキーをフォールバック
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
            | Error _ ->
                interimSettings
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
