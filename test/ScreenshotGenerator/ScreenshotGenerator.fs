namespace TagBasedVideoManager.Tests.E2E

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Builder
open Microsoft.Playwright
open Xunit
open Microsoft.Extensions.DependencyInjection
open TagBasedVideoManager

/// README.md に掲載するための画面キャプチャを自動的に取得して
/// doc/images/ ディレクトリ配下に保存する専用のツール（テストランナー経由で実行可能）
type ScreenshotGenerator () =

    // リポジトリルートを確実に特定するヘルパー
    static let getRepoRoot () =
        let rec findRoot (dir: DirectoryInfo) =
            if dir = null then None
            elif File.Exists(Path.Combine(dir.FullName, "TagBasedVideoManager.sln")) then Some dir.FullName
            else findRoot dir.Parent
        
        match findRoot (DirectoryInfo(Directory.GetCurrentDirectory())) with
        | Some r -> r
        | None ->
            // フォールバック: test ディレクトリから親を探す
            let mutable dir = DirectoryInfo(Directory.GetCurrentDirectory())
            while dir <> null && dir.Name <> "test" && dir.Parent <> null do
                dir <- dir.Parent
            if dir <> null && dir.Name = "test" then dir.Parent.FullName
            else Directory.GetCurrentDirectory()

    // 空いているランダムなポートを割り当てるヘルパー
    let getFreePort () =
        let listener = System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        listener.Start()
        let port = (listener.LocalEndpoint :?> System.Net.IPEndPoint).Port
        listener.Stop()
        port

    // テスト用の共通サーバー・ブラウザ起動処理
    let startTestServer dbPath =
        let port = getFreePort ()
        let url = $"http://localhost:{port}"
        let repoRoot = getRepoRoot ()
        let webRoot = Path.Combine(repoRoot, "src", "wwwroot")

        let host =
            Microsoft.AspNetCore.WebHost.CreateDefaultBuilder()
                .UseContentRoot(repoRoot)
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

    [<Fact>]
    member _.GenerateScreenshots() =
        task {
            let repoRoot = getRepoRoot ()
            // テンポラリ出力先の設定 (テストプロジェクト直下)
            let testWorkDir = Path.Combine(repoRoot, "test", "ScreenshotGenerator")
            if not (Directory.Exists(testWorkDir)) then
                Directory.CreateDirectory(testWorkDir) |> ignore

            let dbPath = Path.Combine(testWorkDir, $"screenshot_generator_{Guid.NewGuid():N}.db")
            
            // doc/images ディレクトリのパスを設定
            let docImagesDir = Path.Combine(repoRoot, "doc", "images")
            if not (Directory.Exists(docImagesDir)) then
                Directory.CreateDirectory(docImagesDir) |> ignore

            // 再生テスト用のダミーmp4ファイルを作成 (サイズ0バイトで問題なし)
            let dummyVideoFile1 = Path.Combine(testWorkDir, $"dummy_ani_1_{Guid.NewGuid():N}.mp4")
            let dummyVideoFile2 = Path.Combine(testWorkDir, $"dummy_sf_2_{Guid.NewGuid():N}.mp4")
            let dummyVideoFile3 = Path.Combine(testWorkDir, $"dummy_mus_3_{Guid.NewGuid():N}.mp4")
            File.WriteAllBytes(dummyVideoFile1, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile2, [| 0uy; 0uy; 0uy; 0uy |])
            File.WriteAllBytes(dummyVideoFile3, [| 0uy; 0uy; 0uy; 0uy |])

            do
                use conn = TagBasedVideoManager.Infrastructure.Db.getConnection dbPath
                conn.Open()
                let _ = TagBasedVideoManager.Infrastructure.DbInit.initializeDatabase conn |> ignore
                
                // 1. タグデータのテスト登録 (カラーコード付き)
                let tagBike = { TagBasedVideoManager.Domain.Tag.Id = "t_bike"; TagBasedVideoManager.Domain.Tag.Name = "バイク"; TagBasedVideoManager.Domain.Tag.ColorCode = "#ef4444"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let tagTouring = { TagBasedVideoManager.Domain.Tag.Id = "t_touring"; TagBasedVideoManager.Domain.Tag.Name = "ツーリング"; TagBasedVideoManager.Domain.Tag.ColorCode = "#3b82f6"; TagBasedVideoManager.Domain.Tag.ParentId = Some "t_bike"; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let tagTravel = { TagBasedVideoManager.Domain.Tag.Id = "t_travel"; TagBasedVideoManager.Domain.Tag.Name = "旅行"; TagBasedVideoManager.Domain.Tag.ColorCode = "#10b981"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                let tagHomeVideo = { TagBasedVideoManager.Domain.Tag.Id = "t_home_video"; TagBasedVideoManager.Domain.Tag.Name = "ホームビデオ"; TagBasedVideoManager.Domain.Tag.ColorCode = "#f59e0b"; TagBasedVideoManager.Domain.Tag.ParentId = None; TagBasedVideoManager.Domain.Tag.VideoCount = 0 }
                
                TagBasedVideoManager.Infrastructure.Db.saveTag conn tagBike |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTouring |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.saveTag conn tagTravel |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.saveTag conn tagHomeVideo |> Async.RunSynchronously |> ignore
                
                // 2. 動画データのテスト登録 (お気に入りフラグ、再生回数、タグ情報付き)
                let video1 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test1"
                    TagBasedVideoManager.Domain.Video.FileName = "2026年信州バイクツーリング.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile1
                    TagBasedVideoManager.Domain.Video.Duration = 5400L
                    TagBasedVideoManager.Domain.Video.FileSize = 104857600L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = true
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddDays(-2.0)
                    TagBasedVideoManager.Domain.Video.AccessCount = 15
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = Some (DateTime.UtcNow.AddHours(-1.0))
                    TagBasedVideoManager.Domain.Video.Tags = [tagBike; tagTouring]
                }
                let video2 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test2"
                    TagBasedVideoManager.Domain.Video.FileName = "沖縄旅行2日目_美ら海水族館.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile2
                    TagBasedVideoManager.Domain.Video.Duration = 3600L
                    TagBasedVideoManager.Domain.Video.FileSize = 524288000L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = false
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow.AddDays(-5.0)
                    TagBasedVideoManager.Domain.Video.AccessCount = 8
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = Some (DateTime.UtcNow.AddDays(-1.0))
                    TagBasedVideoManager.Domain.Video.Tags = [tagTravel]
                }
                let video3 = {
                    TagBasedVideoManager.Domain.Video.Id = "v_test3"
                    TagBasedVideoManager.Domain.Video.FileName = "子供の運動会2025.mp4"
                    TagBasedVideoManager.Domain.Video.FilePath = dummyVideoFile3
                    TagBasedVideoManager.Domain.Video.Duration = 1440L
                    TagBasedVideoManager.Domain.Video.FileSize = 209715200L
                    TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                    TagBasedVideoManager.Domain.Video.IsFavorite = true
                    TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                    TagBasedVideoManager.Domain.Video.AccessCount = 0
                    TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                    TagBasedVideoManager.Domain.Video.Tags = [tagHomeVideo]
                }
                
                TagBasedVideoManager.Infrastructure.Db.saveVideo conn video1 |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.saveVideo conn video2 |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.saveVideo conn video3 |> Async.RunSynchronously |> ignore
                
                // タグ中間テーブルの紐付け
                TagBasedVideoManager.Infrastructure.Db.addTagToVideo conn "v_test1" "t_bike" |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.addTagToVideo conn "v_test1" "t_touring" |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.addTagToVideo conn "v_test2" "t_travel" |> Async.RunSynchronously |> ignore
                TagBasedVideoManager.Infrastructure.Db.addTagToVideo conn "v_test3" "t_home_video" |> Async.RunSynchronously |> ignore
                
                // 3. 自動タグ付けルールのテスト登録
                use cmd = conn.CreateCommand()
                cmd.CommandText <- "INSERT INTO TaggingRules (Id, Pattern, TagId, MatchType, TargetField, MinSize, MaxSize, CreatedAt) VALUES ('r_test', 'ツーリング', 't_touring', 'partial', 'fileName', NULL, NULL, '2026-07-11T00:00:00Z')"
                cmd.ExecuteNonQuery() |> ignore

                // 4. スマートフォルダのテスト登録
                cmd.CommandText <- "INSERT INTO SmartFolders (Id, Name, Query, CreatedAt) VALUES ('sf_test', 'お気に入りツーリング', 'tag:ツーリング is:favorite', '2026-07-11T00:00:00Z')"
                cmd.ExecuteNonQuery() |> ignore

                // 5. 例外エラーログのダミー登録
                cmd.CommandText <- "INSERT INTO ErrorLogs (Message, StackTrace, CreatedAt) VALUES ('SQLite constraint violation: UNIQUE constraint failed', 'at TagBasedVideoManager.Infrastructure.Db.saveVideo...\n at TagBasedVideoManager.HttpHandlers...', '2026-07-11T04:00:00Z')"
                cmd.ExecuteNonQuery() |> ignore
                ()

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()

            let host, url = startTestServer dbPath
            try
                do! host.StartAsync()

                use! playwright = Playwright.CreateAsync()
                let! browser = playwright.Chromium.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
                // フルHD解像度でブラウザを開き、CSSレイアウトを整える
                let! page = browser.NewPageAsync(BrowserNewPageOptions(ViewportSize = ViewportSize(Width = 1920, Height = 1080)))
                
                // Kestrel サーバーのメイン画面へ遷移
                let! _ = page.GotoAsync(url)
                
                // サイドバーとライブラリ描画の待機
                let! _ = page.WaitForSelectorAsync("aside")
                do! page.WaitForTimeoutAsync(2500.0f) // レンダリングおよびCSSの安定化待機

                // --- 1. メイン画面 (screenshot_main.png) ---
                let mainScreenshotPath = Path.Combine(docImagesDir, "screenshot_main.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = mainScreenshotPath))
                
                // --- 2. 動画再生モーダル (screenshot_player.png) ---
                let videoTitle = page.Locator("h3:has-text('2026年信州バイクツーリング')").First
                do! videoTitle.ClickAsync()
                let playModalContainer = page.Locator("div[x-show='selectedVideo !== null']")
                do! playModalContainer.WaitForAsync()
                let videoEl = playModalContainer.Locator("video")
                do! videoEl.WaitForAsync()
                do! page.WaitForTimeoutAsync(1500.0f) // モーダルの出現フェードアニメーションを待つ
                
                let playerScreenshotPath = Path.Combine(docImagesDir, "screenshot_player.png")
                let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = playerScreenshotPath))

                // モーダルを閉じる
                let closeModalBtn = page.Locator("button[title='閉じる']").First
                let! closeBtnCount = closeModalBtn.CountAsync()
                if closeBtnCount > 0 then
                    do! closeModalBtn.ClickAsync()
                else
                    do! page.Keyboard.PressAsync("Escape")
                
                do! page.WaitForTimeoutAsync(1000.0f)

                // --- 3. 設定管理モーダル (screenshot_settings.png) ---
                let settingsBtn = page.Locator("button[title='設定データの一括インポート・エクスポート']").First
                let! settingsBtnCount = settingsBtn.CountAsync()
                if settingsBtnCount > 0 then
                    do! settingsBtn.ClickAsync()
                    let bulkModalContainer = page.Locator("div[x-show='bulkModal.show']")
                    do! bulkModalContainer.WaitForAsync()
                    do! page.WaitForTimeoutAsync(1500.0f)
                    
                    let settingsScreenshotPath = Path.Combine(docImagesDir, "screenshot_settings.png")
                    let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = settingsScreenshotPath))
                    
                    // 設定モーダルを閉じる (キャンセルボタンをクリック)
                    let cancelBtn = page.Locator("div[x-show='bulkModal.show'] button:has-text('キャンセル')").First
                    do! cancelBtn.ClickAsync()
                    do! page.WaitForTimeoutAsync(1000.0f)
                    ()

                // --- 4. 分析ダッシュボード (screenshot_dashboard.png) ---
                let statsBtn = page.Locator("button:has-text('統計・おすすめ')").First
                let! statsBtnCount = statsBtn.CountAsync()
                if statsBtnCount > 0 then
                    do! statsBtn.ClickAsync()
                    let! _ = page.WaitForSelectorAsync("div:has-text('ランキング')")
                    
                    // ダッシュボード内の例外エラーログタブを開き、スタックトレースを表示させる
                    let errorLogTab = page.Locator("button:has-text('例外エラーログ')").First
                    let! errorLogTabCount = errorLogTab.CountAsync()
                    if errorLogTabCount > 0 then
                        do! errorLogTab.ClickAsync()
                        do! page.WaitForTimeoutAsync(1000.0f) // ログアコーディオンの描画待機
                    
                    let dashboardScreenshotPath = Path.Combine(docImagesDir, "screenshot_dashboard.png")
                    let! _ = page.ScreenshotAsync(PageScreenshotOptions(Path = dashboardScreenshotPath))
                    ()

                do! browser.CloseAsync()
            finally
                host.Dispose()
                // テスト用一時ファイルのクリーンアップ
                if File.Exists(dbPath) then
                    try File.Delete(dbPath) with | _ -> ()
                if File.Exists(dummyVideoFile1) then
                    try File.Delete(dummyVideoFile1) with | _ -> ()
                if File.Exists(dummyVideoFile2) then
                    try File.Delete(dummyVideoFile2) with | _ -> ()
                if File.Exists(dummyVideoFile3) then
                    try File.Delete(dummyVideoFile3) with | _ -> ()
        }
