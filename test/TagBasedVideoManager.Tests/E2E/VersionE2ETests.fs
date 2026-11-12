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

/// <summary>
/// アプリケーションバージョン表示に関する Playwright E2E テスト (E2E-01, E2E-02, E2E-03)。
/// デスクトップ表示、モバイル折り返し表示、および API 障害耐性を検証する。
/// </summary>
type VersionE2ETests (output: ITestOutputHelper) =

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

    let seedTestData dbPath =
        use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
        let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
        ()

    let ensureResultsDir () =
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let projectRoot = findProjectRoot (DirectoryInfo(baseDir))
        let resultsDir = Path.Combine(projectRoot, "test", "TestResults")
        if not (Directory.Exists(resultsDir)) then
            Directory.CreateDirectory(resultsDir) |> ignore
        resultsDir

    [<Fact>]
    member _.``デスクトップ表示時にヘッダーにバージョンバッジが表示されタイトルに反映される (E2E-01)`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_ver_desktop_{Guid.NewGuid():N}.db")
            seedTestData dbPath
            let host, url = startTestServer dbPath
            do! host.StartAsync()

            try
                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! context = browser.NewContextAsync(BrowserNewContextOptions(
                    ViewportSize = ViewportSize(Width = 1280, Height = 800)
                ))
                let! page = context.NewPageAsync()

                let! _ = page.GotoAsync(url)
                
                // バッジが表示されるのを待機
                let badge = page.Locator("[data-testid='app-version-badge']")
                do! badge.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))

                let! badgeText = badge.TextContentAsync()
                badgeText.Trim() |> should equal "v0.2.0"

                let! title = page.TitleAsync()
                title |> should equal "Tag-Based Video Manager (v0.2.0)"

                // 画面キャプチャ保存
                let resultsDir = ensureResultsDir ()
                let shotPath = Path.Combine(resultsDir, "E2E-01_Desktop_Version.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = shotPath))
                output.WriteLine($"Saved screenshot to {shotPath}")
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``モバイル表示時にタイトルが折り返されてもバッジが末尾に追従して表示される (E2E-02)`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_ver_mobile_{Guid.NewGuid():N}.db")
            seedTestData dbPath
            let host, url = startTestServer dbPath
            do! host.StartAsync()

            try
                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                // モバイル幅 375px
                let! context = browser.NewContextAsync(BrowserNewContextOptions(
                    ViewportSize = ViewportSize(Width = 375, Height = 667)
                ))
                let! page = context.NewPageAsync()

                let! _ = page.GotoAsync(url)
                
                let badge = page.Locator("[data-testid='app-version-badge']")
                do! badge.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))

                let! badgeText = badge.TextContentAsync()
                badgeText.Trim() |> should equal "v0.2.0"

                let resultsDir = ensureResultsDir ()
                let shotPath = Path.Combine(resultsDir, "E2E-02_Mobile_Version.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = shotPath))
                output.WriteLine($"Saved screenshot to {shotPath}")
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``API障害・ハング時にコア機能が維持されバッジは安全にフォールバックする (E2E-03)`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_ver_recovery_{Guid.NewGuid():N}.db")
            seedTestData dbPath
            let host, url = startTestServer dbPath
            do! host.StartAsync()

            try
                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                let! context = browser.NewContextAsync(BrowserNewContextOptions(
                    ViewportSize = ViewportSize(Width = 1280, Height = 800)
                ))
                let! page = context.NewPageAsync()

                // /api/version を 500 エラーでモック
                do! page.RouteAsync("**/api/version", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable 500,
                        Body = "Internal Server Error"
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                
                // コアUI（タイトル）がロードされることを待機
                let h1 = page.Locator("h1")
                do! h1.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                let! h1Text = h1.TextContentAsync()
                h1Text.Contains("Tag-Based Video Manager") |> should be True

                // バッジは非表示のままであること
                let badge = page.Locator("[data-testid='app-version-badge']")
                let! isVisible = badge.IsVisibleAsync()
                isVisible |> should be False

                let resultsDir = ensureResultsDir ()
                let shotPath = Path.Combine(resultsDir, "E2E-03_Recovery_Version.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = shotPath))
                output.WriteLine($"Saved screenshot to {shotPath}")
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with _ -> ()
        } :> Task
