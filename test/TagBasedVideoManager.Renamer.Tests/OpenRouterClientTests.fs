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
