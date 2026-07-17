namespace TagBasedVideoManager.Tests

open System.Net
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Xunit
open FsUnit
open TagBasedVideoManager

type StaticFileTests () =

    [<Fact>]
    member _.``ルートパスへのGETリクエストはwwwroot配下のindex.htmlを正常に返却する`` () =
        // 1. WebHostBuilderの構築 (プロダクションコードのProgram設定を適用)
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseWebRoot(Path.Combine("..", "..", "..", "..", "src", "wwwroot")) // テスト実行位置からの相対
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        // 2. テストサーバーの起動
        use server = new TestServer(builder)
        use client = server.CreateClient()

        // 3. リクエストの送信
        let response = client.GetAsync("/index.html").Result
        
        // 4. アサーション (最初は Program.configureApp 内で UseStaticFiles がないため失敗する)
        response.StatusCode |> should equal HttpStatusCode.OK
        
        let content = response.Content.ReadAsStringAsync().Result
        content |> should contain "Tag-Based Video Manager"

    [<Fact>]
    member _.``ルートパスへのGETリクエストはデフォルトファイルマッピングによりindex.htmlを正常に返却する`` () =
        // 境界値: /index.html ではなく / へのアクセス
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseWebRoot(Path.Combine("..", "..", "..", "..", "src", "wwwroot"))
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        
        let content = response.Content.ReadAsStringAsync().Result
        content |> should contain "Tag-Based Video Manager"

    [<Fact>]
    member _.``存在しない静的ファイルへのGETリクエストは404エラーを返却する`` () =
        // 異常値/境界値: 存在しないパスへのアクセス
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseWebRoot(Path.Combine("..", "..", "..", "..", "src", "wwwroot"))
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/nonexistent.html").Result
        response.StatusCode |> should equal HttpStatusCode.NotFound
