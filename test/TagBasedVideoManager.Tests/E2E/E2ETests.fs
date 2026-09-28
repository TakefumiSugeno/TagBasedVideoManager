namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Builder
open Microsoft.Playwright
open Xunit
open FsUnit
open Microsoft.Extensions.DependencyInjection
open TagBasedVideoManager

type JSRange = {
    StartOffset: int
    EndOffset: int
    Count: int
}

type JSProfile = {
    Url: string
    Source: string
    Ranges: JSRange[]
}

type E2ETests (output: ITestOutputHelper) =
    
    static let coverageProfiles = System.Collections.Concurrent.ConcurrentBag<JSProfile>()



    
    // 空いているランダムなポートを割り当てるヘルパー
    let getFreePort () =
        let listener = System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        listener.Start()
        let port = (listener.LocalEndpoint :?> System.Net.IPEndPoint).Port
        listener.Stop()
        port

    static let rec findProjectRoot (dir: DirectoryInfo) =
        if dir = null then AppDomain.CurrentDomain.BaseDirectory
        else
            let candidateSrc = Path.Combine(dir.FullName, "src", "TagBasedVideoManager")
            if Directory.Exists(candidateSrc) then dir.FullName
            elif dir.Parent <> null then findProjectRoot dir.Parent
            else dir.FullName

    // テスト用の共通サーバー・ブラウザ起動処理
    let startTestServer dbPath =
        let port = getFreePort ()
        let url = $"http://localhost:{port}"
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let projectRoot = findProjectRoot (DirectoryInfo(baseDir))
        let contentRoot = Path.Combine(projectRoot, "src", "TagBasedVideoManager")
        let webRoot = Path.Combine(contentRoot, "wwwroot")

        // 他の並行実行テストクラスとの環境変数 DATABASE_PATH の競合を防ぐため、
        // DIコンテナに登録される IDbConnection をこのテストインスタンスの dbPath で直接オーバーライドする。
        let host =
            Microsoft.AspNetCore.WebHost.CreateDefaultBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .UseUrls(url)
                .ConfigureServices(fun services ->
                    Program.configureServices services
                    // 後からAddしたものが優先解決されるため、環境変数依存の登録を上書きする
                    services.AddTransient<System.Data.IDbConnection>(fun _ -> 
                        new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}")
                    ) |> ignore
                )
                .Configure(Program.configureApp)
                .Build()
        host, url

    static member Log(msg: string) =
        try
            let resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestResults")
            if not (Directory.Exists(resultsDir)) then Directory.CreateDirectory(resultsDir) |> ignore
            let logPath = Path.Combine(resultsDir, "cdp_debug.log")
            File.AppendAllText(logPath, sprintf "%s - %s\n" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")) msg)
        with _ -> ()

    static member AddCoverageData(url: string, source: string, ranges: JSRange[]) =
        if ranges <> null && ranges.Length > 0 then
            coverageProfiles.Add({ Url = url; Source = source; Ranges = ranges })
            E2ETests.GenerateFrontendCoverageReport()

    static member ParseAndAddCoverage(json: System.Text.Json.JsonElement) =
        try
            let hasResult, resultArr = json.TryGetProperty("result")
            if hasResult && resultArr.ValueKind = System.Text.Json.JsonValueKind.Array then
                for item in resultArr.EnumerateArray() do
                    let hasUrl, urlProp = item.TryGetProperty("url")
                    let url = if hasUrl then urlProp.GetString() else ""
                    if not (String.IsNullOrEmpty(url)) then
                        E2ETests.Log(sprintf "[CDP COV URL]: %s" url)
                    if url.Contains("app.js") then
                        let currentDir = Directory.GetCurrentDirectory()
                        let projectRoot = findProjectRoot (DirectoryInfo(AppContext.BaseDirectory))
                        let appJsPath = 
                            let p1 = Path.Combine(currentDir, "..", "..", "..", "..", "src", "TagBasedVideoManager", "wwwroot", "app.js")
                            let p2 = Path.Combine(currentDir, "..", "src", "TagBasedVideoManager", "wwwroot", "app.js")
                            let p3 = Path.Combine(projectRoot, "src", "TagBasedVideoManager", "wwwroot", "app.js")
                            if File.Exists(p3) then p3 elif File.Exists(p2) then p2 else p1
                        E2ETests.Log(sprintf "[CDP PATH CHECK]: appJsPath = %s (Exists: %b)" appJsPath (File.Exists(appJsPath)))
                        let sourceCode = if File.Exists(appJsPath) then File.ReadAllText(appJsPath) else ""
                        
                        let rangesList = System.Collections.Generic.List<JSRange>()
                        let hasFuncs, funcsArr = item.TryGetProperty("functions")
                        if hasFuncs && funcsArr.ValueKind = System.Text.Json.JsonValueKind.Array then
                            for func in funcsArr.EnumerateArray() do
                                let hasRanges, rangesArr = func.TryGetProperty("ranges")
                                if hasRanges && rangesArr.ValueKind = System.Text.Json.JsonValueKind.Array then
                                    for r in rangesArr.EnumerateArray() do
                                        let startOfs = r.GetProperty("startOffset").GetInt32()
                                        let endOfs = r.GetProperty("endOffset").GetInt32()
                                        let count = r.GetProperty("count").GetInt32()
                                        rangesList.Add({ StartOffset = startOfs; EndOffset = endOfs; Count = count })
                        
                        E2ETests.Log(sprintf "[CDP COV FOUND]: %s with %d ranges" url rangesList.Count)
                        E2ETests.AddCoverageData(url, sourceCode, rangesList.ToArray())
        with ex ->
            E2ETests.Log(sprintf "[CDP COV ERROR]: %s" (ex.ToString()))

    static member GenerateFrontendCoverageReport() =
        try
            let current = Directory.GetCurrentDirectory()
            let resultsDir = 
                let d1 = Path.Combine(current, "TestResults")
                let d2 = Path.Combine(current, "..", "..", "..", "TestResults")
                if Directory.Exists(d2) then d2 else d1
            E2ETests.Log(sprintf "[CDP REPORT]: resultsDir = %s" resultsDir)
            if Directory.Exists(resultsDir) then
                let subDirs = Directory.GetDirectories(resultsDir, "TestRun_*")
                if subDirs.Length > 0 then
                    let latestDir = subDirs |> Array.sortByDescending Directory.GetCreationTime |> Array.head
                    let outputReportDir = Path.Combine(latestDir, "CoverageReport_Frontend")
                    Directory.CreateDirectory(outputReportDir) |> ignore

                    let profilesList = coverageProfiles.ToArray()
                    E2ETests.Log(sprintf "[CDP REPORT]: Profiles count = %d" profilesList.Length)
                    if profilesList.Length > 0 then
                        let sourceCode = profilesList.[0].Source
                        let sourceLength = sourceCode.Length
                        let executed = Array.create sourceLength false

                        let allRanges = 
                            profilesList
                            |> Array.collect (fun p -> p.Ranges)
                            |> Array.sortByDescending (fun r -> r.EndOffset - r.StartOffset)

                        for r in allRanges do
                            let startIdx = max 0 r.StartOffset
                            let endIdx = min sourceLength r.EndOffset
                            if startIdx < endIdx then
                                let isExecuted = r.Count > 0
                                for i in startIdx .. (endIdx - 1) do
                                    executed.[i] <- isExecuted

                        let lines = sourceCode.Replace("\r\n", "\n").Split('\n')
                        let mutable currentOffset = 0
                        let mutable coveredCount = 0
                        let mutable coverableCount = 0

                        let lineData = 
                            lines 
                            |> Array.mapi (fun idx lineText ->
                                let lineLen = lineText.Length
                                let startOfs = currentOffset
                                let endOfs = startOfs + lineLen
                                currentOffset <- endOfs + 1

                                let nonWhitespaceIndices = 
                                    [startOfs .. (endOfs - 1)] 
                                    |> List.filter (fun i -> 
                                        i < sourceLength && 
                                        not (Char.IsWhiteSpace(sourceCode.[i]))
                                    )

                                if nonWhitespaceIndices.Length = 0 then
                                    idx + 1, lineText, "ignored", 0.0
                                else
                                    let coveredChars = nonWhitespaceIndices |> List.filter (fun i -> executed.[i])
                                    let ratio = float coveredChars.Length / float nonWhitespaceIndices.Length
                                    let status = 
                                        if ratio >= 1.0 then "covered-full"
                                        elif ratio > 0.0 then "covered-partial"
                                        else "covered-none"

                                    if ratio >= 1.0 then coveredCount <- coveredCount + 1
                                    coverableCount <- coverableCount + 1
                                    idx + 1, lineText, status, ratio
                            )

                        let overallRate = 
                            if coverableCount > 0 then (float coveredCount / float coverableCount) * 100.0
                            else 0.0

                        let htmlBuilder = System.Text.StringBuilder()
                        htmlBuilder.AppendLine("<!DOCTYPE html>") |> ignore
                        htmlBuilder.AppendLine("<html><head>") |> ignore
                        htmlBuilder.AppendLine("<meta charset='utf-8' />") |> ignore
                        htmlBuilder.AppendLine("<title>Frontend Coverage Report - app.js</title>") |> ignore
                        htmlBuilder.AppendLine("<style>") |> ignore
                        htmlBuilder.AppendLine("body { font-family: sans-serif; background: #0f172a; color: #cbd5e1; margin: 0; padding: 20px; }") |> ignore
                        htmlBuilder.AppendLine(".container { max-width: 1200px; margin: 0 auto; background: #1e293b; padding: 20px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); }") |> ignore
                        htmlBuilder.AppendLine("h1 { color: #f8fafc; margin-top: 0; }") |> ignore
                        htmlBuilder.AppendLine(".summary-box { display: flex; gap: 20px; margin-bottom: 20px; background: #0f172a; padding: 15px; border-radius: 6px; }") |> ignore
                        htmlBuilder.AppendLine(".metric { flex: 1; text-align: center; }") |> ignore
                        htmlBuilder.AppendLine(".metric-value { font-size: 24px; font-weight: bold; color: #38bdf8; }") |> ignore
                        htmlBuilder.AppendLine("pre { background: #0f172a; padding: 15px; border-radius: 6px; overflow-x: auto; font-family: monospace; font-size: 13px; line-height: 1.5; }") |> ignore
                        htmlBuilder.AppendLine(".line { display: flex; }") |> ignore
                        htmlBuilder.AppendLine(".ln { width: 50px; color: #64748b; text-align: right; padding-right: 15px; user-select: none; border-right: 1px solid #334155; margin-right: 15px; }") |> ignore
                        htmlBuilder.AppendLine(".code-text { flex: 1; white-space: pre; }") |> ignore
                        htmlBuilder.AppendLine(".covered-full { background: rgba(34, 197, 94, 0.15); border-left: 3px solid #22c55e; }") |> ignore
                        htmlBuilder.AppendLine(".covered-partial { background: rgba(234, 179, 8, 0.15); border-left: 3px solid #eab308; }") |> ignore
                        htmlBuilder.AppendLine(".covered-none { background: rgba(239, 68, 68, 0.15); border-left: 3px solid #ef4444; }") |> ignore
                        htmlBuilder.AppendLine("</style></head><body>") |> ignore
                        htmlBuilder.AppendLine("<div class='container'>") |> ignore
                        htmlBuilder.AppendLine("<h1>Frontend Code Coverage Report (app.js)</h1>") |> ignore
                        htmlBuilder.AppendFormat("<div class='summary-box'><div class='metric'><div class='metric-value'>{0:F1}%</div><div>Line Coverage</div></div>", overallRate) |> ignore
                        htmlBuilder.AppendFormat("<div class='metric'><div class='metric-value'>{0} / {1}</div><div>Covered / Coverable Lines</div></div></div>", coveredCount, coverableCount) |> ignore
                        htmlBuilder.AppendLine("<pre>") |> ignore

                        for (lineNum, text, status, _) in lineData do
                            let escapedText = System.Net.WebUtility.HtmlEncode(text)
                            htmlBuilder.AppendFormat("<div class='line {0}'><span class='ln'>{1}</span><span class='code-text'>{2}</span></div>", status, lineNum, escapedText) |> ignore

                        htmlBuilder.AppendLine("</pre></div></body></html>") |> ignore

                        File.WriteAllText(Path.Combine(outputReportDir, "index.html"), htmlBuilder.ToString())
        with ex ->
            E2ETests.Log(sprintf "[CDP REPORT ERROR]: %s" (ex.ToString()))

    [<Fact>]
    member _.``E2E: サイドバーの表示非表示トグル and 検索フィルタクリアボタンが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_ui_{Guid.NewGuid():N}.db")
            let dummyVideoFile = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile, [| 0uy; 0uy; 0uy; 0uy |])
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                let dummyVideo = {
                    TagBasedVideoManager.Domain.Video.Id = "v_dummy"
                    TagBasedVideoManager.Domain.Video.FileName = "Dummy.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile
                    TagBasedVideoManager.Domain.Video.Duration = 10L
                    TagBasedVideoManager.Domain.Video.FileSize = 1000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn dummyVideo |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.Response.AddHandler(fun _ response ->
                    task {
                        try
                            if response.Status = 500 then
                                let! body = response.TextAsync()
                                output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                        with ex ->
                            output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                    } |> ignore
                )
                let! _ = page.GotoAsync(url)
                
                // 【検証1】サイドバー開閉
                let sidebar = page.Locator("aside")
                let! (isSidebarVisibleBefore: bool) = sidebar.IsVisibleAsync()
                isSidebarVisibleBefore |> should equal true

                let toggleButton = page.Locator("button[title='サイドバーの表示・非表示']")
                do! toggleButton.ClickAsync()
                
                do! sidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let! (isSidebarVisibleAfter: bool) = sidebar.IsVisibleAsync()
                isSidebarVisibleAfter |> should equal false

                do! toggleButton.ClickAsync()
                do! sidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isSidebarVisibleFinal: bool) = sidebar.IsVisibleAsync()
                isSidebarVisibleFinal |> should equal true

                // 【検証2】検索フィルタおよびクリアボタンの連動
                let searchInput = page.Locator("input[placeholder*='tag:']")
                do! searchInput.FillAsync("test_query")
                do! searchInput.PressAsync("Enter")
                
                let clearTextButton = page.Locator("button:has-text('クリア')").First
                do! clearTextButton.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                do! clearTextButton.ClickAsync()
                
                let! _ = page.WaitForFunctionAsync("document.querySelector('input[placeholder*=\"tag:\"]').value === ''")
                let! (inputValue: string) = searchInput.InputValueAsync()
                inputValue |> should equal ""

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile) then
                    try File.Delete(dummyVideoFile) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 動画のお気に入りトグルと複数選択・一括操作およびタグ手動付与が正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_click_{Guid.NewGuid():N}.db")
            
            let dummyVideoFile1 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_1.mp4")
            let dummyVideoFile2 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_2.mp4")
            File.WriteAllBytes(dummyVideoFile1, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile2, [| 0uy; 0uy; 0uy; 0uy |])

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                let tagTouring = { TagBasedVideoManager.Domain.Tag.Id = "t_touring"; TagBasedVideoManager.Domain.Tag.Name = "ツーリング"; TagBasedVideoManager.Domain.Tag.ColorCode = "#00ff00"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTouring |> Async.RunSynchronously
                
                let video1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test1"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo1.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile1
                    TagBasedVideoManager.Domain.Video.Duration = 60L
                    TagBasedVideoManager.Domain.Video.FileSize = 1024000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let video2 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test2"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo2.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile2
                    TagBasedVideoManager.Domain.Video.Duration = 120L
                    TagBasedVideoManager.Domain.Video.FileSize = 2048000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video2 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.Response.AddHandler(fun _ response ->
                    task {
                        try
                            if response.Status = 500 then
                                let! body = response.TextAsync()
                                output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                        with ex ->
                            output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                    } |> ignore
                )
                let! _ = page.GotoAsync(url)

                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1)
                let favButton = gridContainer.Locator("button[title='お気に入り']").First
                do! favButton.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 1. お気に入りトグルの検証
                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1)
                let favButton = gridContainer.Locator("button[title='お気に入り']").First
                do! favButton.ClickAsync()

                let activeFavButton = gridContainer.Locator("button[title='お気に入り'].text-yellow-400").First
                do! activeFavButton.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 2. 複数選択と一括操作の検証
                let checkboxes = gridContainer.Locator("input[x-model='selectedVideoIds']")
                let! (count: int) = checkboxes.CountAsync()
                count |> should equal 2

                do! checkboxes.Nth(0).ClickAsync()
                do! checkboxes.Nth(1).ClickAsync()
                
                let batchBar = page.Locator("div:has-text('件選択中')")
                do! batchBar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                let batchFavButton = page.Locator("div.fixed.bottom-6 button:has-text('お気に入り')")
                do! batchFavButton.ClickAsync()

                let activeFavButtons = gridContainer.Locator("button[title='お気に入り'].text-yellow-400")
                do! activeFavButtons.Nth(1).WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 3. 手動タグ付与モーダルの検証
                let addTagButton = gridContainer.Locator("button:has-text('タグ')").First
                do! addTagButton.ClickAsync()

                let tagModal = page.Locator("h3:has-text('タグの付与')")
                do! tagModal.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                let tagModalContainer = page.Locator("div[x-show='videoTagModal.show']")
                let checkboxInRow = tagModalContainer.Locator("input[type='checkbox']").First
                do! checkboxInRow.CheckAsync()

                let closeBtn = page.Locator("div[x-show='videoTagModal.show'] button:has-text('閉じる')")
                do! closeBtn.ClickAsync()

                let tagBadge = gridContainer.Locator("span:has-text('ツーリング')").First
                do! tagBadge.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile1) then
                    try File.Delete(dummyVideoFile1) with | _ -> ()
                if File.Exists(dummyVideoFile2) then
                    try File.Delete(dummyVideoFile2) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: ヘッダーとサイドバーの表示切替・フィルタリングイベントが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_header_sidebar_{Guid.NewGuid():N}.db")
            
            let dummyVideoFile1 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_1.mp4")
            let dummyVideoFile2 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_2.mp4")
            File.WriteAllBytes(dummyVideoFile1, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile2, [| 0uy; 0uy; 0uy; 0uy |])

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                let tagTouring = { TagBasedVideoManager.Domain.Tag.Id = "t_touring"; TagBasedVideoManager.Domain.Tag.Name = "ツーリング"; TagBasedVideoManager.Domain.Tag.ColorCode = "#00ff00"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTouring |> Async.RunSynchronously
                
                let video1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test1"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo1.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile1
                    TagBasedVideoManager.Domain.Video.Duration = 60L
                    TagBasedVideoManager.Domain.Video.FileSize = 1024000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = true // お気に入り
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let video2 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test2"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo2.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile2
                    TagBasedVideoManager.Domain.Video.Duration = 120L
                    TagBasedVideoManager.Domain.Video.FileSize = 2048000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video2 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.Response.AddHandler(fun _ response ->
                    task {
                        try
                            if response.Status = 500 then
                                let! body = response.TextAsync()
                                output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                        with ex ->
                            output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                    } |> ignore
                )
                let! _ = page.GotoAsync(url)
                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1) // 2つ目の動画グリッドコンテナ
                let listContainer = page.Locator("div[x-show=\"viewMode === 'list'\"]")
                let favButton = gridContainer.Locator("button[title='お気に入り']").First
                do! favButton.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 1. 【ヘッダー】グリッド／リスト表示モード of 切替
                
                // リスト表示ボタンをクリック
                let listModeBtn = page.Locator("button[title='リスト表示']")
                do! listModeBtn.ClickAsync()
                do! listContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                
                let! isListVisible = listContainer.IsVisibleAsync()
                let! isGridVisible = gridContainer.IsVisibleAsync()
                isListVisible |> should equal true
                isGridVisible |> should equal false

                // グリッド表示ボタンをクリックして戻す
                let gridModeBtn = page.Locator("button[title='グリッド表示']")
                do! gridModeBtn.ClickAsync()
                do! gridContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                
                let! isGridVisible2 = gridContainer.IsVisibleAsync()
                isGridVisible2 |> should equal true

                // 2. 【ヘッダー】同期（フォルダスキャン）ボタンクリック
                let syncBtn = page.Locator("button:has-text('同期')")
                let scanResponseTask = page.WaitForResponseAsync("**/api/scan")
                do! syncBtn.ClickAsync()
                let! _ = scanResponseTask

                // 3. 【サイドバー】「お気に入り」フィルタークリック
                let favFilterBtn = page.Locator("button:has-text('お気に入り')").First
                do! favFilterBtn.ClickAsync()
                let! _ = page.WaitForFunctionAsync("document.querySelectorAll('div[x-show=\"viewMode === \\'grid\\'\"]')[1].querySelectorAll('.group').length === 1")

                // お気に入りの動画（v_test1）のみが表示されているか確認（グリッド上の動画数が1件になる）
                let visibleVideoCards = gridContainer.Locator(".group")
                let! (visibleCount: int) = visibleVideoCards.CountAsync()
                visibleCount |> should equal 1

                // お気に入りフィルターを解除
                do! favFilterBtn.ClickAsync()
                let! _ = page.WaitForFunctionAsync("document.querySelectorAll('div[x-show=\"viewMode === \\'grid\\'\"]')[1].querySelectorAll('.group').length === 2")
                let! (visibleCountAfterReset: int) = visibleVideoCards.CountAsync()
                visibleCountAfterReset |> should equal 2

                // 4. 【サイドバー】タグクリック（検索窓への挿入とフィルタリング）
                let tagItem = page.Locator("aside span[class*='cursor-pointer']:has-text('ツーリング')").First
                let! _ = tagItem.EvaluateAsync("node => node.click()")
                let! _ = page.WaitForFunctionAsync("document.querySelector('input[placeholder*=\"tag:\"]').value.includes('tag:ツーリング')")

                // 検索窓に "tag:ツーリング" が挿入されていることを確認
                let searchInput = page.Locator("input[placeholder*='tag:']")
                let! (inputValue: string) = searchInput.InputValueAsync()
                inputValue.Contains("tag:ツーリング") |> should equal true

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile1) then
                    try File.Delete(dummyVideoFile1) with | _ -> ()
                if File.Exists(dummyVideoFile2) then
                    try File.Delete(dummyVideoFile2) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: タグ作成モーダルと自動適用ルール作成モーダルのイベントが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_modal_crud_{Guid.NewGuid():N}.db")
            let dummyVideoFile = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile, [| 0uy; 0uy; 0uy; 0uy |])
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                // 初回自動スキャンを抑止するためのダミー動画の登録
                let dummyVideo = {
                    TagBasedVideoManager.Domain.Video.Id = "v_dummy"
                    TagBasedVideoManager.Domain.Video.FileName = "Dummy.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile
                    TagBasedVideoManager.Domain.Video.Duration = 10L
                    TagBasedVideoManager.Domain.Video.FileSize = 1000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                 }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn dummyVideo |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            let mutable testSuccess = false
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.PageError.AddHandler(fun _ msg -> output.WriteLine($"BROWSER PAGE ERROR: {msg}"))
                page.Response.AddHandler(fun _ response ->
                   task {
                       try
                           if response.Status = 500 then
                               let! body = response.TextAsync()
                               output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                       with ex ->
                           output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                   } |> ignore
                )
                let! _ = page.GotoAsync(url)

                // 1. 【タグ作成モーダル】開く -> 入力 -> キャンセル -> 再度開く -> 保存
                // 「タグ階層」セクションの中の「追加」テキストボタンをクリックする
                let openTagModalBtn = page.Locator("aside >> div:has-text('タグ階層') >> button:has-text('追加')")
                do! openTagModalBtn.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))
                do! openTagModalBtn.ClickAsync()
                
                let tagModalContainer = page.Locator("div[x-show='tagModal.show']")
                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // キャンセルして閉じる
                let cancelTagBtn = tagModalContainer.Locator("button:has-text('キャンセル')")
                do! cancelTagBtn.ClickAsync()
                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))

                // 再度開いて保存
                do! openTagModalBtn.ClickAsync()
                let tagNameInput = tagModalContainer.Locator("input[type='text']").First
                do! tagNameInput.FillAsync("新規タグ")
                
                let saveTagBtn = tagModalContainer.Locator("button:has-text('保存')")
                do! saveTagBtn.ClickAsync()
                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))
                
                // サイドバーに新規タグが表示されるのを待つ
                let newTagSidebar = page.Locator("aside span:has-text('新規タグ')").First
                do! newTagSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 2. 【自動適用ルール作成モーダル】開く -> 保存
                // 「自動適用ルール」セクションの中の「追加」テキストボタンをクリックする
                let openRuleModalBtn = page.Locator("aside >> div:has-text('自動適用ルール') >> button:has-text('追加')")
                do! openRuleModalBtn.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))
                do! openRuleModalBtn.ClickAsync()

                let ruleModalContainer = page.Locator("div[x-show='ruleModal.show']")
                do! ruleModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // パターン入力
                let rulePatternInput = ruleModalContainer.Locator("input[placeholder*='例: 溜池']")
                do! rulePatternInput.FillAsync("TestPattern")
                
                // 適用するタグ（新規タグ）がセレクトボックスの選択肢に出現するのを待つ
                let optionLoc = ruleModalContainer.Locator("option:has-text('新規タグ')")
                do! optionLoc.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Attached, Timeout = Nullable 15000.0f))

                // 適用するタグ（新規タグ）を選択
                let! _ = page.EvaluateAsync("""
                   const select = document.querySelector("div[x-show='ruleModal.show'] select[x-model='ruleModal.tagId']");
                   if (select && select.options.length > 1) {
                       const option = select.options[1];
                       const dataStack = document.body._x_dataStack;
                       if (dataStack && dataStack.length > 0) {
                           dataStack[0].ruleModal.tagId = option.value;
                       } else {
                           select.value = option.value;
                           select.dispatchEvent(new Event('change', { bubbles: true }));
                       }
                   }
                """)
                
                let saveRuleBtn = ruleModalContainer.Locator("button[\\@click='saveRule()']")
                do! saveRuleBtn.ClickAsync()
                try
                    do! ruleModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))
                with ex ->
                    let testDir = 
                        let mutable dir = DirectoryInfo(contentRoot)
                        while dir <> null && dir.Name <> "test" && dir.Parent <> null do
                            dir <- dir.Parent
                        if dir <> null && dir.Name = "test" then dir.FullName
                        else Path.Combine(contentRoot, "test")
                    let screenshotPath = Path.Combine(testDir, "TestResults", "error_screenshot.png")
                    let _ = Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath))
                    let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = screenshotPath))
                    raise ex
                
                // サイドバーに新規ルールが表示されるのを待つ
                let newRuleSidebar = page.Locator("aside div:has-text('TestPattern')").First
                do! newRuleSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 3. 【タグの編集】
                // 編集用の親タグとして別のタグ（親タグ）を作成しておく
                do! openTagModalBtn.ClickAsync()
                do! tagNameInput.FillAsync("親タグ")
                do! saveTagBtn.ClickAsync()
                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))
                let parentTagSidebar = page.Locator("aside span:has-text('親タグ')").First
                do! parentTagSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                let newTagContainer = page.Locator("aside div.group:has(span:has-text('新規タグ'))").First
                do! newTagContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))
                do! newTagContainer.HoverAsync()
                
                let editTagBtn = newTagContainer.Locator("button[title='編集']")
                do! editTagBtn.ClickAsync()

                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))
                
                // 親タグがセレクトボックスの選択肢に出現するのを待つ
                let parentOptionLoc = tagModalContainer.Locator("option:has-text('親タグ')")
                do! parentOptionLoc.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Attached, Timeout = Nullable 15000.0f))

                // 親タグを選択 (インデックス2が「親タグ」)
                let! _ = page.EvaluateAsync("""
                    const select = document.querySelector("div[x-show='tagModal.show'] select[x-model='tagModal.parentId']");
                    if (select && select.options.length > 2) {
                        const option = select.options[2];
                        const dataStack = document.body._x_dataStack;
                        if (dataStack && dataStack.length > 0) {
                            dataStack[0].tagModal.parentId = option.value;
                        }
                    }
                """)

                do! saveTagBtn.ClickAsync()
                do! tagModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))
                let! _ = page.WaitForFunctionAsync("document.querySelector('aside').textContent.includes('新規タグ')")

                // 4. 【自動適用ルールの編集】
                let editRuleBtn = newRuleSidebar.Locator("button[title='編集']")
                do! editRuleBtn.ClickAsync()

                do! ruleModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 入力欄と保存ボタンをその場で取得して操作する
                let rulePatternInputEdit = ruleModalContainer.Locator("input[placeholder*='例: 溜池']")
                do! rulePatternInputEdit.FillAsync("EditedPattern")
                let saveRuleBtnEdit = ruleModalContainer.Locator("button[\\@click='saveRule()']")
                do! saveRuleBtnEdit.ClickAsync()
                do! ruleModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))

                let editedRuleSidebar = page.Locator("aside div:has-text('EditedPattern')").First
                do! editedRuleSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 5. 【自動適用ルールの削除】
                let deleteRuleBtn = editedRuleSidebar.Locator("button[title='削除']")
                do! deleteRuleBtn.ClickAsync()
                
                do! editedRuleSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))

                // 6. 【タグの削除】
                let dialogHandler = EventHandler<IDialog>(fun _ dialog -> 
                   task { do! dialog.AcceptAsync() } |> ignore
                )
                page.add_Dialog(dialogHandler)
                
                try
                    do! newTagContainer.HoverAsync()
                    let deleteTagBtn = newTagContainer.Locator("button[title='削除']")
                    do! deleteTagBtn.ClickAsync()

                    do! newTagSidebar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))
                finally
                    page.remove_Dialog(dialogHandler)

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile) then
                    try File.Delete(dummyVideoFile) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 動画再生モーダルのイベントが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_play_modal_{Guid.NewGuid():N}.db")
            
            // テスト実行用に、確実に実在する一時的なダミーmp4ファイルを生成する (DLLのロック競合や存在しないファイルの500エラーを回避)
            let dummyVideoFile = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile, [| 0uy; 0uy; 0uy; 0uy |])

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                let video1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test1"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo1.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile
                    TagBasedVideoManager.Domain.Video.Duration = 60L
                    TagBasedVideoManager.Domain.Video.FileSize = 1024000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.Response.AddHandler(fun _ response ->
                    task {
                        try
                            if response.Status = 500 then
                                let! body = response.TextAsync()
                                output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                        with ex ->
                            output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                    } |> ignore
                )
                let! _ = page.GotoAsync(url)
                let gridContainer = page.Locator("main >> div[x-show=\"viewMode === 'grid'\"]").First
                let videoCard = gridContainer.Locator(".group").First
                do! videoCard.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 1. 動画カードをクリックして再生モーダルを開く
                
                // より確実にクリックを検知するため、タイトルのテキストをクリックする
                let titleEl = videoCard.Locator("h3").First
                do! titleEl.ClickAsync()

                // 再生モーダルが表示されるのを待つ
                let playModalContainer = page.Locator("div[x-show='selectedVideo !== null']")
                do! playModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 2. モーダル内でお気に入り星ボタンをトグルする
                let modalFavBtn = playModalContainer.Locator("button[title='お気に入り']")
                do! modalFavBtn.ClickAsync()
                
                // お気に入り状態（星が黄色）に変化するのを待つ
                let modalActiveFavBtn = playModalContainer.Locator("button[title='お気に入り'].text-yellow-400")
                do! modalActiveFavBtn.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 3. 閉じるボタンで再生モーダルを閉じる
                let closePlayModalBtn = playModalContainer.Locator("button[title='閉じる']")
                do! closePlayModalBtn.ClickAsync()
                do! playModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                
                let! isPlayModalVisible = playModalContainer.IsVisibleAsync()
                isPlayModalVisible |> should equal false

                // 4. 背景部分をクリックして閉じることを検証
                do! titleEl.ClickAsync()
                do! playModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // カード部分を避けて、背景部分（左上）を確実にクリックする
                do! playModalContainer.ClickAsync(LocatorClickOptions(Position = Position(X = 10.0f, Y = 10.0f)))
                do! playModalContainer.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))

                let! isPlayModalVisibleAfterBackgroundClick = playModalContainer.IsVisibleAsync()
                isPlayModalVisibleAfterBackgroundClick |> should equal false

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile) then
                    try File.Delete(dummyVideoFile) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 一括操作バーのドロップダウンメニューとタグ適用・解除・選択解除イベントが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_batch_bar_{Guid.NewGuid():N}.db")
            
            // 実在する物理ダミー動画ファイルを生成して、動画カード表示時のバックグラウンドストリーム要求による500エラーを防止する
            let dummyVideoFile1 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_1.mp4")
            let dummyVideoFile2 = Path.Combine(contentRoot, $"dummy_video_{Guid.NewGuid():N}_2.mp4")
            File.WriteAllBytes(dummyVideoFile1, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile2, [| 0uy; 0uy; 0uy; 0uy |])

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                let tagTouring = { TagBasedVideoManager.Domain.Tag.Id = "t_touring"; TagBasedVideoManager.Domain.Tag.Name = "ツーリング"; TagBasedVideoManager.Domain.Tag.ColorCode = "#00ff00"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTouring |> Async.RunSynchronously
                
                let video1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test1"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo1.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile1
                    TagBasedVideoManager.Domain.Video.Duration = 60L
                    TagBasedVideoManager.Domain.Video.FileSize = 1024000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let video2 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test2"
                    TagBasedVideoManager.Domain.Video.FileName = "TestVideo2.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile2
                    TagBasedVideoManager.Domain.Video.Duration = 120L
                    TagBasedVideoManager.Domain.Video.FileSize = 2048000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn video2 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                page.Response.AddHandler(fun _ response ->
                    task {
                        try
                            if response.Status = 500 then
                                let! body = response.TextAsync()
                                output.WriteLine($"HTTP 500 at {response.Url}: {body}")
                        with ex ->
                            output.WriteLine($"Failed to read 500 response body: {ex.Message}")
                    } |> ignore
                )
                let! _ = page.GotoAsync(url)
                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1)
                let checkboxes = gridContainer.Locator("input[x-model='selectedVideoIds']")
                do! checkboxes.First.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 動画のチェックボックスをクリックして選択
                do! checkboxes.Nth(0).ClickAsync()
                do! checkboxes.Nth(1).ClickAsync()

                // 一括操作バーが表示されるのを待つ
                let batchBar = page.Locator("div:has-text('件選択中')")
                do! batchBar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 1. タグ一括追加モーダルの展開と適用
                let batchTagAddBtn = page.Locator("div.fixed.bottom-6 button:has-text('タグ追加')")
                do! batchTagAddBtn.ClickAsync()

                // 一括タグ操作モーダルが表示されるのを待つ
                let batchTagModal = page.Locator("div[x-show='batchTagModalData.show']")
                do! batchTagModal.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // select 要素で「ツーリング」タグを選択
                let selectEl = batchTagModal.Locator("select")
                let! _ = selectEl.SelectOptionAsync("t_touring")

                // 「追加する」ボタンをクリック
                let addConfirmBtn = batchTagModal.Locator("button:has-text('追加する')")
                do! addConfirmBtn.ClickAsync()

                // 両方の動画カードに「ツーリング」タグが表示されるのを待つ
                let tagBadges = gridContainer.Locator("span:has-text('ツーリング')")
                do! tagBadges.Nth(1).WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 一括タグ操作後は自動的に選択解除されるため、2番目の操作（一括削除）のために再度動画を選択し直す
                do! checkboxes.Nth(0).ClickAsync()
                do! checkboxes.Nth(1).ClickAsync()
                do! batchBar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 2. タグ一括削除モーダルの展開と解除
                let batchTagRemoveBtn = page.Locator("div.fixed.bottom-6 button:has-text('タグ削除')")
                do! batchTagRemoveBtn.ClickAsync()

                // 一括タグ操作モーダルが表示されるのを待つ
                do! batchTagModal.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // select 要素で「ツーリング」タグを選択
                let! _ = selectEl.SelectOptionAsync("t_touring")

                // 「削除する」ボタンをクリック
                let removeConfirmBtn = batchTagModal.Locator("button:has-text('削除する')")
                do! removeConfirmBtn.ClickAsync()

                // 両方の動画カードから「ツーリング」タグが消えるの確認（件数が0になるのを確認）
                do! tagBadges.First.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let! tagBadgeCount = tagBadges.CountAsync()
                tagBadgeCount |> should equal 0

                // 選択解除のために再度選択し直す
                do! checkboxes.Nth(0).ClickAsync()
                do! checkboxes.Nth(1).ClickAsync()
                do! batchBar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // 3. 選択解除ボタンの検証
                let clearSelectionBtn = page.Locator("div.fixed.bottom-6 button[title='選択解除']")
                do! clearSelectionBtn.ClickAsync()
                
                // 一括操作バーが非表示になるのを待つ
                do! batchBar.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 15000.0f))

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile1) then
                    try File.Delete(dummyVideoFile1) with | _ -> ()
                if File.Exists(dummyVideoFile2) then
                    try File.Delete(dummyVideoFile2) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 統計・おすすめダッシュボードが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_analytics_{Guid.NewGuid():N}.db")
            let dummyVideoFile = Path.Combine(contentRoot, $"dummy_video_analytics_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile, [| 0uy; 0uy; 0uy; 0uy |])
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                let v1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_analytics_1"
                    TagBasedVideoManager.Domain.Video.FileName = "video_test_e2e_anime_pv.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile
                    TagBasedVideoManager.Domain.Video.Duration = 60L
                    TagBasedVideoManager.Domain.Video.FileSize = 1024L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 5
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = Some DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn v1 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. ヘッダーの「統計・おすすめ」ボタンをクリック
                let statsBtn = page.Locator("header button:has-text('統計・おすすめ')")
                do! statsBtn.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))
                do! statsBtn.ClickAsync()

                // 2. モーダルが開くのを待つ
                let modal = page.Locator("div[x-show=\"analyticsModal.show\"]")
                do! modal.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // モーダル内のランキングリストで、動画 "video_test_e2e_anime_pv.mp4" が表示されていることを確認
                let rankingItem = modal.Locator("h4:has-text('video_test_e2e_anime_pv.mp4')")
                do! rankingItem.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // 再生回数のテキスト「再生数: 5 回」が含まれているか
                let countText = modal.Locator("span:has-text('再生数: 5 回')")
                do! countText.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // 3. おすすめタブのクリック
                let recommendTabBtn = modal.Locator("button:has-text('あなたへのおすすめ')")
                do! recommendTabBtn.ClickAsync()

                // 嗜好キーワードセクションの表示を確認
                let keywordHeader = modal.Locator("span:has-text('あなたの嗜好キーワード')")
                do! keywordHeader.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // 4. モーダルを閉じる
                let closeBtn = modal.Locator("button:has-text('閉じる')")
                do! closeBtn.ClickAsync()

                // モーダルが非表示になるのを待つ
                do! modal.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile) then
                    try File.Delete(dummyVideoFile) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 動画一覧のページング・インクリメンタル表示機能が正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_paging_{Guid.NewGuid():N}.db")
            let dummyVideoFile = Path.Combine(contentRoot, $"dummy_video_paging_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile, [| 0uy; 0uy; 0uy; 0uy |])
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                // 60個の動画をDBにインサート
                for i in 1 .. 60 do
                    let v = {
                        TagBasedVideoManager.Domain.Video.Id = $"v_paging_{i}"
                        TagBasedVideoManager.Domain.Video.FileName = sprintf "video_paging_test_%02d.mp4" i
                        TagBasedVideoManager.Domain.Video.FilePath = Path.Combine(contentRoot, $"dummy_video_paging_{i}.mp4")
                        TagBasedVideoManager.Domain.Video.Duration = 10L
                        TagBasedVideoManager.Domain.Video.FileSize = 1024L
                        TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                        TagBasedVideoManager.Domain.Video.IsFavorite = false
                        TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddMinutes(float -i)
                        TagBasedVideoManager.Domain.Video.AccessCount = 0
                        TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                        TagBasedVideoManager.Domain.Video.Tags = []
                    }
                    let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn v |> Async.RunSynchronously
                    ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                
                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 動画カードが表示されるまで待つ
                let firstCard = page.Locator("h3:has-text('video_paging_test_01.mp4')").First
                do! firstCard.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 15000.0f))

                // デフォルトはページングモード (1ページ50件) のため、表示されている動画カード数を検証
                // ※ グリッド表示の各動画コンテナは template x-for 配下に配置されている
                let cards = page.Locator("div[x-show=\"viewMode === 'grid'\"] > div")
                let! initialCount = cards.CountAsync()
                initialCount |> should equal 50

                // ナビゲーションの「次へ」ボタンをクリックして2ページ目に移動
                let nextBtn = page.Locator("button:has-text('次へ')")
                do! nextBtn.ClickAsync()

                // 2ページ目に切り替わるのを待ち、動画数が10件であることを確認
                let pageInfo = page.Locator("span:has-text('2 / 2 ページ')")
                do! pageInfo.WaitForAsync(new LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! page2Count = cards.CountAsync()
                page2Count |> should equal 10

                // ツールバーの表示設定で「追加」モードをクリック
                let incrementalBtn = page.Locator("button[title='順次追加表示']")
                do! incrementalBtn.ClickAsync()

                // インクリメンタルモードでは初期20件が表示される
                let! incInitCount = cards.CountAsync()
                incInitCount |> should equal 20

                // 「もっと表示」ボタンをクリック
                let loadMoreBtn = page.Locator("button:has-text('もっと表示')")
                do! loadMoreBtn.ClickAsync()

                // 40件に増えるのを確認
                let! incSecondCount = cards.CountAsync()
                incSecondCount |> should equal 40

                // 再度「もっと表示」ボタンをクリック
                do! loadMoreBtn.ClickAsync()

                // 全件（60件）になるのを確認
                let! incThirdCount = cards.CountAsync()
                incThirdCount |> should equal 60

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile) then
                    try File.Delete(dummyVideoFile) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 動画一覧の再生回数による並び替えが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_sort_{Guid.NewGuid():N}.db")
            let dummyVideoFile1 = Path.Combine(contentRoot, $"dummy_video1_{Guid.NewGuid():N}.mp4")
            let dummyVideoFile2 = Path.Combine(contentRoot, $"dummy_video2_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile1, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile2, [| 0uy; 0uy; 0uy; 0uy |])
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                let v1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_sort_1"
                    TagBasedVideoManager.Domain.Video.FileName = "Alpha_Video.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile1
                    TagBasedVideoManager.Domain.Video.Duration = 10L
                    TagBasedVideoManager.Domain.Video.FileSize = 1000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddMinutes(-5.0)
                    TagBasedVideoManager.Domain.Video.AccessCount = 5
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let v2 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_sort_2"
                    TagBasedVideoManager.Domain.Video.FileName = "Beta_Video.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile2
                    TagBasedVideoManager.Domain.Video.Duration = 10L
                    TagBasedVideoManager.Domain.Video.FileSize = 1000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 10
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = []
                }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn v1 |> Async.RunSynchronously
                let _ = TagBasedVideoManager.Infrastructure.Db.saveVideo conn v2 |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()
                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! page = browser.NewPageAsync()
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                let! _ = page.GotoAsync(url)

                // 動画カードが表示されるまで待機
                let cards = page.Locator("div[x-show=\"viewMode === 'grid'\"] > div")
                let! _ = page.WaitForSelectorAsync("div[x-show=\"viewMode === 'grid'\"] > div")
                
                // 初期状態（登録日順）のチェック
                // v2 (CreatedAtが最新) が上に来るはず
                let! firstCardText = cards.Nth(0).Locator(".video-title-text").TextContentAsync()
                firstCardText.Trim() |> should equal "Beta_Video.mp4"

                // 表示設定ツールバーが開いていることを確認（もし閉じていたら開く）
                let! toolbarVisible = page.Locator("div[x-show='showToolbar']").IsVisibleAsync()
                if not toolbarVisible then
                    do! page.ClickAsync("button[title='表示設定ツールバーの開閉']")

                // ソートを「再生回数」に変更 (値: "accessCount")
                let! _ = page.SelectOptionAsync("select[x-model='sortBy']", "accessCount")
                // 表示更新を待つ
                do! page.WaitForTimeoutAsync(200.0f)

                // 再生回数順（v2: 10回 が最初）になっていることを確認
                let! sortedFirstCardText = cards.Nth(0).Locator(".video-title-text").TextContentAsync()
                sortedFirstCardText.Trim() |> should equal "Beta_Video.mp4" // v2 (10回)

                // 昇順（ASC）に変更して並び替え順が反転する（v1: 5回 が最初になる）ことを確認
                let! _ = page.ClickAsync("button[title='降順'], button[title='昇順']")
                do! page.WaitForTimeoutAsync(200.0f)

                let! ascFirstCardText = cards.Nth(0).Locator(".video-title-text").TextContentAsync()
                ascFirstCardText.Trim() |> should equal "Alpha_Video.mp4" // v1 (5回)

                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()

            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile1) then
                    try File.Delete(dummyVideoFile1) with | _ -> ()
                if File.Exists(dummyVideoFile2) then
                    try File.Delete(dummyVideoFile2) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E: 左ペインのタグ表示のレイアウトが最適化されていることを検証する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_tag_layout_{Guid.NewGuid():N}.db")
            
            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                let tagTest = { TagBasedVideoManager.Domain.Tag.Id = "t_layout_test"; TagBasedVideoManager.Domain.Tag.Name = "レイアウトテスト"; TagBasedVideoManager.Domain.Tag.ColorCode = "#ff0000"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let _ = TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTest |> Async.RunSynchronously
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()
                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! page = browser.NewPageAsync()
                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)
                let! _ = page.GotoAsync(url)

                // タグ行が表示されるまで待つ
                let! _ = page.WaitForSelectorAsync("aside >> text=レイアウトテスト")
                
                // タグ名を表示している span のクラスを検証
                let tagNameSpan = page.Locator("aside >> text=レイアウトテスト")
                
                // 親の span (filterByTagを呼ぶ要素) が `flex-1 min-w-0 mr-2` 等を持っているかを検証する
                let parentSpan = tagNameSpan.Locator("..")
                let! parentClass = parentSpan.GetAttributeAsync("class")
                parentClass.Contains("flex-1") |> should equal true
                parentClass.Contains("min-w-0") |> should equal true

                // 件数バッジのクラスを検証
                let countSpan = parentSpan.Locator("..").Locator("span:has-text('(0)')")
                let! countClass = countSpan.GetAttributeAsync("class")
                countClass.Contains("ml-auto") |> should equal true
                countClass.Contains("shrink-0") |> should equal true

                // 操作ボタンの div が absolute になっているか検証
                let actionsDiv = parentSpan.Locator("..").Locator("..").Locator("div.absolute")
                let! actionsClass = actionsDiv.GetAttributeAsync("class")
                actionsClass.Contains("absolute") |> should equal true
                actionsClass.Contains("right-2") |> should equal true
                
                let! covResult = cdp.SendAsync("Profiler.takePreciseCoverage", null)
                if covResult.HasValue then
                    E2ETests.ParseAndAddCoverage(covResult.Value)
                let! _ = cdp.SendAsync("Profiler.stopPreciseCoverage", null)
                let! _ = cdp.SendAsync("Profiler.disable", null)
                do! cdp.DetachAsync()
                do! browser.CloseAsync()

            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
        }



