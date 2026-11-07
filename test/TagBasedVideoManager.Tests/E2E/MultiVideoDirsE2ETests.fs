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
/// 複数動画ディレクトリ（VIDEO_DIRS）対応に関する Playwright E2E テスト。
/// test_plan.md の第6節「E2Eテスト・画面検証ケース定義」に基づく検証を実施する。
/// </summary>
type MultiVideoDirsE2ETests (output: ITestOutputHelper) =

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

    // テスト用のテストサーバー起動処理
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

    let getTestResultsDir () =
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let projectRoot = findProjectRoot (DirectoryInfo(baseDir))
        let resultsDir = Path.Combine(projectRoot, "test", "TestResults")
        if not (Directory.Exists(resultsDir)) then
            Directory.CreateDirectory(resultsDir) |> ignore
        resultsDir

    [<Fact>]
    member _.``E2E-01: Happy Path - 複数物理フォルダの動画検出とルート名タグの自動付与`` () =
        task {
            let origVideoDirs = Environment.GetEnvironmentVariable("VIDEO_DIRS")
            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let tempBase = Path.Combine(Path.GetTempPath(), $"mv_e2e_01_{Guid.NewGuid():N}")
            let dirA = Path.Combine(tempBase, "AlphaFolder")
            let dirB = Path.Combine(tempBase, "BetaFolder")
            let dbPath = Path.Combine(tempBase, "e2e_01.db")

            Directory.CreateDirectory(dirA) |> ignore
            Directory.CreateDirectory(dirB) |> ignore

            let fileA = Path.Combine(dirA, "alpha_video.mp4")
            let fileB = Path.Combine(dirB, "beta_video.mp4")
            File.WriteAllBytes(fileA, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(fileB, [| 0uy; 0uy; 0uy; 0uy |])

            Environment.SetEnvironmentVariable("VIDEO_DIRS", $"{dirA};{dirB}")
            Environment.SetEnvironmentVariable("VIDEO_DIR", null)

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                ()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))

            try
                do! host.StartAsync()
                let! page = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"[BROWSER] {msg.Text}"))

                let! _ = page.GotoAsync(url)

                // 同期ボタンの表示を待機
                let syncBtn = page.Locator("button:has-text('同期')")
                do! syncBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible))

                // 同期ボタンが活性化（disabled解除）するのを待機
                let! _ = page.WaitForSelectorAsync("button:has-text('同期'):not([disabled])")

                // 同期ボタンをクリックし、スキャン完了（200 OK）を待機
                let scanResponseTask = page.WaitForResponseAsync(fun resp -> resp.Url.Contains("/api/scan") && resp.Status = 200)
                do! syncBtn.ClickAsync()
                let! scanResp = scanResponseTask
                scanResp.Status |> should equal 200

                // スキャン反映後の最新状態（タグ一覧含む）を表示するためページをリロード
                let! _ = page.ReloadAsync()

                // 一覧に動画が表示されるのを待機
                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1)
                let videoCards = gridContainer.Locator(".group")
                do! videoCards.First.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))

                let! videoCount = videoCards.CountAsync()
                videoCount |> should equal 2

                // サイドバーにそれぞれのルート名タグ（AlphaFolder, BetaFolder）が表示されていることをアサート
                let sidebar = page.Locator("aside")
                do! sidebar.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible))
                let! sidebarText = sidebar.InnerTextAsync()
                sidebarText.Contains("AlphaFolder") |> should be True
                sidebarText.Contains("BetaFolder") |> should be True

                // スクリーンショット保存
                let resultsDir = getTestResultsDir ()
                let screenshotPath = Path.Combine(resultsDir, "e2e-01-happy-path.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = screenshotPath, FullPage = Nullable true))
                File.Exists(screenshotPath) |> should be True

                do! browser.CloseAsync()
            finally
                playwright.Dispose()
                host.Dispose()
                Environment.SetEnvironmentVariable("VIDEO_DIRS", origVideoDirs)
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                if Directory.Exists(tempBase) then
                    try Directory.Delete(tempBase, true) with _ -> ()
        }

    [<Fact>]
    member _.``E2E-02: Zero-State / 初回起動 - VIDEO_DIRS 未設定時のエラー詳細トースト表示`` () =
        task {
            let origVideoDirs = Environment.GetEnvironmentVariable("VIDEO_DIRS")
            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let tempBase = Path.Combine(Path.GetTempPath(), $"mv_e2e_02_{Guid.NewGuid():N}")
            let dbPath = Path.Combine(tempBase, "e2e_02.db")
            Directory.CreateDirectory(tempBase) |> ignore

            Environment.SetEnvironmentVariable("VIDEO_DIRS", null)
            Environment.SetEnvironmentVariable("VIDEO_DIR", null)

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                ()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))

            try
                do! host.StartAsync()
                let! page = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"[BROWSER] {msg.Text}"))

                let! _ = page.GotoAsync(url)

                let syncBtn = page.Locator("button:has-text('同期')")
                do! syncBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible))
                let! _ = page.WaitForSelectorAsync("button:has-text('同期'):not([disabled])")

                let scanResponseTask = page.WaitForResponseAsync(fun resp -> resp.Url.Contains("/api/scan") && resp.Status = 500)
                do! syncBtn.ClickAsync()
                let! scanResp = scanResponseTask
                scanResp.Status |> should equal 500

                // トーストが表示されるのを待機
                let toast = page.Locator("div[x-show='toast.show']")
                do! toast.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // 固定文のアサート
                let! toastText = toast.InnerTextAsync()
                toastText.Contains("スキャン同期に失敗しました。") |> should be True

                // エラー詳細 monospace 領域のアサート
                let detailElement = toast.Locator("div.font-mono")
                do! detailElement.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! detailText = detailElement.InnerTextAsync()
                detailText.Contains("VIDEO_DIRS") |> should be True
                detailText.Contains(@"VIDEO_DIRS=D:\Anime;F:\Movie") |> should be True

                // スクリーンショット保存
                let resultsDir = getTestResultsDir ()
                let screenshotPath = Path.Combine(resultsDir, "e2e-02-zero-state-error.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = screenshotPath, FullPage = Nullable true))
                File.Exists(screenshotPath) |> should be True

                do! browser.CloseAsync()
            finally
                playwright.Dispose()
                host.Dispose()
                Environment.SetEnvironmentVariable("VIDEO_DIRS", origVideoDirs)
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                if Directory.Exists(tempBase) then
                    try Directory.Delete(tempBase, true) with _ -> ()
        }

    [<Fact>]
    member _.``E2E-03: Negative / Recovery Path - エラー後の設定修正によるスキャン成功と動画表示`` () =
        task {
            let origVideoDirs = Environment.GetEnvironmentVariable("VIDEO_DIRS")
            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let tempBase = Path.Combine(Path.GetTempPath(), $"mv_e2e_03_{Guid.NewGuid():N}")
            let recoveryDir = Path.Combine(tempBase, "RecoveredVideos")
            let dbPath = Path.Combine(tempBase, "e2e_03.db")
            Directory.CreateDirectory(recoveryDir) |> ignore

            let recoveryFile = Path.Combine(recoveryDir, "recovered.mp4")
            File.WriteAllBytes(recoveryFile, [| 0uy; 0uy; 0uy; 0uy |])

            // 最初は VIDEO_DIRS を未設定にする
            Environment.SetEnvironmentVariable("VIDEO_DIRS", null)
            Environment.SetEnvironmentVariable("VIDEO_DIR", null)

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                ()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))

            try
                do! host.StartAsync()
                let! page = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"[BROWSER] {msg.Text}"))

                let! _ = page.GotoAsync(url)

                let syncBtn = page.Locator("button:has-text('同期')")
                do! syncBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible))
                let! _ = page.WaitForSelectorAsync("button:has-text('同期'):not([disabled])")

                // 1. 未設定状態でスキャン実行してエラーを確認
                let scanErrorTask = page.WaitForResponseAsync(fun resp -> resp.Url.Contains("/api/scan") && resp.Status = 500)
                do! syncBtn.ClickAsync()
                let! errResp = scanErrorTask
                errResp.Status |> should equal 500

                let toast = page.Locator("div[x-show='toast.show']")
                do! toast.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! errToastText = toast.InnerTextAsync()
                errToastText.Contains("スキャン同期に失敗しました。") |> should be True

                // 2. 環境変数 VIDEO_DIRS に有効な動画ディレクトリを設定（リカバリ）
                Environment.SetEnvironmentVariable("VIDEO_DIRS", recoveryDir)

                // 同期ボタンが再活性化するのを待機
                let! _ = page.WaitForSelectorAsync("button:has-text('同期'):not([disabled])")

                // 3. 再度「同期」ボタンをクリック
                let scanSuccessTask = page.WaitForResponseAsync(fun resp -> resp.Url.Contains("/api/scan") && resp.Status = 200)
                do! syncBtn.ClickAsync()
                let! successResp = scanSuccessTask
                successResp.Status |> should equal 200

                // 4. スキャン成功により動画一覧が表示されることをアサート
                let gridContainer = page.Locator("div[x-show=\"viewMode === 'grid'\"]").Nth(1)
                let videoCards = gridContainer.Locator(".group")
                do! videoCards.First.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 10000.0f))
                let! count = videoCards.CountAsync()
                count |> should equal 1

                // スクリーンショット保存
                let resultsDir = getTestResultsDir ()
                let screenshotPath = Path.Combine(resultsDir, "e2e-03-recovery-path.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = screenshotPath, FullPage = Nullable true))
                File.Exists(screenshotPath) |> should be True

                do! browser.CloseAsync()
            finally
                playwright.Dispose()
                host.Dispose()
                Environment.SetEnvironmentVariable("VIDEO_DIRS", origVideoDirs)
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                if Directory.Exists(tempBase) then
                    try Directory.Delete(tempBase, true) with _ -> ()
        }

    [<Fact>]
    member _.``E2E-04: UIモック忠実性 - エラー詳細トーストのフォント・配色・スタイルおよび閉じるボタン検証`` () =
        task {
            let origVideoDirs = Environment.GetEnvironmentVariable("VIDEO_DIRS")
            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let tempBase = Path.Combine(Path.GetTempPath(), $"mv_e2e_04_{Guid.NewGuid():N}")
            let dbPath = Path.Combine(tempBase, "e2e_04.db")
            Directory.CreateDirectory(tempBase) |> ignore

            Environment.SetEnvironmentVariable("VIDEO_DIRS", null)
            Environment.SetEnvironmentVariable("VIDEO_DIR", null)

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                ()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            let! playwright = Playwright.CreateAsync()
            let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))

            try
                do! host.StartAsync()
                let! page = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))

                page.Console.AddHandler(fun _ msg -> output.WriteLine($"[BROWSER] {msg.Text}"))

                let! _ = page.GotoAsync(url)

                let syncBtn = page.Locator("button:has-text('同期')")
                do! syncBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible))
                let! _ = page.WaitForSelectorAsync("button:has-text('同期'):not([disabled])")

                let scanResponseTask = page.WaitForResponseAsync(fun resp -> resp.Url.Contains("/api/scan") && resp.Status = 500)
                do! syncBtn.ClickAsync()
                let! scanResp = scanResponseTask
                scanResp.Status |> should equal 500

                // トーストが表示されるのを待機
                let toast = page.Locator("div[x-show='toast.show']")
                do! toast.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // エラー詳細 monospace 領域の検証
                let detailElement = toast.Locator("div.font-mono")
                do! detailElement.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                let! classAttr = detailElement.GetAttributeAsync("class")
                // mockup.html 仕様:
                // class="mt-1 text-xs text-slate-300 font-mono bg-black/40 p-2 rounded border border-white/10 break-all leading-relaxed whitespace-pre-wrap"
                classAttr.Contains("font-mono") |> should be True
                classAttr.Contains("bg-black/40") |> should be True
                classAttr.Contains("break-all") |> should be True
                classAttr.Contains("leading-relaxed") |> should be True
                classAttr.Contains("whitespace-pre-wrap") |> should be True

                // 閉じるボタンの検証
                let closeBtn = toast.Locator("button")
                let! closeBtnVisible = closeBtn.IsVisibleAsync()
                closeBtnVisible |> should be True

                // スクリーンショット保存（トースト表示状態）
                let resultsDir = getTestResultsDir ()
                let screenshotPath = Path.Combine(resultsDir, "e2e-04-mockup-fidelity.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = screenshotPath, FullPage = Nullable true))
                File.Exists(screenshotPath) |> should be True

                // 閉じるボタンをクリックしてトーストが非表示になることを確認
                do! closeBtn.ClickAsync()
                do! toast.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Hidden, Timeout = Nullable 5000.0f))
                let! isToastHidden = toast.IsHiddenAsync()
                isToastHidden |> should be True

                do! browser.CloseAsync()
            finally
                playwright.Dispose()
                host.Dispose()
                Environment.SetEnvironmentVariable("VIDEO_DIRS", origVideoDirs)
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                if Directory.Exists(tempBase) then
                    try Directory.Delete(tempBase, true) with _ -> ()
        }
