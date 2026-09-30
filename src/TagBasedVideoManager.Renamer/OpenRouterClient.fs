namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open TagBasedVideoManager.Renamer

module OpenRouterClient =

    let private buildSystemPrompt () =
        "あなたはファイル名の短縮・正規化を行う専門AIです。\n" +
        "提供されたファイル一覧に対し、指定の命名規則に従って安全で簡潔なファイル名を生成してください。\n" +
        "出力は必ず指定されたJSONフォーマットのみを返し、余計な解説文やMarkdownタグを含めないでください。"

    let private buildUserPrompt (rule: NamingRule) (candidates: ScanCandidate list) =
        let sb = StringBuilder()
        sb.AppendLine($"【命名規則】: {rule.Name}") |> ignore
        sb.AppendLine($"【命名パターン】: {rule.Pattern}") |> ignore
        sb.AppendLine($"【命名指示】: {rule.PromptInstruction}") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("【ファイル一覧】:") |> ignore
        candidates
        |> List.iteri (fun idx c ->
            let lastModStr = c.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
            let parentFolder = Path.GetFileName(c.DirectoryPath)
            sb.AppendLine($"{idx + 1}. FullPath: \"{c.FullPath}\", FileName: \"{c.FileName}\", ParentFolder: \"{parentFolder}\", LastModified: \"{lastModStr}\"") |> ignore
        )
        sb.AppendLine() |> ignore
        sb.AppendLine("【出力JSON仕様】:") |> ignore
        sb.AppendLine("[") |> ignore
        sb.AppendLine("  {") |> ignore
        sb.AppendLine("    \"originalFileName\": \"元ファイル名\",") |> ignore
        sb.AppendLine("    \"proposedFileName\": \"新ファイル名.mp4\",") |> ignore
        sb.AppendLine("    \"aiComment\": \"問題点・補完理由（※規則通り命名できた場合は null または空文字にすること）\"") |> ignore
        sb.AppendLine("  }") |> ignore
        sb.AppendLine("]") |> ignore
        sb.ToString()

    let private cleanJsonContent (rawContent: string) =
        let trimmed = rawContent.Trim()
        let withoutPrefix =
            if trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase) then
                trimmed.Substring(7)
            elif trimmed.StartsWith("```") then
                trimmed.Substring(3)
            else
                trimmed

        let withoutSuffix =
            if withoutPrefix.EndsWith("```") then
                withoutPrefix.Substring(0, withoutPrefix.Length - 3)
            else
                withoutPrefix

        withoutSuffix.Trim()

    let private normalizeAiComment (rawComment: string option) : string option =
        match rawComment with
        | None -> None
        | Some c ->
            if String.IsNullOrWhiteSpace(c) then None
            elif String.Equals(c.Trim(), "null", StringComparison.OrdinalIgnoreCase) then None
            else Some (c.Trim())

    /// AIモデルへリネーム候補を問い合わせる
    let requestProposals
        (httpHandler: (string -> string -> Async<int * string>) option)
        (apiKey: string option)
        (model: string)
        (rule: NamingRule)
        (candidates: ScanCandidate list)
        : Async<Result<RenameProposal list, RenamerError>> =
        async {
            if List.isEmpty candidates then
                return Ok []
            else
                let messages = [
                    dict [ "role", box "system"; "content", box (buildSystemPrompt ()) ]
                    dict [ "role", box "user"; "content", box (buildUserPrompt rule candidates) ]
                ]
                let requestDict = dict [
                    "model", box model
                    "messages", box messages
                    "temperature", box 0.2
                ]

                let requestJson = JsonSerializer.Serialize(requestDict)
                let endpointUrl = "https://openrouter.ai/api/v1/chat/completions"

                try
                    let! statusCode, responseBody =
                        match httpHandler with
                        | Some mockHandler -> mockHandler endpointUrl requestJson
                        | None ->
                            async {
                                use client = new HttpClient()
                                client.Timeout <- TimeSpan.FromSeconds(60.0)
                                match apiKey with
                                | Some key when not (String.IsNullOrWhiteSpace(key)) ->
                                    let cleanKey = key.Trim().Trim('"', '\'')
                                    if not (String.IsNullOrWhiteSpace(cleanKey)) then
                                        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", cleanKey)
                                | _ -> ()

                                client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/TagBasedVideoManager")
                                client.DefaultRequestHeaders.Add("X-Title", "TagBasedVideoManager-Renamer")

                                use content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                                let! resp = client.PostAsync(endpointUrl, content) |> Async.AwaitTask
                                let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                                return int resp.StatusCode, body
                            }

                    if statusCode < 200 || statusCode >= 300 then
                        let detailMsg =
                            if statusCode = 401 then
                                $"OpenRouter API returned error status 401 (認証エラー): APIキーが無効または未設定です (Raw: {responseBody})。appsettings.json または .env の apiKey ('sk-or-v1-...') を確認してください。"
                            else
                                $"OpenRouter API returned error status {statusCode}: {responseBody}"
                        return Error (OpenRouterError (statusCode, detailMsg))
                    else
                        try
                            use rootDoc = JsonDocument.Parse(responseBody)
                            let choicesProp = rootDoc.RootElement.GetProperty("choices")
                            if choicesProp.GetArrayLength() = 0 then
                                return Error (OpenRouterError (statusCode, "OpenRouter API response contains no choices"))
                            else
                                let firstChoice = choicesProp.[0]
                                let messageProp = firstChoice.GetProperty("message")
                                let contentStr = messageProp.GetProperty("content").GetString()
                                let rawJson = cleanJsonContent contentStr

                                use proposalsDoc = JsonDocument.Parse(rawJson)
                                let elements =
                                    if proposalsDoc.RootElement.ValueKind = JsonValueKind.Array then
                                        proposalsDoc.RootElement.EnumerateArray() |> Seq.toList
                                    elif proposalsDoc.RootElement.ValueKind = JsonValueKind.Object && proposalsDoc.RootElement.TryGetProperty("proposals", ref Unchecked.defaultof<JsonElement>) then
                                        proposalsDoc.RootElement.GetProperty("proposals").EnumerateArray() |> Seq.toList
                                    else
                                        [ proposalsDoc.RootElement ]

                                let candidateMap =
                                    candidates
                                    |> List.map (fun c -> c.FileName, c)
                                    |> Map.ofList

                                let proposals =
                                    elements
                                    |> List.choose (fun el ->
                                        let origName =
                                            if el.TryGetProperty("originalFileName", ref Unchecked.defaultof<JsonElement>) then
                                                el.GetProperty("originalFileName").GetString()
                                            else ""
                                        let propName =
                                            if el.TryGetProperty("proposedFileName", ref Unchecked.defaultof<JsonElement>) then
                                                el.GetProperty("proposedFileName").GetString()
                                            else ""
                                        let comment =
                                            if el.TryGetProperty("aiComment", ref Unchecked.defaultof<JsonElement>) then
                                                let cEl = el.GetProperty("aiComment")
                                                if cEl.ValueKind = JsonValueKind.String then Some (cEl.GetString())
                                                else None
                                            else None

                                        let matchedCandidate =
                                            match Map.tryFind origName candidateMap with
                                            | Some c -> Some c
                                            | None ->
                                                candidates
                                                |> List.tryFind (fun c -> String.Equals(c.FileName, origName, StringComparison.OrdinalIgnoreCase))

                                        matchedCandidate
                                        |> Option.map (fun candidate ->
                                            let proposedPath = Path.Combine(candidate.DirectoryPath, propName)
                                            {
                                                OriginalFullPath = candidate.FullPath
                                                OriginalFileName = candidate.FileName
                                                DirectoryPath = candidate.DirectoryPath
                                                OriginalLength = candidate.PathLength
                                                ProposedFileName = propName
                                                ProposedLength = proposedPath.Length
                                                AiComment = normalizeAiComment comment
                                                IsSelected = true
                                                LastWriteTime = candidate.LastWriteTime
                                                IsAiProposed = true
                                                IsAiProcessing = false
                                            }
                                        )
                                    )

                                return Ok proposals
                        with
                        | ex ->
                            return Error (OpenRouterError (statusCode, $"OpenRouter レスポンスのJSONパースに失敗しました: {ex.Message} (Raw: {responseBody})"))
                with
                | ex ->
                    return Error (OpenRouterError (0, $"OpenRouter API 通信中に例外が発生しました: {ex.Message}"))
        }

    /// <summary>
    /// APIキー未設定時またはオフライン用の初期短縮ルール (未提案状態)
    /// AI提案前の手動編集初期値として利用される。勝手なAIコメントは付与しない。
    /// </summary>
    let generateLocalProposals (rule: NamingRule) (candidates: ScanCandidate list) : RenameProposal list =
        let dateRegex = System.Text.RegularExpressions.Regex(@"(\d{4})[-_.](\d{2})[-_.](\d{2})")
        candidates
        |> List.map (fun c ->
            let ext = Path.GetExtension(c.FileName)
            let baseWithoutExt = Path.GetFileNameWithoutExtension(c.FileName)
            let dateMatch = dateRegex.Match(baseWithoutExt)

            let datePrefix =
                if dateMatch.Success then
                    let y = dateMatch.Groups.[1].Value
                    let m = dateMatch.Groups.[2].Value
                    let d = dateMatch.Groups.[3].Value
                    $"{y}-{m}-{d}"
                else
                    c.LastWriteTime.ToString("yyyy-MM-dd")

            let cleanTitle =
                if dateMatch.Success then
                    baseWithoutExt.Remove(dateMatch.Index, dateMatch.Length).Trim('_', '-', ' ')
                else
                    baseWithoutExt.Trim('_', '-', ' ')

            let shortenedTitle =
                if String.IsNullOrWhiteSpace(cleanTitle) then "video"
                elif cleanTitle.Length > 30 then cleanTitle.Substring(0, 30).TrimEnd('_', '-')
                else cleanTitle

            let proposedName = $"{datePrefix}_{shortenedTitle}{ext}"
            let proposedPath = Path.Combine(c.DirectoryPath, proposedName)

            {
                OriginalFullPath = c.FullPath
                OriginalFileName = c.FileName
                DirectoryPath = c.DirectoryPath
                OriginalLength = c.PathLength
                ProposedFileName = c.FileName // 初期状態は元ファイル名（手動編集用）
                ProposedLength = c.PathLength
                AiComment = None              // 勝手な固定コメントを捏造しない
                IsSelected = true
                LastWriteTime = c.LastWriteTime
                IsAiProposed = false          // 未提案フラグ
                IsAiProcessing = false
            }
        )

    let private buildSingleSystemPrompt () =
        "あなたはファイル名の短縮・正規化を行う専門AIです。\n" +
        "対象ファイルに対し、指定された命名規則とWeb検索結果（提供されている場合）を参考に、安全で簡潔な新ファイル名および命名理由を提案してください。\n" +
        "正常に短縮できた場合も含め、なぜその短縮名にしたか、Web検索結果のどの情報を参考にしたかを「aiComment」に必ず日本語で簡潔に記述してください。\n" +
        "出力は必ず指定された単一JSONオブジェクトのみを返し、余計な解説文やMarkdownタグを含めないでください。"

    let private buildSingleUserPrompt (rule: NamingRule) (candidate: ScanCandidate) (searchResults: SearchResultItem list) =
        let sb = StringBuilder()
        sb.AppendLine($"【命名規則名】: {rule.Name}") |> ignore
        sb.AppendLine($"【命名パターン】: {rule.Pattern}") |> ignore
        sb.AppendLine($"【命名指示】: {rule.PromptInstruction}") |> ignore
        sb.AppendLine() |> ignore
        let parentFolder = Path.GetFileName(candidate.DirectoryPath)
        let lastModStr = candidate.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        sb.AppendLine("【対象ファイル情報】:") |> ignore
        sb.AppendLine($"- フルパス: \"{candidate.FullPath}\"") |> ignore
        sb.AppendLine($"- 現在のファイル名: \"{candidate.FileName}\"") |> ignore
        sb.AppendLine($"- 親フォルダ: \"{parentFolder}\"") |> ignore
        sb.AppendLine($"- 更新日時: \"{lastModStr}\"") |> ignore
        sb.AppendLine() |> ignore
        if not (List.isEmpty searchResults) then
            sb.AppendLine(WebSearchClient.formatSearchResultsForPrompt searchResults) |> ignore
            sb.AppendLine() |> ignore
        sb.AppendLine("【出力JSON仕様】:") |> ignore
        sb.AppendLine("{") |> ignore
        sb.AppendLine($"  \"originalFileName\": \"{candidate.FileName}\",") |> ignore
        sb.AppendLine("  \"proposedFileName\": \"新ファイル名.mp4\",") |> ignore
        sb.AppendLine("  \"aiComment\": \"命名意図・短縮根拠・Web検索結果の反映内容を簡潔に記述\"") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString()

    /// <summary>
    /// 単一ファイルに対してWeb検索結果をコンテキストに含めてリネーム候補を問い合わせる
    /// 正常時・例外時を問わずAIコメントを常時保持する
    /// </summary>
    let requestSingleProposal
        (httpHandler: (string -> string -> Async<int * string>) option)
        (apiKey: string option)
        (model: string)
        (rule: NamingRule)
        (candidate: ScanCandidate)
        (searchResults: SearchResultItem list)
        (cancellationToken: System.Threading.CancellationToken)
        : Async<Result<RenameProposal, RenamerError>> =
        async {
            let messages = [
                dict [ "role", box "system"; "content", box (buildSingleSystemPrompt ()) ]
                dict [ "role", box "user"; "content", box (buildSingleUserPrompt rule candidate searchResults) ]
            ]
            let requestDict = dict [
                "model", box model
                "messages", box messages
                "temperature", box 0.2
            ]

            let requestJson = JsonSerializer.Serialize(requestDict)
            let endpointUrl = "https://openrouter.ai/api/v1/chat/completions"

            try
                let! statusCode, responseBody =
                    match httpHandler with
                    | Some mockHandler -> mockHandler endpointUrl requestJson
                    | None ->
                        async {
                            use client = new HttpClient()
                            client.Timeout <- TimeSpan.FromSeconds(60.0)
                            match apiKey with
                            | Some key when not (String.IsNullOrWhiteSpace(key)) ->
                                let cleanKey = key.Trim().Trim('"', '\'')
                                if not (String.IsNullOrWhiteSpace(cleanKey)) then
                                    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", cleanKey)
                            | _ -> ()

                            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/TagBasedVideoManager")
                            client.DefaultRequestHeaders.Add("X-Title", "TagBasedVideoManager-Renamer")

                            use content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                            let! resp = client.PostAsync(endpointUrl, content, cancellationToken) |> Async.AwaitTask
                            let! body = resp.Content.ReadAsStringAsync(cancellationToken) |> Async.AwaitTask
                            return int resp.StatusCode, body
                        }

                if statusCode < 200 || statusCode >= 300 then
                    let detailMsg =
                        if statusCode = 401 then
                            $"OpenRouter API returned error status 401 (認証エラー): APIキーが無効または未設定です (Raw: {responseBody})。appsettings.json または .env の apiKey ('sk-or-v1-...') を確認してください。"
                        else
                            $"OpenRouter API returned error status {statusCode}: {responseBody}"
                    return Error (OpenRouterError (statusCode, detailMsg))
                else
                    try
                        use rootDoc = JsonDocument.Parse(responseBody)
                        let choicesProp = rootDoc.RootElement.GetProperty("choices")
                        if choicesProp.GetArrayLength() = 0 then
                            return Error (OpenRouterError (statusCode, "OpenRouter API response contains no choices"))
                        else
                            let firstChoice = choicesProp.[0]
                            let messageProp = firstChoice.GetProperty("message")
                            let contentStr = messageProp.GetProperty("content").GetString()
                            let rawJson = cleanJsonContent contentStr

                            use proposalsDoc = JsonDocument.Parse(rawJson)
                            let element =
                                if proposalsDoc.RootElement.ValueKind = JsonValueKind.Array && proposalsDoc.RootElement.GetArrayLength() > 0 then
                                    proposalsDoc.RootElement.[0]
                                elif proposalsDoc.RootElement.ValueKind = JsonValueKind.Object && proposalsDoc.RootElement.TryGetProperty("proposals", ref Unchecked.defaultof<JsonElement>) then
                                    let arr = proposalsDoc.RootElement.GetProperty("proposals")
                                    if arr.ValueKind = JsonValueKind.Array && arr.GetArrayLength() > 0 then arr.[0]
                                    else proposalsDoc.RootElement
                                else
                                    proposalsDoc.RootElement

                            let propName =
                                if element.TryGetProperty("proposedFileName", ref Unchecked.defaultof<JsonElement>) then
                                    element.GetProperty("proposedFileName").GetString()
                                else candidate.FileName

                            let rawComment =
                                if element.TryGetProperty("aiComment", ref Unchecked.defaultof<JsonElement>) then
                                    let cEl = element.GetProperty("aiComment")
                                    if cEl.ValueKind = JsonValueKind.String then Some (cEl.GetString())
                                    else None
                                else None

                            // AIコメント常時保持: 空やnullの場合はデフォルトコメントを補完
                            let comment =
                                match normalizeAiComment rawComment with
                                | Some c when not (String.IsNullOrWhiteSpace(c)) -> c
                                | _ ->
                                    if not (List.isEmpty searchResults) then
                                        "Web検索結果および命名規則を参考に短縮しました。"
                                    else
                                        "命名規則に従って短縮しました。"

                            let finalPropName =
                                if String.IsNullOrWhiteSpace(propName) then candidate.FileName
                                else propName.Trim()

                            let proposedPath = Path.Combine(candidate.DirectoryPath, finalPropName)
                            let proposal: RenameProposal = {
                                OriginalFullPath = candidate.FullPath
                                OriginalFileName = candidate.FileName
                                DirectoryPath = candidate.DirectoryPath
                                OriginalLength = candidate.PathLength
                                ProposedFileName = finalPropName
                                ProposedLength = proposedPath.Length
                                AiComment = Some comment
                                IsSelected = true
                                LastWriteTime = candidate.LastWriteTime
                                IsAiProposed = true
                                IsAiProcessing = false
                            }
                            return Ok proposal
                    with ex ->
                        return Error (OpenRouterError (statusCode, $"OpenRouter レスポンスのJSONパースに失敗しました: {ex.Message} (Raw: {responseBody})"))
            with
            | :? OperationCanceledException ->
                return Error (OpenRouterError (0, "Web検索/AI提案リクエストがキャンセルされました。"))
            | ex ->
                return Error (OpenRouterError (0, $"OpenRouter API 通信中に例外が発生しました: {ex.Message}"))
        }


