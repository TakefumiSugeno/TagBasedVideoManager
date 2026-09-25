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
open Dapper
open TagBasedVideoManager
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure

/// ルール検証用DTO
[<CLIMutable>]
type RuleDto = {
    Id: string
    Pattern: string
    TagId: string
    CreatedAt: string
}

type RuleTests () =
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_rules_{Guid.NewGuid():N}.db")
    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    interface IDisposable with
        member _.Dispose() =
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``自動タグ付けルールの作成・一覧取得・削除および既存動画への遡及適用が正常に動作する`` () =
        // 1. データベース初期化
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 2. テスト用データの準備 (既存動画とタグの登録)
        let testTag : Domain.Tag = { Id = "t_fsharp"; Name = "F#"; ColorCode = "#512bd4"; ParentId = None; VideoCount = 0 }
        let _ = Db.saveTag conn testTag |> Async.RunSynchronously

        let testVideo = {
            Id = "v_test_01"
            FileName = "Learning F# Programming.mp4"
            FilePath = "/videos/Learning F# Programming.mp4"
            Duration = 120L
            FileSize = 1048576L
            ThumbnailPath = None
            IsFavorite = false
            CreatedAt = DateTime.UtcNow
            AccessCount = 0
            LastAccessedAt = None
            Tags = []
        }
        let _ = Db.saveVideo conn testVideo |> Async.RunSynchronously

        // 3. WebHostBuilderの構築
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

        // --- 1. ルールの作成 (POST /api/rules) ---
        let payload = {| pattern = "F#"; tagId = "t_fsharp" |}
        let content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json")
        let response = client.PostAsync("/api/rules", content).Result
        response.StatusCode |> should equal HttpStatusCode.OK

        let ruleJson = response.Content.ReadAsStringAsync().Result
        let rule = JsonSerializer.Deserialize<RuleDto>(ruleJson, jsonOptions)
        rule.Pattern |> should equal "F#"
        rule.TagId |> should equal "t_fsharp"

        // --- 2. ルール一覧の取得 (GET /api/rules) ---
        let listResponse = client.GetAsync("/api/rules").Result
        listResponse.StatusCode |> should equal HttpStatusCode.OK
        
        let listJson = listResponse.Content.ReadAsStringAsync().Result
        let rules = JsonSerializer.Deserialize<RuleDto list>(listJson, jsonOptions)
        rules.Length |> should be (greaterThanOrEqualTo 1)
        rules |> List.exists (fun r -> r.Id = rule.Id) |> should be True

        // --- 3. 遡及適用の確認 ---
        // 非同期での遡及適用を待つため、最大5秒間ポーリングでDB内の紐付けを確認する
        let mutable hasApplied = false
        let limit = DateTime.UtcNow.AddSeconds(5.0)
        use checkConn = Db.getConnection dbPath
        checkConn.Open()
        while not hasApplied && DateTime.UtcNow < limit do
            let count = checkConn.ExecuteScalar<int>("SELECT COUNT(*) FROM VideoTags WHERE VideoId = 'v_test_01' AND TagId = 't_fsharp'")
            if count > 0 then
                hasApplied <- true
            else
                System.Threading.Thread.Sleep(200)

        hasApplied |> should be True

        // --- 4. ルールの削除 (DELETE /api/rules/{id}) ---
        let deleteResponse = client.DeleteAsync($"/api/rules/{rule.Id}").Result
        deleteResponse.StatusCode |> should equal HttpStatusCode.NoContent

        // 削除後の取得確認
        let listResponse2 = client.GetAsync("/api/rules").Result
        let listJson2 = listResponse2.Content.ReadAsStringAsync().Result
        let rules2 = JsonSerializer.Deserialize<RuleDto list>(listJson2, jsonOptions)
        rules2 |> List.exists (fun r -> r.Id = rule.Id) |> should be False
