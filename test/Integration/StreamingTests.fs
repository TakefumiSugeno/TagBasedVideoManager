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

type StreamingTests () =
    let dummyVideoPath = Path.Combine(AppContext.BaseDirectory, "stream_test.mp4")
    
    // テスト用の100バイトのダミー動画データをファイルとして書き出す
    let createDummyVideoFile () =
        if not (File.Exists(dummyVideoPath)) then
            let dummyBytes = Array.init 100 (fun i -> byte i)
            File.WriteAllBytes(dummyVideoPath, dummyBytes)

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
                .ConfigureServices(Program.configureServices)
                .Configure(fun app ->
                    // テスト用に、本来のパス解決をこのダミー動画パスに差し替えるハンドラーを構成するか、
                    // もしくはテスト用のルーティングを設定する
                    // プロダクションコードのHttpHandlersをそのまま検証するため、
                    // テストサーバーの起動引数に動画パスのダミー解決を組み込みます。
                    Program.configureApp app
                )

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
                .ConfigureServices(Program.configureServices)
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
                .ConfigureServices(Program.configureServices)
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

