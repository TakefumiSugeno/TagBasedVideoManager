namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Builder
open Microsoft.Playwright
open Xunit
open FsUnit
open Microsoft.Extensions.DependencyInjection
open TagBasedVideoManager

/// <summary>
/// Web アプリケーションの描画待ちスピナー、オーバーレイ、Zero-State ガード機構および
/// エラー状態（Error-State）管理に関する先行失敗テスト (Red)。
/// タスク 1.2 (E2E テスト) および タスク 2.1 (Alpine.js / videoManager 状態管理テスト) を網羅する。
/// </summary>
type WebLoadingSpinnerE2ETests (output: ITestOutputHelper) =

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

    let captureScreenshot (page: IPage) filename =
        task {
            let resultsDir = ensureResultsDir ()
            let shotPath = Path.Combine(resultsDir, filename)
            let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = shotPath))
            output.WriteLine($"Saved screenshot to {shotPath}")
        }

    let waitForAlpineInit (page: IPage) =
        task {
            do! page.WaitForLoadStateAsync(LoadState.DOMContentLoaded)
            let! _ = page.WaitForSelectorAsync("body[x-data]")
            let! _ = page.WaitForFunctionAsync("() => typeof window.Alpine !== 'undefined' && typeof window.videoManager === 'function' && document.querySelector('[x-data]') && typeof Alpine.$data === 'function' && Alpine.$data(document.querySelector('[x-data]'))")
            do! Task.Delay(300)
            ()
        }

    // =========================================================================
    // タスク 2.1: Alpine.js / videoManager() 状態管理・非同期関数の先行テスト
    // =========================================================================

    [<Fact>]
    member _.``2.1-UT-01: videoManager は独立したローディング状態変数およびエラー変数を初期値として保持していること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_state_init_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                let! _ = page.GotoAsync(url)

                // Alpine.js が初期化されるまで待機
                do! waitForAlpineInit page

                // videoManager() のプロパティ存在と初期値を検証
                let! stateJson = page.EvaluateAsync<string>("""() => {
                    const vm = window.videoManager ? window.videoManager() : {};
                    return JSON.stringify({
                        hasIsLoadingVideos: typeof vm.isLoadingVideos === 'boolean',
                        isLoadingVideos: vm.isLoadingVideos,
                        hasIsLoadingTags: typeof vm.isLoadingTags === 'boolean',
                        isLoadingTags: vm.isLoadingTags,
                        hasIsLoadingAnalytics: typeof vm.isLoadingAnalytics === 'boolean',
                        isLoadingAnalytics: vm.isLoadingAnalytics,
                        hasLoadErrorVideos: typeof vm.loadErrorVideos !== 'undefined',
                        loadErrorVideos: vm.loadErrorVideos,
                        hasLoadErrorTags: typeof vm.loadErrorTags !== 'undefined',
                        loadErrorTags: vm.loadErrorTags,
                        hasLoadErrorAnalytics: typeof vm.loadErrorAnalytics !== 'undefined',
                        loadErrorAnalytics: vm.loadErrorAnalytics,
                        hasLatestVideoRequestId: typeof vm.latestVideoRequestId === 'number',
                        latestVideoRequestId: vm.latestVideoRequestId
                    });
                }""")

                use doc = JsonDocument.Parse(stateJson)
                let root = doc.RootElement

                // 状態プロパティの存在検証
                root.GetProperty("hasIsLoadingVideos").GetBoolean() |> should equal true
                root.GetProperty("isLoadingVideos").GetBoolean() |> should equal false

                root.GetProperty("hasIsLoadingTags").GetBoolean() |> should equal true
                root.GetProperty("isLoadingTags").GetBoolean() |> should equal false

                root.GetProperty("hasIsLoadingAnalytics").GetBoolean() |> should equal true
                root.GetProperty("isLoadingAnalytics").GetBoolean() |> should equal false

                root.GetProperty("hasLoadErrorVideos").GetBoolean() |> should equal true
                root.GetProperty("loadErrorVideos").ValueKind |> should equal JsonValueKind.Null

                root.GetProperty("hasLoadErrorTags").GetBoolean() |> should equal true
                root.GetProperty("loadErrorTags").ValueKind |> should equal JsonValueKind.Null

                root.GetProperty("hasLoadErrorAnalytics").GetBoolean() |> should equal true
                root.GetProperty("loadErrorAnalytics").ValueKind |> should equal JsonValueKind.Null

                root.GetProperty("hasLatestVideoRequestId").GetBoolean() |> should equal true
                root.GetProperty("latestVideoRequestId").GetInt32() |> should equal 0

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``2.1-UT-02: fetchVideos 実行時にローディング状態が遷移し、APIエラー時に loadErrorVideos がセットされること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_fetch_videos_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                let currentStatus = ref 200
                let currentBody = ref "[{\"id\": 1, \"title\": \"Test Video\"}]"
                do! page.RouteAsync("**/api/videos*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable !currentStatus,
                        ContentType = "application/json",
                        Body = !currentBody
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 1. 成功時の状態遷移検証
                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchVideos(); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingVideos && vm.loadErrorVideos === null && Array.isArray(vm.videos) && vm.videos.length === 1 && vm.videos[0].title === 'Test Video'; }")

                // 2. 失敗時の状態遷移検証 (500 エラー)
                currentStatus := 500
                currentBody := "Internal Server Error"

                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchVideos(); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingVideos && vm.loadErrorVideos !== null && vm.loadErrorVideos.status === 500; }")

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``2.1-UT-03: 連続した fetchVideos 実行時に latestVideoRequestId がインクリメントされ先行リクエスト完了でスピナーが解除されないこと`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_race_condition_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                let callIndex = ref 0
                let tcs1 = TaskCompletionSource<unit>()
                let tcs2 = TaskCompletionSource<unit>()

                do! page.RouteAsync("**/api/videos*", fun route ->
                    task {
                        let idx = System.Threading.Interlocked.Increment(&callIndex.contents)
                        if idx = 1 then
                            // 初回ロード用
                            do! route.FulfillAsync(RouteFulfillOptions(
                                Status = Nullable 200,
                                ContentType = "application/json",
                                Body = "[]"
                            ))
                        elif idx = 2 then
                            // テストリクエスト1 (遅延)
                            do! tcs1.Task
                            do! route.FulfillAsync(RouteFulfillOptions(
                                Status = Nullable 200,
                                ContentType = "application/json",
                                Body = "[{\"id\": 101, \"title\": \"Old Video\"}]"
                            ))
                        else
                            // テストリクエスト2 (最新)
                            do! tcs2.Task
                            do! route.FulfillAsync(RouteFulfillOptions(
                                Status = Nullable 200,
                                ContentType = "application/json",
                                Body = "[{\"id\": 202, \"title\": \"New Video\"}]"
                            ))
                    } :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 2つのリクエストを発行
                let! ids = page.EvaluateAsync<int[]>("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    window.__p1 = vm.fetchVideos();
                    const id1 = vm.latestVideoRequestId;
                    window.__p2 = vm.fetchVideos();
                    const id2 = vm.latestVideoRequestId;
                    return [id1, id2];
                }""")

                // IDが単調増加していること
                ids.[1] |> should be (greaterThan ids.[0])

                // リクエスト1 (先行) を先に完了させる
                tcs1.SetResult(())
                let! _ = page.EvaluateAsync("() => window.__p1")

                // リクエスト2 が実行中なので、スピナーは維持されていること (isLoadingVideos = true)
                let! isStillLoading = page.EvaluateAsync<bool>("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    return vm.isLoadingVideos === true;
                }""")
                isStillLoading |> should equal true

                // リクエスト2 (最新) を完了させる
                tcs2.SetResult(())
                let! _ = page.EvaluateAsync("() => window.__p2")

                // 最新リクエスト完了によりスピナーが解除され、結果はリクエスト2 (id: 202) のみ反映されていること
                let! results = page.EvaluateAsync<bool[]>("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    return [
                        vm.isLoadingVideos,
                        vm.videos.some(v => v.id === 202),
                        vm.videos.some(v => v.id === 101)
                    ];
                }""")

                results.[0] |> should equal false
                results.[1] |> should equal true
                results.[2] |> should equal false

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``2.1-UT-04: fetchTags 実行時にローディング状態が遷移し、APIエラー時に loadErrorTags がセットされること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_fetch_tags_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                let currentStatus = ref 200
                let currentBody = ref "[{\"id\": 1, \"name\": \"TagA\"}]"
                do! page.RouteAsync("**/api/tags*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable !currentStatus,
                        ContentType = "application/json",
                        Body = !currentBody
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 1. 成功時の状態遷移検証
                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchTags(); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingTags && vm.loadErrorTags === null && Array.isArray(vm.tags) && vm.tags.length === 1; }")

                // 2. 失敗時の状態遷移検証 (500 エラー)
                currentStatus := 500
                currentBody := "Internal Server Error"

                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchTags(); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingTags && vm.loadErrorTags !== null && vm.loadErrorTags.status === 500; }")

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``2.1-UT-05: fetchAnalytics 関数の提供および実行時のローディング・エラー状態遷移`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_fetch_analytics_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                let currentStatus = ref 200
                let currentBody = ref "[]"
                do! page.RouteAsync("**/api/analysis/*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable !currentStatus,
                        ContentType = "application/json",
                        Body = !currentBody
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 1. 成功時の状態遷移検証
                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchAnalytics('ranking'); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingAnalytics && vm.loadErrorAnalytics === null; }")

                // 2. 失敗時の状態遷移検証 (500 エラー)
                currentStatus := 500
                currentBody := "Internal Server Error"

                let! _ = page.EvaluateAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); vm.fetchAnalytics('ranking'); }")
                let! _ = page.WaitForFunctionAsync("() => { const vm = Alpine.$data(document.querySelector('[x-data]')); return vm && !vm.isLoadingAnalytics && vm.loadErrorAnalytics !== null && vm.loadErrorAnalytics.status === 500; }")

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``2.1-UT-06: fetchWithTimeout はタイムアウト時に 408 TimeoutError を送出し、外部 signal 中断時は AbortError を維持すること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"vm_fetch_timeout_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                let! result = page.EvaluateAsync<bool>("""async () => {
                    if (typeof window.fetchWithTimeout !== 'function') return false;

                    const origFetch = window.fetch;
                    try {
                        const createAbortingFetch = () => (input, opts) => new Promise((resolve, reject) => {
                            if (opts && opts.signal) {
                                if (opts.signal.aborted) {
                                    const err = new Error('The user aborted a request.');
                                    err.name = 'AbortError';
                                    return reject(err);
                                }
                                opts.signal.addEventListener('abort', () => {
                                    const err = new Error('The user aborted a request.');
                                    err.name = 'AbortError';
                                    reject(err);
                                });
                            }
                        });

                        // 1. タイムアウト時の検証 (50ms でタイムアウト)
                        window.fetch = createAbortingFetch();
                        let caughtTimeout = false;
                        try {
                            await window.fetchWithTimeout('/test-timeout', { timeout: 50 });
                        } catch (err) {
                            if (err.name === 'TimeoutError' && err.status === 408) {
                                caughtTimeout = true;
                            }
                        }

                        // 2. 外部手動キャンセル時の検証 (TimeoutError ではなく AbortError のままであること)
                        let caughtManualAbort = false;
                        const externalCtrl = new AbortController();
                        const fetchPromise = window.fetchWithTimeout('/test-abort', {
                            timeout: 2000,
                            signal: externalCtrl.signal
                        });
                        setTimeout(() => externalCtrl.abort(), 20);
                        try {
                            await fetchPromise;
                        } catch (err) {
                            if (err.name === 'AbortError' && err.status !== 408) {
                                caughtManualAbort = true;
                            }
                        }

                        return caughtTimeout && caughtManualAbort;
                    } finally {
                        window.fetch = origFetch;
                    }
                }""")

                result |> should equal true
                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    // =========================================================================
    // タスク 1.2: Playwright E2E 画面検証テスト (遅延・モック・Zero/Error-State)
    // =========================================================================

    [<Fact>]
    member _.``1.2-E2E-01: 動画一覧の初回ロード遅延中にスピナーが表示され Zero-State が完全に抑止されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_video_loading_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // /api/videos を 2000ms 遅延させて空配列を返す
                do! page.RouteAsync("**/api/videos*", fun route ->
                    task {
                        do! Task.Delay(2000)
                        do! route.FulfillAsync(RouteFulfillOptions(
                            Status = Nullable 200,
                            ContentType = "application/json",
                            Body = "[]"
                        ))
                    } :> Task
                )

                let! _ = page.GotoAsync(url)

                // 1. 初回ロード中スピナー（または読み込み中テキスト）が表示されていること
                let spinner = page.Locator("[data-testid='videos-loading-spinner'], [data-testid='video-loading-spinner'], :text('動画ライブラリを読み込み中...'), :text('動画を読み込み中...')")
                let! isSpinnerVisible = spinner.First.IsVisibleAsync()

                // 2. 「動画が見つかりませんでした」という Zero-State が非表示であること
                let zeroState = page.Locator("main [data-testid='video-zero-state'], main :text('動画が見つかりませんでした')").First
                let! isZeroVisible = zeroState.IsVisibleAsync()

                do! captureScreenshot page "E2E-01_Initial_Loading_Spinner.png"

                isSpinnerVisible |> should equal true
                isZeroVisible |> should equal false

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-02: ディレクトリ同期実行中 (isScanning=true) に動画 0 件であっても Zero-State が抑止されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_scanning_guard_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // isScanning = true, videos = [] の状態を明示的にセット
                let! _ = page.EvaluateAsync("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    vm.videos = [];
                    vm.isScanning = true;
                }""")

                // 同期中スピナーまたはインジケータが表示されていること
                let scanningIndicator = page.Locator(":text('動画ディレクトリを同期中...'), :text('同期中...')")
                let! isScanningVisible = scanningIndicator.First.IsVisibleAsync()

                // Zero-State 「動画が見つかりませんでした」が非表示であること
                let zeroState = page.Locator("main [data-testid='video-zero-state'], main :text('動画が見つかりませんでした')").First
                let! isZeroVisible = zeroState.IsVisibleAsync()

                do! captureScreenshot page "E2E-02_Scanning_Zero_State_Suppression.png"

                isScanningVisible |> should equal true
                isZeroVisible |> should equal false

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-03: 動画 0 件の正常ロード完了時に Zero-State とディレクトリ同期回復ボタンが表示されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_zero_state_scan_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // 動画 0 件を返却
                do! page.RouteAsync("**/api/videos*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable 200,
                        ContentType = "application/json",
                        Body = "[]"
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 「動画が見つかりませんでした」が表示されるのを待機
                let zeroState = page.Locator("main [data-testid='video-zero-state'], main :text('動画が見つかりませんでした')").First
                do! zeroState.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))

                // 「ディレクトリ同期を実行する」ボタンが表示されていること
                let scanBtn = page.Locator("[data-testid='zero-state-scan-btn'], button:has-text('ディレクトリ同期を実行する')")
                let! isScanBtnVisible = scanBtn.First.IsVisibleAsync()

                do! captureScreenshot page "E2E-03_Zero_State_With_Scan_Button.png"

                isScanBtnVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-04: 検索結果 0 件時に検索条件クリア回復ボタンが表示されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_zero_state_clear_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 検索クエリが存在し、動画が 0 件の状態
                let! _ = page.EvaluateAsync("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    vm.searchQuery = 'nonexistent_test_query';
                    vm.videos = [];
                    vm.isLoadingVideos = false;
                    vm.isScanning = false;
                    vm.loadErrorVideos = null;
                }""")

                // 「検索条件をクリアする」ボタンが表示されるのを待機
                let clearBtn = page.Locator("[data-testid='zero-state-clear-btn']")
                do! clearBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! isClearBtnVisible = clearBtn.IsVisibleAsync()

                do! captureScreenshot page "E2E-04_Zero_State_With_Clear_Button.png"

                isClearBtnVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-05: 検索実行時に半透明オーバーレイが表示され完了後に結果が描画されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_search_overlay_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // 初期状態として動画 1 件を返却
                let initialVideosJson = "[{\"id\":1,\"fileName\":\"video1.mp4\",\"filePath\":\"/video1.mp4\",\"fileSizeBytes\":1000,\"durationSeconds\":60,\"fileModifiedAt\":\"2026-01-01T00:00:00Z\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"accessCount\":0,\"isFavorite\":false,\"tags\":[]}]"

                let isSecondCall = ref false
                do! page.RouteAsync("**/api/videos*", fun route ->
                    task {
                        if not !isSecondCall then
                            do! route.FulfillAsync(RouteFulfillOptions(
                                Status = Nullable 200,
                                ContentType = "application/json",
                                Body = initialVideosJson
                            ))
                        else
                            // 検索実行時は 2000ms 遅延
                            do! Task.Delay(2000)
                            do! route.FulfillAsync(RouteFulfillOptions(
                                Status = Nullable 200,
                                ContentType = "application/json",
                                Body = initialVideosJson
                            ))
                    } :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 初回ロード完了（動画が 1 件描画された状態）を待機
                let! _ = page.WaitForFunctionAsync("() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    return vm && vm.videos && vm.videos.length > 0 && !vm.isLoadingVideos;
                }")

                // 2回目の呼び出し（検索）モードに切り替え
                isSecondCall := true

                // 検索バーに入力して検索開始
                let searchInput = page.Locator("input[x-model='searchQuery']:visible, input[placeholder*='ツーリング'], input[placeholder*='検索']").First
                do! searchInput.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                do! searchInput.FillAsync("test")

                // 一覧上に半透明オーバーレイが表示されるのを待機
                let overlay = page.Locator("[data-testid='videos-overlay']")
                do! overlay.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! isOverlayVisible = overlay.IsVisibleAsync()

                do! captureScreenshot page "E2E-05_Search_Overlay.png"

                isOverlayVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-06: オーバーレイ表示中に背後操作が抑止されること (pointer-events-auto)`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_overlay_block_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))
                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 動画一覧が存在し、isLoadingVideos = true の状態
                let! _ = page.EvaluateAsync("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    vm.videos = [{ id: 1, title: 'Sample Video' }];
                    vm.isLoadingVideos = true;
                }""")

                // オーバーレイ要素が存在し、クリックを遮断するクラス（pointer-events-auto）を有していること
                let overlay = page.Locator("[data-testid='videos-overlay'], div.cursor-wait")
                let! isOverlayVisible = overlay.First.IsVisibleAsync()

                isOverlayVisible |> should equal true

                let! hasPointerEvents = overlay.First.EvaluateAsync<bool>("""el => {
                    const style = window.getComputedStyle(el);
                    return style.pointerEvents === 'auto' || el.classList.contains('pointer-events-auto');
                }""")

                hasPointerEvents |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-09: 動画一覧取得が HTTP 500 エラーとなった場合に Error-State と再試行ボタンが表示されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_videos_error_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // /api/videos を 500 エラーでモック
                do! page.RouteAsync("**/api/videos*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable 500,
                        Body = "Internal Server Error"
                    )) :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // エラー表示（「動画一覧の取得に失敗しました」または「動画の取得に失敗しました」）が表示されること
                let errorState = page.Locator("[data-testid='videos-error-state'], :text('動画一覧の取得に失敗しました'), :text('動画の取得に失敗しました')")
                let! isErrorVisible = errorState.First.IsVisibleAsync()

                // 「再試行する」または「再試行」ボタンが表示されること
                let retryBtn = page.Locator("[data-testid='videos-retry-btn'], button:has-text('再試行する'), button:has-text('再試行')")
                let! isRetryBtnVisible = retryBtn.First.IsVisibleAsync()

                do! captureScreenshot page "E2E-09_Videos_Error_State.png"

                isErrorVisible |> should equal true
                isRetryBtnVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-10: タグ一覧の初回ロード遅延中にタグスピナーが表示され Zero-State が抑止されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_tags_loading_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // /api/tags を 2000ms 遅延
                do! page.RouteAsync("**/api/tags*", fun route ->
                    task {
                        do! Task.Delay(2000)
                        do! route.FulfillAsync(RouteFulfillOptions(
                            Status = Nullable 200,
                            ContentType = "application/json",
                            Body = "[]"
                        ))
                    } :> Task
                )

                let! _ = page.GotoAsync(url)

                // タグスピナー（または「タグ読み込み中...」）が表示されていること
                let tagSpinner = page.Locator("[data-testid='tags-loading-spinner'], :text('タグ読み込み中...')")
                let! isTagSpinnerVisible = tagSpinner.First.IsVisibleAsync()

                // 「登録されたタグはありません」が表示されていないこと
                let tagZero = page.Locator(":text('登録されたタグはありません')")
                let! isTagZeroVisible = tagZero.IsVisibleAsync()

                do! captureScreenshot page "E2E-10_Tags_Loading_Spinner.png"

                isTagSpinnerVisible |> should equal true
                isTagZeroVisible |> should equal false

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-12: タグ一覧取得が HTTP 500 エラーとなった場合にサイドバーに Error-State と再試行ボタンが表示されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_tags_error_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // /api/tags を 500 エラーでモック
                do! page.RouteAsync("**/api/tags*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable 500,
                        Body = "Internal Server Error"
                    ))
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // タグのエラー表示「タグの取得に失敗しました」が表示されること
                let tagError = page.Locator("[data-testid='tags-error-state'], :text('タグの取得に失敗しました')")
                let! isTagErrorVisible = tagError.First.IsVisibleAsync()

                // 再試行ボタンが表示されること
                let retryBtn = page.Locator("[data-testid='tags-retry-btn'], button:has-text('再試行')")
                let! isRetryBtnVisible = retryBtn.First.IsVisibleAsync()

                do! captureScreenshot page "E2E-12_Tags_Error_State.png"

                isTagErrorVisible |> should equal true
                isRetryBtnVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-13: 視聴分析モーダルを開いた際の非同期データ取得中にスピナーが表示され Zero-State が抑止されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_analytics_loading_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // 分析モーダル用 API を遅延
                do! page.RouteAsync("**/api/analysis/*", fun route ->
                    task {
                        do! Task.Delay(2000)
                        do! route.FulfillAsync(RouteFulfillOptions(
                            Status = Nullable 200,
                            ContentType = "application/json",
                            Body = "[]"
                        ))
                    } :> Task
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 分析モーダルを開く
                let! _ = page.EvaluateAsync("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    if (vm.openAnalyticsModal) {
                        vm.openAnalyticsModal();
                    }
                }""")

                // 分析スピナー（「分析データを読み込み中...」）が表示されるのを待機
                let analyticsSpinner = page.Locator("[data-testid='analytics-loading-spinner']")
                do! analyticsSpinner.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! isAnalyticsSpinnerVisible = analyticsSpinner.IsVisibleAsync()

                // 「再生履歴がまだありません」が表示されていないこと
                let analyticsZero = page.Locator(":text('再生履歴がまだありません')")
                let! isAnalyticsZeroVisible = analyticsZero.IsVisibleAsync()

                do! captureScreenshot page "E2E-13_Analytics_Loading_Spinner.png"

                isAnalyticsSpinnerVisible |> should equal true
                isAnalyticsZeroVisible |> should equal false

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task

    [<Fact>]
    member _.``1.2-E2E-15: 視聴分析データ取得が HTTP 500 エラーとなった場合に Error-State と再試行ボタンが表示されること`` () =
        task {
            let dbPath = Path.Combine(AppContext.BaseDirectory, $"e2e_analytics_error_{Guid.NewGuid():N}.db")
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
                page.Console.Add(fun msg -> output.WriteLine($"[BROWSER CONSOLE {msg.Type}] {msg.Text}"))
                page.PageError.Add(fun err -> output.WriteLine($"[BROWSER ERROR] {err}"))

                // 分析モーダル用 API を 500 エラーでモック
                do! page.RouteAsync("**/api/analysis/*", fun route ->
                    route.FulfillAsync(RouteFulfillOptions(
                        Status = Nullable 500,
                        Body = "Internal Server Error"
                    ))
                )

                let! _ = page.GotoAsync(url)
                do! waitForAlpineInit page

                // 分析モーダルを開く
                let! _ = page.EvaluateAsync("""() => {
                    const vm = Alpine.$data(document.querySelector('[x-data]'));
                    if (vm.openAnalyticsModal) {
                        vm.openAnalyticsModal();
                    }
                }""")

                // 分析エラー表示「分析データの取得に失敗しました」が表示されること
                let analyticsError = page.Locator("[data-testid='analytics-error-state']")
                do! analyticsError.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! isAnalyticsErrorVisible = analyticsError.IsVisibleAsync()

                // 再試行ボタンが表示されること
                let retryBtn = page.Locator("[data-testid='analytics-retry-btn']")
                do! retryBtn.WaitForAsync(LocatorWaitForOptions(State = WaitForSelectorState.Visible, Timeout = Nullable 5000.0f))
                let! isRetryBtnVisible = retryBtn.IsVisibleAsync()

                do! captureScreenshot page "E2E-15_Analytics_Error_State.png"

                isAnalyticsErrorVisible |> should equal true
                isRetryBtnVisible |> should equal true

                do! browser.CloseAsync()
            finally
                host.Dispose()
                if File.Exists(dbPath) then try File.Delete(dbPath) with _ -> ()
        } :> Task
