namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Xunit
open FsUnit
open Giraffe
open TagBasedVideoManager
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure

type StreamingTests () =
    let dummyVideoPath = Path.Combine(AppContext.BaseDirectory, "stream_test.mp4")
    
    // テスト用の100バイトのダミー動画データをファイルとして書き出す
    let createDummyVideoFile () =
        if not (File.Exists(dummyVideoPath)) then
            let dummyBytes = Array.init 100 (fun i -> byte i)
            File.WriteAllBytes(dummyVideoPath, dummyBytes)

    let configureServicesWithDummyVideo (services: IServiceCollection) =
        Program.configureServices services
        use sp = services.BuildServiceProvider()
        use conn = sp.GetRequiredService<System.Data.IDbConnection>()
        DbInit.initializeDatabase conn |> ignore
        let video = {
            Id = "test_id"
            FileName = "stream_test.mp4"
            FilePath = dummyVideoPath
            Duration = 10L
            FileSize = 100L
            ThumbnailPath = None
            IsFavorite = false
            CreatedAt = DateTime.UtcNow
            FileModifiedAt = DateTime.UtcNow
            AccessCount = 0
            LastAccessedAt = None
            Tags = []
            FileUuid = None
        }
        Db.saveVideo conn video |> Async.RunSynchronously |> ignore

    interface IDisposable with
        member _.Dispose() =
            if File.Exists(dummyVideoPath) then
                try File.Delete(dummyVideoPath) with | _ -> ()

    [<Fact>]
    member _.``動画ストリーミングにおいてRange指定リクエストに対してHTTP206で部分バイナリを返却する`` () =
        createDummyVideoFile()

        // 1. WebHostBuilderの構築 (プロダクションコードの設定を流用し、ダミー動画パスを紐付ける)
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(configureServicesWithDummyVideo)
                .Configure(Program.configureApp)

        // 2. テストサーバーの起動
        use server = new TestServer(builder)
        use client = server.CreateClient()

        // 3. Rangeヘッダー (bytes=0-9) を付与してGETリクエスト送信
        let request = new HttpRequestMessage(HttpMethod.Get, "/api/videos/test_id/stream")
        request.Headers.Range <- RangeHeaderValue(0L, 9L)
        
        let response = client.SendAsync(request).Result

        // 4. アサーション (最初は未実装のため404または200になり、206にはならないため失敗する)
        response.StatusCode |> should equal HttpStatusCode.PartialContent
        response.Content.Headers.ContentLength.Value |> should equal 10L
        response.Content.Headers.ContentRange.ToString() |> should equal "bytes 0-9/100"
        
        let bytes = response.Content.ReadAsByteArrayAsync().Result
        bytes.Length |> should equal 10
        bytes.[0] |> should equal 0uy
        bytes.[9] |> should equal 9uy

    [<Fact>]
    member _.``Range指定のないGETリクエストに対してHTTP200でファイル全体を返却する`` () =
        // 境界値: Rangeヘッダーなしの通常アクセス
        createDummyVideoFile()

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(configureServicesWithDummyVideo)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/api/videos/test_id/stream").Result

        response.StatusCode |> should equal HttpStatusCode.OK
        response.Content.Headers.ContentLength.Value |> should equal 100L
        
        let bytes = response.Content.ReadAsByteArrayAsync().Result
        bytes.Length |> should equal 100

    [<Fact>]
    member _.``ファイルサイズ境界を超えるRange指定リクエストに対して実質的な残存部分をHTTP206で返却する`` () =
        // 境界値/異常値: bytes=95-120 (ファイルサイズ100を超える指定)
        createDummyVideoFile()

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(configureServicesWithDummyVideo)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let request = new HttpRequestMessage(HttpMethod.Get, "/api/videos/test_id/stream")
        request.Headers.Range <- RangeHeaderValue(95L, 120L) // 95から120（サイズ上限は100）
        
        let response = client.SendAsync(request).Result

        // アサーション: 206で返り、サイズは 95-99 の 5バイト、Content-Range は bytes 95-99/100
        response.StatusCode |> should equal HttpStatusCode.PartialContent
        response.Content.Headers.ContentLength.Value |> should equal 5L
        response.Content.Headers.ContentRange.ToString() |> should equal "bytes 95-99/100"
        
        let bytes = response.Content.ReadAsByteArrayAsync().Result
        bytes.Length |> should equal 5
        bytes.[0] |> should equal 95uy
        bytes.[4] |> should equal 99uy

    [<Fact>]
    member _.``IT-WEB-03: Linux 255バイト制限超過動画に対するストリーム要求に対してTestHost経由でHTTP422と専用エラーコードを返却する`` () =
        let longFileName = String('あ', 86) + ".mp4"
        let longVideoId = "vid_long_test_host"
        let longVideoPath = Path.Combine(AppContext.BaseDirectory, longFileName)

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Program.configureServices services
                    use sp = services.BuildServiceProvider()
                    use conn = sp.GetRequiredService<System.Data.IDbConnection>()
                    DbInit.initializeDatabase conn |> ignore
                    let video = {
                        Id = longVideoId
                        FileName = longFileName
                        FilePath = longVideoPath
                        Duration = 120L
                        FileSize = 100L
                        ThumbnailPath = None
                        IsFavorite = false
                        CreatedAt = DateTime.UtcNow
                        FileModifiedAt = DateTime.UtcNow
                        AccessCount = 0
                        LastAccessedAt = None
                        Tags = []
                        FileUuid = None
                    }
                    Db.saveVideo conn video |> Async.RunSynchronously |> ignore
                )
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync($"/api/videos/{longVideoId}/stream").Result

        response.StatusCode |> should equal HttpStatusCode.UnprocessableEntity
        let body = response.Content.ReadAsStringAsync().Result
        body.Contains("\"code\":\"FILE_NAME_TOO_LONG\"") |> should equal true
        body.Contains("255") |> should equal true

    [<Fact>]
    member _.``UT-18 / IT-09: DB登録済みだが物理ファイルが存在しない動画へのストリーム要求に対してHTTP500にならずHTTP404とFILE_NOT_FOUNDを返却する`` () =
        let missingVideoId = "vid_missing_test"
        let missingVideoPath = Path.Combine(AppContext.BaseDirectory, "non_existent_video_12345.mp4")
        if File.Exists(missingVideoPath) then
            File.Delete(missingVideoPath)

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Program.configureServices services
                    use sp = services.BuildServiceProvider()
                    use conn = sp.GetRequiredService<System.Data.IDbConnection>()
                    DbInit.initializeDatabase conn |> ignore
                    let video = {
                        Id = missingVideoId
                        FileName = "non_existent_video_12345.mp4"
                        FilePath = missingVideoPath
                        Duration = 120L
                        FileSize = 100L
                        ThumbnailPath = None
                        IsFavorite = false
                        CreatedAt = DateTime.UtcNow
                        FileModifiedAt = DateTime.UtcNow
                        AccessCount = 0
                        LastAccessedAt = None
                        Tags = []
                        FileUuid = None
                    }
                    Db.saveVideo conn video |> Async.RunSynchronously |> ignore
                )
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync($"/api/videos/{missingVideoId}/stream").Result

        response.StatusCode |> should equal HttpStatusCode.NotFound
        let body = response.Content.ReadAsStringAsync().Result
        body.Contains("\"code\":\"FILE_NOT_FOUND\"") |> should equal true
        (body.Contains("non_existent_video_12345.mp4") || body.Contains(missingVideoPath)) |> should equal true

    [<Fact>]
    member _.``Adversarial QA: DB未登録の無効な動画IDに対するストリーム要求はダミーファイルへフォールバックせず404 FILE_NOT_FOUNDを返却する`` () =
        createDummyVideoFile()

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(configureServicesWithDummyVideo)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let nonExistentId = "non_existent_video_arbitrary_id_xyz"
        let response = client.GetAsync($"/api/videos/{nonExistentId}/stream").Result

        response.StatusCode |> should equal HttpStatusCode.NotFound
        let body = response.Content.ReadAsStringAsync().Result
        body.Contains("\"code\":\"FILE_NOT_FOUND\"") |> should equal true


