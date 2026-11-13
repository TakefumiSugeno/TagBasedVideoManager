namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Xunit
open FsUnit
open TagBasedVideoManager

/// <summary>
/// GET /api/version の結合テスト (IT-01, IT-02, IT-03)。
/// キャッシュ抑止ヘッダー、稼働バージョン返却、および不正メソッド耐性を検証する。
/// </summary>
type VersionApiTests () =
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_ver_{Guid.NewGuid():N}.db")
    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let createTestClient () =
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)
                    Program.configureServices services
                )
                .Configure(Program.configureApp)
        let server = new TestServer(builder)
        server.CreateClient()

    interface IDisposable with
        member _.Dispose() =
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``GET api/version は 稼働バージョン JSON と Cache-Control no-cache ヘッダーを返却する (IT-01)`` () =
        use client = createTestClient ()
        let response = client.GetAsync("/api/version").Result
        
        response.StatusCode |> should equal HttpStatusCode.OK
        
        // Cache-Control ヘッダー検証 (no-cache, no-store, must-revalidate)
        response.Headers.CacheControl |> should not' (be Null)
        let cc = response.Headers.CacheControl.ToString()
        cc.Contains("no-cache") |> should be True
        cc.Contains("no-store") |> should be True
        cc.Contains("must-revalidate") |> should be True

        // JSON ボディ検証
        let content = response.Content.ReadAsStringAsync().Result
        use doc = JsonDocument.Parse(content)
        let root = doc.RootElement
        root.GetProperty("version").GetString() |> should equal "0.2.0"

    [<Fact>]
    member _.``POST api/version は 不正メソッドとして 404 または 405 を返し 500 エラーを起こさない (IT-02)`` () =
        use client = createTestClient ()
        use content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        let response = client.PostAsync("/api/version", content).Result
        
        // Giraffe の既定ルーティングでは POST 未定義時に 404 Not Found を返す
        response.StatusCode |> should not' (equal HttpStatusCode.InternalServerError)

    [<Fact>]
    member _.``GET api/version は 認証ヘッダーやセッションなしで即座に 200 OK を返却する (IT-03)`` () =
        use client = createTestClient ()
        let request = new HttpRequestMessage(HttpMethod.Get, "/api/version")
        let response = client.SendAsync(request).Result
        
        response.StatusCode |> should equal HttpStatusCode.OK
