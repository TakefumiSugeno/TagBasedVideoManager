namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Xunit
open FsUnit
open TagBasedVideoManager
open TagBasedVideoManager.Infrastructure

/// テスト用のシリアライズ確認DTO
[<CLIMutable>]
type TagDto = {
    Id: string
    Name: string
    ColorCode: string
    ParentId: string
}

type TagTests () =
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_tags_{Guid.NewGuid():N}.db")

    // テスト用のJSONオプション
    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    interface IDisposable with
        member _.Dispose() =
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``タグの作成・一覧取得・階層更新・削除のCRUDが正常に動作する`` () =
        // 1. データベース初期化
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 2. WebHostBuilderの構築
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)
                    Program.configureServices services
                )
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        // --- 1. 親タグの作成 (POST /api/tags) ---
        let parentPayload = {| name = "バイク"; colorCode = "#ff0000"; parentId = null |}
        let parentContent = new StringContent(JsonSerializer.Serialize(parentPayload, jsonOptions), Encoding.UTF8, "application/json")
        let parentResponse = client.PostAsync("/api/tags", parentContent).Result
        parentResponse.StatusCode |> should equal HttpStatusCode.OK
        
        let parentJson = parentResponse.Content.ReadAsStringAsync().Result
        let parentTag = JsonSerializer.Deserialize<TagDto>(parentJson, jsonOptions)
        parentTag.Name |> should equal "バイク"
        parentTag.ColorCode |> should equal "#ff0000"
        parentTag.ParentId |> should be null

        // --- 2. 子タグの作成 (POST /api/tags) ---
        let childPayload = {| name = "ツーリング"; colorCode = "#00ff00"; parentId = parentTag.Id |}
        let childContent = new StringContent(JsonSerializer.Serialize(childPayload, jsonOptions), Encoding.UTF8, "application/json")
        let childResponse = client.PostAsync("/api/tags", childContent).Result
        childResponse.StatusCode |> should equal HttpStatusCode.OK
        
        let childJson = childResponse.Content.ReadAsStringAsync().Result
        let childTag = JsonSerializer.Deserialize<TagDto>(childJson, jsonOptions)
        childTag.Name |> should equal "ツーリング"
        childTag.ParentId |> should equal parentTag.Id

        // --- 3. タグ一覧の取得 (GET /api/tags) ---
        let listResponse = client.GetAsync("/api/tags").Result
        listResponse.StatusCode |> should equal HttpStatusCode.OK
        
        let listJson = listResponse.Content.ReadAsStringAsync().Result
        let tags = JsonSerializer.Deserialize<TagDto list>(listJson, jsonOptions)
        tags.Length |> should be (greaterThanOrEqualTo 2)
        
        let hasParent = tags |> List.exists (fun t -> t.Id = parentTag.Id)
        let hasChild = tags |> List.exists (fun t -> t.Id = childTag.Id && t.ParentId = parentTag.Id)
        hasParent |> should be True
        hasChild |> should be True

        // --- 4. タグ階層の更新 (PATCH /api/tags/{id}) ---
        // 子タグの親を null (親なし) に更新する
        let patchPayload = {| parentId = null |}
        let patchContent = new StringContent(JsonSerializer.Serialize(patchPayload, jsonOptions), Encoding.UTF8, "application/json")
        let patchResponse = client.PatchAsync($"/api/tags/{childTag.Id}", patchContent).Result
        patchResponse.StatusCode |> should equal HttpStatusCode.OK

        // 再取得して親がnullになっていることを検証
        let listResponse2 = client.GetAsync("/api/tags").Result
        let listJson2 = listResponse2.Content.ReadAsStringAsync().Result
        let tags2 = JsonSerializer.Deserialize<TagDto list>(listJson2, jsonOptions)
        let updatedChild = tags2 |> List.find (fun t -> t.Id = childTag.Id)
        updatedChild.ParentId |> should be null

        // --- 5. タグの削除 (DELETE /api/tags/{id}) ---
        let deleteResponse = client.DeleteAsync($"/api/tags/{parentTag.Id}").Result
        deleteResponse.StatusCode |> should equal HttpStatusCode.NoContent

        // 削除後の確認
        let listResponse3 = client.GetAsync("/api/tags").Result
        let listJson3 = listResponse3.Content.ReadAsStringAsync().Result
        let tags3 = JsonSerializer.Deserialize<TagDto list>(listJson3, jsonOptions)
        tags3 |> List.exists (fun t -> t.Id = parentTag.Id) |> should be False

    [<Fact>]
    member _.``動画に対する手動タグの紐付けおよび紐付け解除が正常に動作する`` () =
        // 1. データベース初期化
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // テストタグと動画の準備
        let testTag : Domain.Tag = { Id = "t_manual"; Name = "手動"; ColorCode = "#ffffff"; ParentId = None; VideoCount = 0 }
        let _ = Db.saveTag conn testTag |> Async.RunSynchronously

        let testVideo : Domain.Video = {
            Id = "v_manual_test"
            FileName = "Manual Video.mp4"
            FilePath = "/videos/Manual Video.mp4"
            Duration = 100L
            FileSize = 1000L
            ThumbnailPath = None
            IsFavorite = false
            CreatedAt = DateTime.UtcNow
            AccessCount = 0
            LastAccessedAt = None
            Tags = []
        }
        let _ = Db.saveVideo conn testVideo |> Async.RunSynchronously

        // 2. WebHostBuilderの構築
        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)
                    Program.configureServices services
                )
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        // --- 1. 動画へのタグ紐付け (POST /api/videos/{id}/tags) ---
        let payload = {| tagId = "t_manual" |}
        let content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json")
        let response = client.PostAsync($"/api/videos/v_manual_test/tags", content).Result
        response.StatusCode |> should equal HttpStatusCode.OK

        // 紐付いていることを確認 (GET /api/videos)
        let getResponse = client.GetAsync("/api/videos").Result
        let getJson = getResponse.Content.ReadAsStringAsync().Result
        // JSONデータ構造: list of 匿名型 (tags を含む)
        let doc = JsonDocument.Parse(getJson)
        let videoNode = doc.RootElement.EnumerateArray() |> Seq.find (fun v -> v.GetProperty("id").GetString() = "v_manual_test")
        let tagsNode = videoNode.GetProperty("tags").EnumerateArray() |> Seq.toList
        tagsNode.Length |> should equal 1
        tagsNode.[0].GetProperty("id").GetString() |> should equal "t_manual"

        // --- 2. タグの紐付け解除 (DELETE /api/videos/{id}/tags) ---
        // Giraffe の DELETE メソッドへのリクエストで Body を送信するために HttpRequestMessage を使用します
        let deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/videos/v_manual_test/tags")
        deleteRequest.Content <- new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json")
        let deleteResponse = client.SendAsync(deleteRequest).Result
        deleteResponse.StatusCode |> should equal HttpStatusCode.OK

        // 解除されていることを確認
        let getResponse2 = client.GetAsync("/api/videos").Result
        let getJson2 = getResponse2.Content.ReadAsStringAsync().Result
        let doc2 = JsonDocument.Parse(getJson2)
        let videoNode2 = doc2.RootElement.EnumerateArray() |> Seq.find (fun v -> v.GetProperty("id").GetString() = "v_manual_test")
        let tagsNode2 = videoNode2.GetProperty("tags").EnumerateArray() |> Seq.toList
        tagsNode2.Length |> should equal 0

