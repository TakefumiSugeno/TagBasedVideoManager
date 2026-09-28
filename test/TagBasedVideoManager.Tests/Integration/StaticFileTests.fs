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
