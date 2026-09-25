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
                                    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", key)
                                | _ -> ()

                                client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/TagBasedVideoManager")
                                client.DefaultRequestHeaders.Add("X-Title", "TagBasedVideoManager-Renamer")

                                use content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                                let! resp = client.PostAsync(endpointUrl, content) |> Async.AwaitTask
                                let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                                return int resp.StatusCode, body
                            }

                    if statusCode < 200 || statusCode >= 300 then
                        return Error (OpenRouterError (statusCode, $"OpenRouter API returned error status {statusCode}: {responseBody}"))
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
