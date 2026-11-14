namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Xunit
open FsUnit
open TagBasedVideoManager
open TagBasedVideoManager.Infrastructure

type PortTests () =

    [<Fact>]
    member _.``PORT環境変数に指定されたポートで実Webサーバーが起動しアクセス可能である`` () =
        let origPort = Environment.GetEnvironmentVariable("PORT")
        let origUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
        let origDbPath = Environment.GetEnvironmentVariable("DATABASE_PATH")
        let testPort = "5099"
        let url = $"http://127.0.0.1:{testPort}"
        let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_port_{Guid.NewGuid():N}.db")

        try
            // 環境変数の設定
            Environment.SetEnvironmentVariable("PORT", testPort)
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", url)
            Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)

            // 1. データベースの初期化
            use conn = Db.getConnection dbPath
            let _ = DbInit.initializeDatabase conn |> ignore

            // 2. WebApplicationの構築
            let builder = WebApplication.CreateBuilder([||])
            Program.configureServices builder.Services
            use app = builder.Build()
            Program.configureApp app

            // 3. サーバーの起動 (環境変数 ASPNETCORE_URLS により自動でバインドされる)
            app.StartAsync().GetAwaiter().GetResult()

            try
                // 4. 実際のネットワーク経由でのアクセス検証
                use client = new HttpClient()
                let response = client.GetAsync($"{url}/api/ping").Result
                response.StatusCode |> should equal HttpStatusCode.OK
                
                let body = response.Content.ReadAsStringAsync().Result
                body |> should equal "pong"
            finally
                // 5. サーバー停止
                app.StopAsync().GetAwaiter().GetResult()
        finally
            // 6. 環境変数と一時DBのクリーンアップ・復元
            Environment.SetEnvironmentVariable("PORT", origPort)
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", origUrls)
            Environment.SetEnvironmentVariable("DATABASE_PATH", origDbPath)
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()
