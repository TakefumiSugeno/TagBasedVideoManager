namespace TagBasedVideoManager.Tests

open System
open System.Net
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Xunit
open FsUnit
open TagBasedVideoManager

type StaticFileTests () =

    let getContentAndWebRoot () =
        let baseDir = AppDomain.CurrentDomain.BaseDirectory
        let rec findProjectRoot (dir: DirectoryInfo) =
            if dir = null then AppDomain.CurrentDomain.BaseDirectory
            else
                let candidateSrc = Path.Combine(dir.FullName, "src", "TagBasedVideoManager")
                if Directory.Exists(candidateSrc) then dir.FullName
                elif dir.Parent <> null then findProjectRoot dir.Parent
                else dir.FullName
        let projectRoot = findProjectRoot (DirectoryInfo(baseDir))
        let contentRoot = Path.Combine(projectRoot, "src", "TagBasedVideoManager")
        let webRoot = Path.Combine(contentRoot, "wwwroot")
        contentRoot, webRoot

    [<Fact>]
    member _.``ルートパスへのGETリクエストはwwwroot配下のindex.htmlを正常に返却する`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/index.html").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        
        let content = response.Content.ReadAsStringAsync().Result
        content |> should contain "Tag-Based Video Manager"

    [<Fact>]
    member _.``ルートパスへのGETリクエストはデフォルトファイルマッピングによりindex.htmlを正常に返却する`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
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
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/nonexistent.html").Result
        response.StatusCode |> should equal HttpStatusCode.NotFound

    [<Fact>]
    member _.``favicon_icoへのGETリクエストは200を返却する (IT-FAV-01)`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/favicon.ico").Result
        response.StatusCode |> should equal HttpStatusCode.OK

    [<Fact>]
    member _.``favicon_svgへのGETリクエストは200およびimage_svg+xmlを返却する (IT-FAV-02)`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/favicon.svg").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        response.Content.Headers.ContentType.MediaType |> should equal "image/svg+xml"

    [<Fact>]
    member _.``apple-touch-icon_pngへのGETリクエストは200およびimage_pngを返却する (IT-FAV-03)`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/apple-touch-icon.png").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        response.Content.Headers.ContentType.MediaType |> should equal "image/png"

    [<Fact>]
    member _.``manifest_webmanifestへのGETリクエストは200およびapplication_manifest+jsonを返却する (IT-MAN-01)`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/manifest.webmanifest").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        response.Content.Headers.ContentType.MediaType |> should equal "application/manifest+json"

    [<Fact>]
    member _.``index.html内のサムネイル画像タグにloading=lazyとdecoding=asyncが設定されていること (E2E-01)`` () =
        let contentRoot, webRoot = getContentAndWebRoot ()
        let builder =
            WebHostBuilder()
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .ConfigureServices(Program.configureServices)
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        let response = client.GetAsync("/index.html").Result
        response.StatusCode |> should equal HttpStatusCode.OK
        let content = response.Content.ReadAsStringAsync().Result

        // サムネイル画像の lazy loading および async decoding 属性の存在を検証
        content |> should contain "loading=\"lazy\""
        content |> should contain "decoding=\"async\""
