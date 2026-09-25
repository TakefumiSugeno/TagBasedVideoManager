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
