namespace TagBasedVideoManager.Renamer.Tests

open System
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module OpenRouterClientTests =

    let sampleRule: NamingRule = {
        Id = "rule-1"
        Name = "日付_場所_行動"
        Pattern = "{Date}_{Location}_{Activity}.mp4"
        PromptInstruction = "ファイル名から撮影日、撮影場所、行動を抽出しアンダースコア繋ぎで命名してください。"
        Order = 0
        EnableWebSearch = false
    }

    let sampleCandidate1: ScanCandidate = {
        FullPath = "C:\\Videos\\LongDir\\VeryLongFileName_20250812_Wakkanai_Touring.mp4"
        FileName = "VeryLongFileName_20250812_Wakkanai_Touring.mp4"
        DirectoryPath = "C:\\Videos\\LongDir"
        PathLength = 265
        FileSizeBytes = 1024L * 1024L
        LastWriteTime = DateTime(2025, 8, 12, 10, 0, 0)
    }

    let sampleCandidate2: ScanCandidate = {
        FullPath = "C:\\Videos\\LongDir\\7f8a9b0c-1234-5678-abcd-ef0123456789_odd_name.mp4"
        FileName = "7f8a9b0c-1234-5678-abcd-ef0123456789_odd_name.mp4"
        DirectoryPath = "C:\\Videos\\LongDir"
        PathLength = 250
        FileSizeBytes = 512L * 1024L
        LastWriteTime = DateTime(2025, 8, 15, 14, 30, 0)
    }

    let wrapInOpenRouterResponse (contentJson: string) =
        // OpenRouter chat completion JSON wrapper
        let escapedContent = contentJson.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")
        $"{{\"id\":\"gen-123\",\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":\"{escapedContent}\"}}}}]}}"

    [<Fact>]
    let ``requestProposals は問題のない命名において aiComment を None としてデシリアライズする`` () =
        async {
            let jsonContent = """
            [
              {
                "originalFileName": "VeryLongFileName_20250812_Wakkanai_Touring.mp4",
                "proposedFileName": "20250812_Wakkanai_Touring.mp4",
                "aiComment": ""
              }
            ]
            """
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse jsonContent }

            let! result = OpenRouterClient.requestProposals (Some mockHttp) (Some "mock-key") "meta-llama/llama-3.3-70b-instruct:free" sampleRule [ sampleCandidate1 ]
            match result with
            | Error err -> failwith $"requestProposals failed: {err}"
            | Ok proposals ->
                proposals.Length |> should equal 1
                let p = proposals.Head
                p.OriginalFileName |> should equal sampleCandidate1.FileName
                p.ProposedFileName |> should equal "20250812_Wakkanai_Touring.mp4"
                p.AiComment |> should equal None // 問題なし時は None
                p.IsSelected |> should equal true
                p.ProposedLength |> should be (lessThan p.OriginalLength)
        }

    [<Fact>]
    let ``requestProposals は元ファイル名に問題がありAIが補完した場合に aiComment を Some で格納する`` () =
        async {
            let jsonContent = """
            [
              {
                "originalFileName": "7f8a9b0c-1234-5678-abcd-ef0123456789_odd_name.mp4",
                "proposedFileName": "20250815_LongDir_Touring.mp4",
                "aiComment": "元ファイル名に日時・地名が含まれていないため、親フォルダ名および更新日時より補完しました。"
              }
            ]
            """
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse jsonContent }

            let! result = OpenRouterClient.requestProposals (Some mockHttp) (Some "mock-key") "meta-llama/llama-3.3-70b-instruct:free" sampleRule [ sampleCandidate2 ]
            match result with
            | Error err -> failwith $"requestProposals failed: {err}"
            | Ok proposals ->
                proposals.Length |> should equal 1
                let p = proposals.Head
                p.ProposedFileName |> should equal "20250815_LongDir_Touring.mp4"
                p.AiComment |> should equal (Some "元ファイル名に日時・地名が含まれていないため、親フォルダ名および更新日時より補完しました。")
        }

    [<Fact>]
    let ``requestProposals は Markdown コードブロックで囲まれた JSON レスポンスを自動トリムしてパースする`` () =
        async {
            let rawContentWithMarkdown = "```json\n[\n  {\n    \"originalFileName\": \"VeryLongFileName_20250812_Wakkanai_Touring.mp4\",\n    \"proposedFileName\": \"20250812_Wakkanai.mp4\",\n    \"aiComment\": null\n  }\n]\n```"
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse rawContentWithMarkdown }

            let! result = OpenRouterClient.requestProposals (Some mockHttp) (Some "mock-key") "meta-llama/llama-3.3-70b-instruct:free" sampleRule [ sampleCandidate1 ]
            match result with
            | Error err -> failwith $"requestProposals failed: {err}"
            | Ok proposals ->
                proposals.Length |> should equal 1
                proposals.Head.ProposedFileName |> should equal "20250812_Wakkanai.mp4"
                proposals.Head.AiComment |> should equal None
        }

    [<Fact>]
    let ``requestProposals は HTTPエラー時に OpenRouterError を返す`` () =
        async {
            let mockHttp _url _body =
                async { return 429, "{\"error\":{\"message\":\"Rate limit exceeded\"}}" }

            let! result = OpenRouterClient.requestProposals (Some mockHttp) (Some "mock-key") "meta-llama/llama-3.3-70b-instruct:free" sampleRule [ sampleCandidate1 ]
            match result with
            | Ok _ -> failwith "Expected OpenRouterError"
            | Error (OpenRouterError (code, msg)) ->
                code |> should equal 429
                msg |> should contain "Rate limit exceeded"
            | Error other -> failwith $"Unexpected error: {other}"
        }

    [<Fact>]
    let ``requestProposals は候補が空リストの場合に HTTPリクエストを行わず Ok [] を返す`` () =
        async {
            let mutable httpCalled = false
            let mockHttp _url _body =
                async {
                    httpCalled <- true
                    return 200, "{}"
                }

            let! result = OpenRouterClient.requestProposals (Some mockHttp) (Some "mock-key") "meta-llama/llama-3.3-70b-instruct:free" sampleRule []
            match result with
            | Error err -> failwith $"Failed: {err}"
            | Ok proposals ->
                proposals |> should be Empty
                httpCalled |> should equal false
        }

    let webSearchRule: NamingRule = {
        Id = "rule-web"
        Name = "ドラマ・アニメ公式短縮"
        Pattern = "{Title} S{Season}E{Episode}.mp4"
        PromptInstruction = "Web検索スニペットを参考に正式作品名と話数を特定し短縮してください。"
        Order = 0
        EnableWebSearch = true
    }

    let sampleWebResult: SearchResultItem = {
        Title = "Sample Drama Series - Official Site"
        Snippet = "The hit mystery drama series 'Sample Drama' Episode 01 aired in 2026."
        Url = "https://example.com/sample-drama"
    }

    [<Fact>]
    let ``requestSingleProposal は単一ファイル情報とWeb検索結果をコンテキストに含めてリクエストし正常時もaiCommentをSomeで保持する`` () =
        async {
            let jsonContent = """
            {
              "originalFileName": "VeryLongFileName_20250812_Wakkanai_Touring.mp4",
              "proposedFileName": "Sample Drama S01E01.mp4",
              "aiComment": "ddgs検索結果より公式タイトル「Sample Drama」を特定して短縮しました。"
            }
            """
            let mutable interceptedBody = ""
            let mockHttp _url body =
                async {
                    interceptedBody <- body
                    return 200, wrapInOpenRouterResponse jsonContent
                }

            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result =
                OpenRouterClient.requestSingleProposal
                    (Some mockHttp)
                    (Some "mock-key")
                    "meta-llama/llama-3.3-70b-instruct:free"
                    webSearchRule
                    sampleCandidate1
                    [ sampleWebResult ]
                    cts.Token

            match result with
            | Error err -> failwith $"requestSingleProposal failed: {err}"
            | Ok proposal ->
                proposal.OriginalFileName |> should equal sampleCandidate1.FileName
                proposal.ProposedFileName |> should equal "Sample Drama S01E01.mp4"
                proposal.AiComment |> should equal (Some "ddgs検索結果より公式タイトル「Sample Drama」を特定して短縮しました。")
                proposal.IsAiProposed |> should equal true
                proposal.IsSelected |> should equal true
                interceptedBody.Contains("Sample Drama Series - Official Site") |> should equal true
                interceptedBody.Contains("https://example.com/sample-drama") |> should equal true
        }

    [<Fact>]
    let ``requestSingleProposal はWeb検索結果が空でも安全に提案を取得しAIコメントを保持する`` () =
        async {
            let jsonContent = """
            {
              "originalFileName": "VeryLongFileName_20250812_Wakkanai_Touring.mp4",
              "proposedFileName": "20250812_Wakkanai_Touring.mp4",
              "aiComment": "日付と地名を抽出して短縮しました。"
            }
            """
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse jsonContent }

            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result =
                OpenRouterClient.requestSingleProposal
                    (Some mockHttp)
                    (Some "mock-key")
                    "meta-llama/llama-3.3-70b-instruct:free"
                    sampleRule
                    sampleCandidate1
                    []
                    cts.Token

            match result with
            | Error err -> failwith $"requestSingleProposal failed: {err}"
            | Ok proposal ->
                proposal.ProposedFileName |> should equal "20250812_Wakkanai_Touring.mp4"
                proposal.AiComment |> should equal (Some "日付と地名を抽出して短縮しました。")
        }

    [<Fact>]
    let ``requestSingleProposal はLLMがaiCommentを返さない場合でもデフォルトコメントで補完し常時Someとする`` () =
        async {
            let jsonContent = """
            {
              "originalFileName": "VeryLongFileName_20250812_Wakkanai_Touring.mp4",
              "proposedFileName": "Shortened_Sample.mp4",
              "aiComment": ""
            }
            """
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse jsonContent }

            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result =
                OpenRouterClient.requestSingleProposal
                    (Some mockHttp)
                    (Some "mock-key")
                    "meta-llama/llama-3.3-70b-instruct:free"
                    sampleRule
                    sampleCandidate1
                    []
                    cts.Token

            match result with
            | Error err -> failwith $"requestSingleProposal failed: {err}"
            | Ok proposal ->
                proposal.ProposedFileName |> should equal "Shortened_Sample.mp4"
                proposal.AiComment |> should not' (equal None)
                proposal.AiComment.Value |> should not' (be EmptyString)
        }

    [<Fact>]
    let ``requestSingleProposal は配列形式のレスポンスでもパースできる`` () =
        async {
            let jsonContent = """
            [
              {
                "originalFileName": "VeryLongFileName_20250812_Wakkanai_Touring.mp4",
                "proposedFileName": "Shortened_From_Array.mp4",
                "aiComment": "配列形式で返却された提案です。"
              }
            ]
            """
            let mockHttp _url _body =
                async { return 200, wrapInOpenRouterResponse jsonContent }

            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result =
                OpenRouterClient.requestSingleProposal
                    (Some mockHttp)
                    (Some "mock-key")
                    "meta-llama/llama-3.3-70b-instruct:free"
                    sampleRule
                    sampleCandidate1
                    []
                    cts.Token

            match result with
            | Error err -> failwith $"requestSingleProposal failed: {err}"
            | Ok proposal ->
                proposal.ProposedFileName |> should equal "Shortened_From_Array.mp4"
                proposal.AiComment |> should equal (Some "配列形式で返却された提案です。")
        }

    [<Fact>]
    let ``requestSingleProposal はHTTPステータスエラー時に安全にErrorを返す`` () =
        async {
            let mockHttp _url _body =
                async { return 401, """{"error": {"message": "Invalid API key"}}""" }

            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result =
                OpenRouterClient.requestSingleProposal
                    (Some mockHttp)
                    (Some "bad-key")
                    "meta-llama/llama-3.3-70b-instruct:free"
                    sampleRule
                    sampleCandidate1
                    []
                    cts.Token

            match result with
            | Ok _ -> failwith "Expected Error but got Ok"
            | Error (OpenRouterError (status, msg)) ->
                status |> should equal 401
                msg.Contains("401") |> should equal true
            | Error other ->
                failwith $"Unexpected error type: {other}"
        }
