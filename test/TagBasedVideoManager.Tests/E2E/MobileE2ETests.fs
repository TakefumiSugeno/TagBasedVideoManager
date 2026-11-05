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

type MobileE2ETests (output: ITestOutputHelper) =

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

    // テスト用の共通サーバー起動処理
    let startTestServer dbPath =
        let port = getFreePort ()
        let url = $"http://localhost:{port}"
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let projectRoot = findProjectRoot (DirectoryInfo(baseDir))
        let contentRoot = Path.Combine(projectRoot, "src", "TagBasedVideoManager")
        let webRoot = Path.Combine(contentRoot, "wwwroot")

        let host =
            Microsoft.AspNetCore.WebHost.CreateDefaultBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .UseUrls(url)
                .ConfigureServices(fun services ->
                    Program.configureServices services
                    services.AddTransient<System.Data.IDbConnection>(fun _ -> 
                        new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}")
                    ) |> ignore
                )
                .Configure(Program.configureApp)
                .Build()
        host, url

    // テストデータシードヘルパー
    let seedTestData dbPath =
        use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
        let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore

        // 親タグ・子タグの作成
        let animeTag = {
            TagBasedVideoManager.Domain.Tag.Id = "tag_anime"
            TagBasedVideoManager.Domain.Tag.Name = "アニメ"
            TagBasedVideoManager.Domain.Tag.ParentId = None
            TagBasedVideoManager.Domain.Tag.ColorCode = "#ec4899"
            TagBasedVideoManager.Domain.Tag.VideoCount = 2
        }
        let actionTag = {
            TagBasedVideoManager.Domain.Tag.Id = "tag_action"
            TagBasedVideoManager.Domain.Tag.Name = "アクション"
            TagBasedVideoManager.Domain.Tag.ParentId = Some "tag_anime"
            TagBasedVideoManager.Domain.Tag.ColorCode = "#ef4444"
            TagBasedVideoManager.Domain.Tag.VideoCount = 1
        }
        let sfTag = {
            TagBasedVideoManager.Domain.Tag.Id = "tag_sf"
            TagBasedVideoManager.Domain.Tag.Name = "SF"
            TagBasedVideoManager.Domain.Tag.ParentId = None
            TagBasedVideoManager.Domain.Tag.ColorCode = "#3b82f6"
            TagBasedVideoManager.Domain.Tag.VideoCount = 2
        }
        TagBasedVideoManager.Infrastructure.Db.saveTag conn animeTag |> Async.RunSynchronously |> ignore
        TagBasedVideoManager.Infrastructure.Db.saveTag conn actionTag |> Async.RunSynchronously |> ignore
        TagBasedVideoManager.Infrastructure.Db.saveTag conn sfTag |> Async.RunSynchronously |> ignore

        let contentRoot = Directory.GetCurrentDirectory()
        let dummyVideo1 = Path.Combine(contentRoot, $"mobile_dummy_1_{Guid.NewGuid():N}.mp4")
        let dummyVideo2 = Path.Combine(contentRoot, $"mobile_dummy_2_{Guid.NewGuid():N}.mp4")
        let dummyVideo3 = Path.Combine(contentRoot, $"mobile_dummy_3_{Guid.NewGuid():N}.mp4")
        File.WriteAllBytes(dummyVideo1, [| 0uy; 0uy; 0uy; 0uy |])
        File.WriteAllBytes(dummyVideo2, [| 0uy; 0uy; 0uy; 0uy |])
        File.WriteAllBytes(dummyVideo3, [| 0uy; 0uy; 0uy; 0uy |])

        let video1 = {
            TagBasedVideoManager.Domain.Video.Id = "v_mobile_1"
            TagBasedVideoManager.Domain.Video.FileName = "Mecha_Action_Episode1.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = dummyVideo1
            TagBasedVideoManager.Domain.Video.Duration = 120L
            TagBasedVideoManager.Domain.Video.FileSize = 10485760L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = None
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddMinutes(-30.0)
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow.AddMinutes(-30.0)
            TagBasedVideoManager.Domain.Video.AccessCount = 5
            TagBasedVideoManager.Domain.Video.LastAccessedAt = Some DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.Tags = [ animeTag; actionTag ]
        }
        let video2 = {
            TagBasedVideoManager.Domain.Video.Id = "v_mobile_2"
            TagBasedVideoManager.Domain.Video.FileName = "Space_Odyssey_SciFi.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = dummyVideo2
            TagBasedVideoManager.Domain.Video.Duration = 300L
            TagBasedVideoManager.Domain.Video.FileSize = 20971520L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = None
            TagBasedVideoManager.Domain.Video.IsFavorite = true
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddMinutes(-20.0)
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow.AddMinutes(-20.0)
            TagBasedVideoManager.Domain.Video.AccessCount = 2
            TagBasedVideoManager.Domain.Video.LastAccessedAt = Some DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.Tags = [ sfTag ]
        }
        let video3 = {
            TagBasedVideoManager.Domain.Video.Id = "v_mobile_3"
            TagBasedVideoManager.Domain.Video.FileName = "Cyber_City_2099.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = dummyVideo3
            TagBasedVideoManager.Domain.Video.Duration = 180L
            TagBasedVideoManager.Domain.Video.FileSize = 15728640L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = None
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddMinutes(-10.0)
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow.AddMinutes(-10.0)
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = [ animeTag; sfTag ]
        }
        TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously |> ignore
        TagBasedVideoManager.Infrastructure.Db.saveVideo conn video2 |> Async.RunSynchronously |> ignore
        TagBasedVideoManager.Infrastructure.Db.saveVideo conn video3 |> Async.RunSynchronously |> ignore

        [ dummyVideo1; dummyVideo2; dummyVideo3 ]

    let getScreenshotPath filename =
        let projectRoot = findProjectRoot (DirectoryInfo(AppContext.BaseDirectory))
        let resultsDir = Path.Combine(projectRoot, "test", "TestResults")
        if not (Directory.Exists(resultsDir)) then Directory.CreateDirectory(resultsDir) |> ignore
        Path.Combine(resultsDir, filename)

    [<Fact>]
    member _.``E2E Mobile: モバイル常設検索バーとタグピルバーによる絞り込み・トグル連携が正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_search_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                // モバイル解像度 (iPhone 14 想定: 390x844)
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. モバイル常設検索バーが表示されていること
                let mobileSearchInput = page.Locator("input[placeholder*='キーワードまたはタグ名で検索']")
                do! mobileSearchInput.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                let! (isInputVisible: bool) = mobileSearchInput.IsVisibleAsync()
                isInputVisible |> should equal true

                // 2. クイックタグピルバーにタグが表示されていること
                let actionTagPill = page.Locator("button:has-text('アクション')").First
                do! actionTagPill.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                
                // 3. タグピル「アクション」をクリック -> 検索バーに 'tag:アクション' が入り、動画一覧が絞り込まれる
                do! actionTagPill.ClickAsync()
                let! _ = page.WaitForFunctionAsync("document.querySelector(\"input[placeholder*='キーワードまたはタグ名で検索']\").value === 'tag:アクション'")
                let! (inputVal: string) = mobileSearchInput.InputValueAsync()
                inputVal |> should equal "tag:アクション"

                // スクリーンショット保存
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_01_tag_pill_filter.png"))

                // 4. クリアボタンをクリック -> 入力がクリアされ全件表示に戻る
                let clearBtn = page.Locator("button[title='クリア']").First
                do! clearBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! clearBtn.ClickAsync()
                let! _ = page.WaitForFunctionAsync("document.querySelector(\"input[placeholder*='キーワードまたはタグ名で検索']\").value === ''")
                let! (clearedVal: string) = mobileSearchInput.InputValueAsync()
                clearedVal |> should equal ""

                // 5. フリーワード検索の検証
                do! mobileSearchInput.FillAsync("Mecha")
                do! page.WaitForTimeoutAsync(400.0f) // debounce
                let! _ = page.WaitForFunctionAsync("document.querySelectorAll(\"[x-show=\\\"mobileViewMode === 'grid'\\\"] h3\").length === 1")

                // カバレッジ回収
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
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Mobile: 2列グリッドとスリムリストのレイアウト切り替えおよびお気に入り操作が正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_layout_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. 初期状態: 2列グリッドが表示されていること
                let gridContainer = page.Locator("div[x-show=\"mobileViewMode === 'grid'\"]")
                do! gridContainer.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                let! (isGridVisible: bool) = gridContainer.IsVisibleAsync()
                isGridVisible |> should equal true

                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_02_layout_grid.png"))

                // 2. 表示切替ボタンをクリックしてリスト表示へ
                let toggleLayoutBtn = page.Locator("button[title*='表示へ切替']").First
                do! toggleLayoutBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! toggleLayoutBtn.ClickAsync()

                // 3. リストコンテナが表示され、グリッドが非表示になること
                let listContainer = page.Locator("div[x-show=\"mobileViewMode === 'compact'\"]")
                do! listContainer.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isListVisible: bool) = listContainer.IsVisibleAsync()
                isListVisible |> should equal true
                let! (isGridHidden: bool) = gridContainer.IsVisibleAsync()
                isGridHidden |> should equal false

                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_03_layout_list.png"))

                // 4. スリムリスト内のお気に入りボタンの操作
                let favoriteButtons = listContainer.Locator("button[title='お気に入り']")
                let firstFavBtn = favoriteButtons.First
                do! firstFavBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! firstFavBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(500.0f)

                // 5. 再度クリックしてグリッドに戻る
                do! toggleLayoutBtn.ClickAsync()
                do! gridContainer.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isGridVisibleAgain: bool) = gridContainer.IsVisibleAsync()
                isGridVisibleAgain |> should equal true

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
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Mobile: 全画面没入型プレイヤーの起動および前後動画の連続視聴ナビゲーションが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_player_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. 動画カードをクリックしてプレイヤー起動
                let gridCards = page.Locator("div[x-show=\"mobileViewMode === 'grid'\"] h3")
                do! gridCards.First.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                do! gridCards.First.ClickAsync()

                // 2. 全画面没入型プレイヤーモーダルが表示されること
                let playerModal = page.Locator("div[x-show=\"selectedVideo !== null\"]")
                do! playerModal.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isPlayerVisible: bool) = playerModal.IsVisibleAsync()
                isPlayerVisible |> should equal true

                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_04_immersive_player.png"))

                // 3. 次の動画ボタンをクリック
                let nextBtn = playerModal.Locator("button[title='次の動画を再生']").First
                do! nextBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! nextBtn.ClickAsync(LocatorClickOptions(Force = Nullable true))
                do! page.WaitForTimeoutAsync(500.0f)

                // 4. 前の動画ボタンをクリック
                let prevBtn = playerModal.Locator("button[title='前の動画を再生']").First
                do! prevBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! prevBtn.ClickAsync(LocatorClickOptions(Force = Nullable true))
                do! page.WaitForTimeoutAsync(500.0f)

                // 5. プレイヤーを閉じる
                let closeBtn = playerModal.Locator("button[title='プレイヤーを閉じる']").First
                do! closeBtn.ClickAsync(LocatorClickOptions(Force = Nullable true))
                do! playerModal.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let! (isPlayerClosed: bool) = playerModal.IsVisibleAsync()
                isPlayerClosed |> should equal false

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
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Mobile: タグ階層ブラウザ（ボトムシート）からのタグ検索と選択絞り込みが正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_tag_sheet_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. クイックタグピルバーの「🏷️ 全タグ」ボタンをタップ
                let allTagsBtn = page.Locator("button[title*='タグ階層ツリー']").First
                do! allTagsBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                do! allTagsBtn.ClickAsync()

                // 2. タグ階層ブラウザのボトムシート本体（fixed inset-x-0 bottom-0）が表示されること
                let tagSheet = page.Locator("div[x-show='tagBrowserOpen'].fixed.bottom-0").First
                do! tagSheet.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isSheetVisible: bool) = tagSheet.IsVisibleAsync()
                isSheetVisible |> should equal true

                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_05_tag_browser_sheet.png"))

                // 3. ボトムシート内のタグ検索バーで「SF」と検索
                let tagSearchInput = tagSheet.Locator("input[x-model='tagSearchQuery']")
                do! tagSearchInput.FillAsync("SF")
                do! page.WaitForTimeoutAsync(300.0f)

                // 4. 「SF」タグを選択
                let sfTagItem = tagSheet.Locator("span:has-text('SF')").First
                do! sfTagItem.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! page.WaitForTimeoutAsync(400.0f)
                do! sfTagItem.ClickAsync(LocatorClickOptions(Force = Nullable true))

                // 5. ボトムシートが閉じ、検索入力に反映されること
                do! tagSheet.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let mobileSearchInput = page.Locator("input[placeholder*='キーワードまたはタグ名で検索']")
                let! (inputVal: string) = mobileSearchInput.InputValueAsync()
                inputVal |> should equal "tag:SF"

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
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Mobile: モバイルからの新規タグ作成モーダル（カラーパレット・階層指定）が正常に動作する`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_create_tag_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! (cdp: ICDPSession) = page.Context.NewCDPSessionAsync(page)
                let! _ = cdp.SendAsync("Profiler.enable", null)
                let args = System.Collections.Generic.Dictionary<string, obj>()
                args.Add("callCount", true)
                args.Add("detailed", true)
                let! _ = cdp.SendAsync("Profiler.startPreciseCoverage", args)

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"BROWSER CONSOLE: {msg.Text}"))
                let! _ = page.GotoAsync(url)

                // 1. クイックタグピルバーの「作成」ボタンをタップ
                let createTagPillBtn = page.Locator("button[title='新しいタグを作成']").First
                do! createTagPillBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                do! createTagPillBtn.ClickAsync()

                // 2. タグ作成モーダルが表示されること
                let tagModal = page.Locator("div[x-show='tagModal.show']").First
                do! tagModal.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isModalVisible: bool) = tagModal.IsVisibleAsync()
                isModalVisible |> should equal true

                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = getScreenshotPath "mobile_06_create_tag_modal.png"))

                // 3. タグ名を入力
                let nameInput = tagModal.Locator("input[x-model='tagModal.name']")
                do! nameInput.FillAsync("コメディ")

                // 4. カラーパレットの丸ボタン（例: 黄色 #eab308）を選択
                let colorButtons = tagModal.Locator("div.space-y-2 button[type='button']")
                let! (btnCount: int) = colorButtons.CountAsync()
                if btnCount > 0 then
                    do! colorButtons.Nth(1).ClickAsync()

                // 5. 「保存」ボタンをクリック
                let saveBtn = tagModal.Locator("button:has-text('保存')").First
                do! saveBtn.ClickAsync()

                // 6. モーダルが閉じ、作成されたタグがピルバーに反映されること
                do! tagModal.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let newTagPill = page.Locator("button:has-text('コメディ')").First
                do! newTagPill.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! (isNewTagVisible: bool) = newTagPill.IsVisibleAsync()
                isNewTagVisible |> should equal true

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
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Mobile: 表示切り替えボタンをクリックしてもアイコンが増殖しないこと`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_mobile_icon_duplication_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 390, Height = 844),
                    IsMobile = Nullable true,
                    HasTouch = Nullable true
                ))

                let! _ = page.GotoAsync(url)
                let toggleBtn = page.Locator("button[title*='表示へ切替']").First
                do! toggleBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))

                // 初期状態のSVG数を確認 (1個)
                let! (count0: int) = toggleBtn.Locator("svg:visible").CountAsync()
                output.WriteLine($"[ICON_CHECK] Initial SVG count: {count0}")
                count0 |> should equal 1

                // 1回目の切り替え
                do! toggleBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (count1: int) = toggleBtn.Locator("svg:visible").CountAsync()
                output.WriteLine($"[ICON_CHECK] After 1st click SVG count: {count1}")
                count1 |> should equal 1

                // 2回目の切り替え
                do! toggleBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (count2: int) = toggleBtn.Locator("svg:visible").CountAsync()
                output.WriteLine($"[ICON_CHECK] After 2nd click SVG count: {count2}")
                count2 |> should equal 1

                // 3回目の切り替え
                do! toggleBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (count3: int) = toggleBtn.Locator("svg:visible").CountAsync()
                output.WriteLine($"[ICON_CHECK] After 3rd click SVG count: {count3}")
                count3 |> should equal 1

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }

    [<Fact>]
    member _.``E2E Desktop: グリッド表示とリスト表示を切り替えてもアイコンが増殖しないこと`` () =
        task {
            let contentRoot = Directory.GetCurrentDirectory()
            let dbPath = Path.Combine(contentRoot, $"test_e2e_desktop_icon_duplication_{Guid.NewGuid():N}.db")
            let dummyFiles = seedTestData dbPath
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                let! (playwright: IPlaywright) = Playwright.CreateAsync()
                let! (browser: IBrowser) = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! (page: IPage) = browser.NewPageAsync(BrowserNewPageOptions(
                    ViewportSize = ViewportSize(Width = 1280, Height = 800)
                ))

                let! _ = page.GotoAsync(url)
                let gridBtn = page.Locator("button[title='グリッド表示']").First
                let listBtn = page.Locator("button[title='リスト表示']").First
                do! gridBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))

                // 各ボタンのSVG数が常に1個であること
                let! (gridSvg0: int) = gridBtn.Locator("svg").CountAsync()
                let! (listSvg0: int) = listBtn.Locator("svg").CountAsync()
                gridSvg0 |> should equal 1
                listSvg0 |> should equal 1

                // リスト表示へ切り替え
                do! listBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (gridSvg1: int) = gridBtn.Locator("svg").CountAsync()
                let! (listSvg1: int) = listBtn.Locator("svg").CountAsync()
                gridSvg1 |> should equal 1
                listSvg1 |> should equal 1

                // グリッド表示へ切り替え
                do! gridBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (gridSvg2: int) = gridBtn.Locator("svg").CountAsync()
                let! (listSvg2: int) = listBtn.Locator("svg").CountAsync()
                gridSvg2 |> should equal 1
                listSvg2 |> should equal 1

                // 再度リスト表示へ切り替え
                do! listBtn.ClickAsync()
                do! page.WaitForTimeoutAsync(200.0f)
                let! (gridSvg3: int) = gridBtn.Locator("svg").CountAsync()
                let! (listSvg3: int) = listBtn.Locator("svg").CountAsync()
                gridSvg3 |> should equal 1
                listSvg3 |> should equal 1

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                for f in dummyFiles do
                    if File.Exists(f) then
                        try File.Delete(f) with | _ -> ()
        }
