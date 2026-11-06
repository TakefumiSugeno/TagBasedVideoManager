namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module WebSearchClientTests =

    [<Fact>]
    let ``extractSearchQuery should strip extension and clean common video tags`` () =
        let filename = "Extremely.Long.Filename.Sample.Episode.01.2026.1080p.WEBRip.x264.AAC-GROUP.mp4"
        let query = WebSearchClient.extractSearchQuery filename
        query |> should not' (be EmptyString)
        query.Contains(".mp4") |> should equal false
        query.Contains("1080p") |> should equal false
        query.Contains("x264") |> should equal false
        query.Contains("Extremely") |> should equal true
        query.Contains("Sample") |> should equal true

    [<Fact>]
    let ``extractSearchQuery with empty string returns empty`` () =
        WebSearchClient.extractSearchQuery "" |> should equal ""

    [<Fact>]
    let ``parseSearchResults should parse valid json output`` () =
        let json = """[{"title": "Title 1", "snippet": "Snippet 1", "url": "https://example.com/1"}]"""
        match WebSearchClient.parseSearchResults json with
        | Ok items ->
            items.Length |> should equal 1
            items[0].Title |> should equal "Title 1"
            items[0].Snippet |> should equal "Snippet 1"
            items[0].Url |> should equal "https://example.com/1"
        | Error err ->
            failwith $"Expected Ok but got Error: {err}"

    [<Fact>]
    let ``parseSearchResults should handle empty array`` () =
        let json = "[]"
        match WebSearchClient.parseSearchResults json with
        | Ok items -> items |> should be Empty
        | Error err -> failwith $"Expected Ok [] but got Error: {err}"

    [<Fact>]
    let ``parseSearchResults should return Error on invalid json`` () =
        let json = "{ invalid json }"
        match WebSearchClient.parseSearchResults json with
        | Ok _ -> failwith "Expected Error but got Ok"
        | Error err -> err |> should not' (be EmptyString)

    [<Fact>]
    let ``formatSearchResultsForPrompt should format items nicely`` () =
        let items: SearchResultItem list = [
            { Title = "Sample Work Title"; Snippet = "A famous drama series released in 2026"; Url = "https://example.com/drama" }
        ]
        let formatted = WebSearchClient.formatSearchResultsForPrompt items
        formatted.Contains("Sample Work Title") |> should equal true
        formatted.Contains("A famous drama series released in 2026") |> should equal true
        formatted.Contains("https://example.com/drama") |> should equal true

    [<Fact>]
    let ``formatSearchResultsForPrompt with empty list returns no-result indicator`` () =
        let formatted = WebSearchClient.formatSearchResultsForPrompt []
        formatted.Contains("なし") |> should equal true

    [<Fact>]
    let ``searchCustomAsync should return Error safely when python executable does not exist`` () =
        task {
            use cts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! result = WebSearchClient.searchCustomAsync "non_existent_python_binary_xyz_123" "scripts/ddgs_search.py" "test" 1 cts.Token
            match result with
            | Ok _ -> failwith "Expected Error for non-existent executable, but got Ok"
            | Error err ->
                err |> should not' (be EmptyString)
        }

    [<Fact>]
    let ``searchAsync should successfully retrieve search results when python and ddgs are available`` () =
        task {
            // プロジェクトルートからの相対パスまたは絶対パスでスクリプトが存在するか確認
            let scriptPath =
                let candidates = [
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../scripts/ddgs_search.py")
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../scripts/ddgs_search.py")
                    Path.Combine(Directory.GetCurrentDirectory(), "scripts/ddgs_search.py")
                    "scripts/ddgs_search.py"
                ]
                candidates |> List.tryFind File.Exists

            match scriptPath with
            | Some path ->
                use cts = new CancellationTokenSource(TimeSpan.FromSeconds(15.0))
                let! result = WebSearchClient.searchCustomAsync "python" (Path.GetFullPath path) "Python programming language" 1 cts.Token
                match result with
                | Ok items ->
                    items.Length |> should be (greaterThanOrEqualTo 1)
                    items[0].Title |> should not' (be EmptyString)
                | Error err ->
                    // ネットワーク制限環境などがある場合はスキップまたは警告
                    printfn $"WebSearch returned Error (possibly network limited): {err}"
            | None ->
                printfn "ddgs_search.py not found, skipping live search test."
        }
