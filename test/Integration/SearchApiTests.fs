namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
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
open Microsoft.Data.Sqlite


/// 動画取得検証用DTO (JSONデシリアライズ用)
[<CLIMutable>]
type VideoDto = {
    Id: string
    FileName: string
    FilePath: string
    Duration: int64
    FileSize: int64
    ThumbnailPath: string
    IsFavorite: bool
    CreatedAt: string
}

type SearchTests () =
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_search_{Guid.NewGuid():N}.db")
    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    interface IDisposable with
        member _.Dispose() =
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``階層化された親タグで検索した際に子および孫タグが付与された動画が再帰的にヒットする`` () =
        // 1. データベース初期化
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // バイク (親) -> ツーリング (子) -> キャンプツーリング (孫)
        let tagBike : Domain.Tag = { Id = "t_bike"; Name = "バイク"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
        let tagTouring : Domain.Tag = { Id = "t_touring"; Name = "ツーリング"; ColorCode = "#00ff00"; ParentId = Some "t_bike"; VideoCount = 0 }
        let tagCampTouring : Domain.Tag = { Id = "t_camp_touring"; Name = "キャンプツーリング"; ColorCode = "#0000ff"; ParentId = Some "t_touring"; VideoCount = 0 }
        let tagMusic : Domain.Tag = { Id = "t_music"; Name = "音楽"; ColorCode = "#ffff00"; ParentId = None; VideoCount = 0 }

        let _ = Db.saveTag conn tagBike |> Async.RunSynchronously
        let _ = Db.saveTag conn tagTouring |> Async.RunSynchronously
        let _ = Db.saveTag conn tagCampTouring |> Async.RunSynchronously
        let _ = Db.saveTag conn tagMusic |> Async.RunSynchronously

        // 3. 動画データの準備
        // 動画1 (2026年信州バイクツーリング.mp4) に孫タグ「キャンプツーリング」を付与
        let videoEva = {
            Id = "v_eva"
            FileName = "2026年信州バイクツーリング.mp4"
            FilePath = "/videos/2026年信州バイクツーリング.mp4"
            Duration = 7200L
            FileSize = 2048576000L
            ThumbnailPath = None
            IsFavorite = false
            CreatedAt = DateTime.UtcNow
            AccessCount = 0
            LastAccessedAt = None
            Tags = []
        }
        // 動画2 (Recital) に「音楽」タグを付与
        let videoRecital = {
            Id = "v_recital"
            FileName = "Recital.mp4"
            FilePath = "/videos/Recital.mp4"
            Duration = 3600L
            FileSize = 1048576000L
            ThumbnailPath = None
            IsFavorite = true
            CreatedAt = DateTime.UtcNow
            AccessCount = 0
            LastAccessedAt = None
            Tags = []
        }

        let _ = Db.saveVideo conn videoEva |> Async.RunSynchronously
        let _ = Db.saveVideo conn videoRecital |> Async.RunSynchronously

        let _ = Db.addTagToVideo conn "v_eva" "t_camp_touring" |> Async.RunSynchronously
        let _ = Db.addTagToVideo conn "v_recital" "t_music" |> Async.RunSynchronously

        // 4. WebHostBuilderの構築
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

        // --- テストケース1: 親タグ「バイク」での検索 (GET /api/videos?q=tag:バイク) ---
        // 期待値: 孫タグ「キャンプツーリング」を持つ「2026年信州バイクツーリング.mp4」のみがヒットする
        let response1 = client.GetAsync("/api/videos?q=tag:バイク").Result
        response1.StatusCode |> should equal HttpStatusCode.OK
        let json1 = response1.Content.ReadAsStringAsync().Result
        let results1 = JsonSerializer.Deserialize<VideoDto list>(json1, jsonOptions)
        results1.Length |> should equal 1
        results1.[0].Id |> should equal "v_eva"

        // --- テストケース2: 中間子タグ「ツーリング」かつキーワードでの複合検索 (GET /api/videos?q=tag:ツーリング 信州) ---
        let response2 = client.GetAsync("/api/videos?q=tag:ツーリング 信州").Result
        response2.StatusCode |> should equal HttpStatusCode.OK
        let json2 = response2.Content.ReadAsStringAsync().Result
        let results2 = JsonSerializer.Deserialize<VideoDto list>(json2, jsonOptions)
        results2.Length |> should equal 1
        results2.[0].Id |> should equal "v_eva"

        // --- テストケース3: キーワード「Recital」での検索 (GET /api/videos?q=Recital) ---
        let response3 = client.GetAsync("/api/videos?q=Recital").Result
        response3.StatusCode |> should equal HttpStatusCode.OK
        let json3 = response3.Content.ReadAsStringAsync().Result
        let results3 = JsonSerializer.Deserialize<VideoDto list>(json3, jsonOptions)
        results3.Length |> should equal 1
        results3.[0].Id |> should equal "v_recital"

    [<Fact>]
    member _.``スマートフォルダのCRUDおよびAPI操作が正常に動作する`` () =
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

        // --- スマートフォルダ一覧取得 (初期状態: 0件) ---
        let getResponse1 = client.GetAsync("/api/smart-folders").Result
        getResponse1.StatusCode |> should equal HttpStatusCode.OK
        let jsonGet1 = getResponse1.Content.ReadAsStringAsync().Result
        let folders1 = JsonSerializer.Deserialize<SmartFolder list>(jsonGet1, jsonOptions)
        folders1.Length |> should equal 0

        // --- スマートフォルダ保存 (新規作成) ---
        let newFolder = {| name = "テストフォルダ"; query = "tag:ツーリング" |}
        let postContent = new StringContent(JsonSerializer.Serialize(newFolder), System.Text.Encoding.UTF8, "application/json")
        let postResponse = client.PostAsync("/api/smart-folders", postContent).Result
        postResponse.StatusCode |> should equal HttpStatusCode.OK
        let jsonPost = postResponse.Content.ReadAsStringAsync().Result
        let createdFolder = JsonSerializer.Deserialize<SmartFolder>(jsonPost, jsonOptions)
        createdFolder.Name |> should equal "テストフォルダ"
        createdFolder.Query |> should equal "tag:ツーリング"
        createdFolder.Id.StartsWith("sf_") |> should be True

        // --- スマートフォルダ一覧取得 (1件) ---
        let getResponse2 = client.GetAsync("/api/smart-folders").Result
        getResponse2.StatusCode |> should equal HttpStatusCode.OK
        let jsonGet2 = getResponse2.Content.ReadAsStringAsync().Result
        let folders2 = JsonSerializer.Deserialize<SmartFolder list>(jsonGet2, jsonOptions)
        folders2.Length |> should equal 1
        folders2.[0].Id |> should equal createdFolder.Id

        // --- スマートフォルダ削除 ---
        let deleteResponse = client.DeleteAsync($"/api/smart-folders/{createdFolder.Id}").Result
        deleteResponse.StatusCode |> should equal HttpStatusCode.OK

        // --- スマートフォルダ一覧取得 (削除後: 0件) ---
        let getResponse3 = client.GetAsync("/api/smart-folders").Result
        getResponse3.StatusCode |> should equal HttpStatusCode.OK
        let jsonGet3 = getResponse3.Content.ReadAsStringAsync().Result
        let folders3 = JsonSerializer.Deserialize<SmartFolder list>(jsonGet3, jsonOptions)
        folders3.Length |> should equal 0

    [<Fact>]
    member _.``タグ・ルール・スマートフォルダの一括インポートAPIがJSONおよびTSV形式で動作しIDが自動採番される`` () =
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

        // --- 1. タグの一括インポート (JSON 形式) ---
        // id 指定ありと、id 指定なし（空値）のタグを送信
        let jsonTagImportData = [
            {| id = "t_custom_1"; name = "インポートタグ1"; colorCode = "#111111"; parentId = null |}
            {| id = ""; name = "インポートタグ2"; colorCode = "#222222"; parentId = null |}
        ]
        let jsonTagContent = new StringContent(JsonSerializer.Serialize(jsonTagImportData), System.Text.Encoding.UTF8, "application/json")
        let tagJsonResponse = client.PostAsync("/api/tags/import", jsonTagContent).Result
        tagJsonResponse.StatusCode |> should equal HttpStatusCode.OK
        let jsonTagResult = tagJsonResponse.Content.ReadAsStringAsync().Result
        let importedTags = JsonSerializer.Deserialize<Tag list>(jsonTagResult, jsonOptions)
        importedTags.Length |> should equal 2
        
        // id が自動採番されていることを検証
        let tag1 = importedTags |> List.find (fun t -> t.Name = "インポートタグ1")
        let tag2 = importedTags |> List.find (fun t -> t.Name = "インポートタグ2")
        tag1.Id |> should equal "t_custom_1"
        tag2.Id.StartsWith("t_") |> should be True
        tag2.Id.Length |> should be (greaterThan 2)

        // --- 2. タグの一括インポート (TSV 形式) ---
        // id 指定なし（空値）の親子タグをインポート
        // TSV データ: id \t name \t colorCode \t parentId
        let tsvTagData = 
            "id\tname\tcolorCode\tparentId\n" +
            "\t子タグ\t#333333\tt_custom_1"
        let tsvTagContent = new StringContent(tsvTagData, System.Text.Encoding.UTF8, "text/plain")
        let tsvTagResponse = client.PostAsync("/api/tags/import", tsvTagContent).Result
        tsvTagResponse.StatusCode |> should equal HttpStatusCode.OK
        let tsvTagResult = tsvTagResponse.Content.ReadAsStringAsync().Result
        let importedTsvTags = JsonSerializer.Deserialize<Tag list>(tsvTagResult, jsonOptions)
        importedTsvTags.Length |> should equal 1
        let childTag = importedTsvTags.[0]
        childTag.Id.StartsWith("t_") |> should be True
        childTag.Name |> should equal "子タグ"
        childTag.ParentId |> should equal (Some "t_custom_1")

        // --- 3. 自動適用ルールの一括インポート (TSV 形式) ---
        // TSV データ: id \t pattern \t tagId \t matchType \t targetField \t minSize \t maxSize
        let tsvRuleData = 
            "id\tpattern\ttagId\tmatchType\ttargetField\tminSize\tmaxSize\n" +
            "\t【ツーリング】\tt_custom_1\tpartial\tfileName\t\t\n" +
            "r_custom_1\t.mp4\t" + childTag.Id + "\tregex\tfilePath\t1024\t2048"
        let tsvRuleContent = new StringContent(tsvRuleData, System.Text.Encoding.UTF8, "text/plain")
        let tsvRuleResponse = client.PostAsync("/api/rules/import", tsvRuleContent).Result
        tsvRuleResponse.StatusCode |> should equal HttpStatusCode.OK
        let tsvRuleResult = tsvRuleResponse.Content.ReadAsStringAsync().Result
        let importedRules = JsonSerializer.Deserialize<TaggingRule list>(tsvRuleResult, jsonOptions)
        importedRules.Length |> should equal 2

        let rule1 = importedRules |> List.find (fun r -> r.Pattern = "【ツーリング】")
        let rule2 = importedRules |> List.find (fun r -> r.Pattern = ".mp4")
        rule1.Id.StartsWith("r_") |> should be True
        rule2.Id |> should equal "r_custom_1"
        rule2.MatchType |> should equal "regex"
        rule2.TargetField |> should equal "filePath"
        rule2.MinSize |> should equal (Some 1024L)
        rule2.MaxSize |> should equal (Some 2048L)

        // --- 4. スマートフォルダの一括インポート (TSV 形式) ---
        // TSV データ: id \t name \t query
        let tsvFolderData = 
            "id\tname\tquery\n" +
            "\tお気に入りツーリング\ttag:ツーリング is:favorite\n" +
            "sf_custom_1\t音楽\ttag:音楽"
        let tsvFolderContent = new StringContent(tsvFolderData, System.Text.Encoding.UTF8, "text/plain")
        let tsvFolderResponse = client.PostAsync("/api/smart-folders/import", tsvFolderContent).Result
        tsvFolderResponse.StatusCode |> should equal HttpStatusCode.OK
        let tsvFolderResult = tsvFolderResponse.Content.ReadAsStringAsync().Result
        let importedFolders = JsonSerializer.Deserialize<SmartFolder list>(tsvFolderResult, jsonOptions)
        importedFolders.Length |> should equal 2

        let folder1 = importedFolders |> List.find (fun f -> f.Name = "お気に入りツーリング")
        let folder2 = importedFolders |> List.find (fun f -> f.Name = "音楽")
        folder1.Id.StartsWith("sf_") |> should be True
        folder1.Query |> should equal "tag:ツーリング is:favorite"
        folder2.Id |> should equal "sf_custom_1"
        folder2.Query |> should equal "tag:音楽"

    [<Fact>]
    member _.``動画再生履歴の記録とランキング・おすすめAPIが動作する`` () =
        let testDir = Directory.GetCurrentDirectory()
        let dummyPath1 = Path.Combine(testDir, "video_test_touring_pv.mp4")
        let dummyPath2 = Path.Combine(testDir, "video_test_touring_op.mp4")
        let dummyPath3 = Path.Combine(testDir, "video_test_other.mp4")
        let dummyPath4 = Path.Combine(testDir, "video_test_movie.mp4")

        File.WriteAllBytes(dummyPath1, [| 0uy; 0uy; 0uy; 0uy |])
        File.WriteAllBytes(dummyPath2, [| 0uy; 0uy; 0uy; 0uy |])
        File.WriteAllBytes(dummyPath3, [| 0uy; 0uy; 0uy; 0uy |])
        File.WriteAllBytes(dummyPath4, [| 0uy; 0uy; 0uy; 0uy |])

        try
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

            // データベースにテスト用動画を直接追加 (再生数検証用)
            let now = DateTime.UtcNow.ToString("o")
            let sql = "INSERT INTO Videos (Id, FileName, FilePath, Duration, FileSize, CreatedAt, AccessCount, LastAccessedAt) VALUES (@Id, @FileName, @FilePath, @Duration, @FileSize, @CreatedAt, 0, NULL)"
            use rawConn = Db.getConnection dbPath
            rawConn.Open()
            
            let v1 = {| Id = "v_test_1"; FileName = "video_test_anime_pv.mp4"; FilePath = dummyPath1; Duration = 60L; FileSize = 1024L; CreatedAt = now |}
            let v2 = {| Id = "v_test_2"; FileName = "video_test_anime_op.mp4"; FilePath = dummyPath2; Duration = 90L; FileSize = 2048L; CreatedAt = now |}
            let v3 = {| Id = "v_test_3"; FileName = "video_test_other.mp4"; FilePath = dummyPath3; Duration = 120L; FileSize = 4096L; CreatedAt = now |}
            let v4 = {| Id = "v_test_4"; FileName = "video_test_movie.mp4"; FilePath = dummyPath4; Duration = 180L; FileSize = 8192L; CreatedAt = now |}
            
            use tx = rawConn.BeginTransaction()
            Dapper.SqlMapper.Execute(rawConn, sql, v1, transaction = tx) |> ignore
            Dapper.SqlMapper.Execute(rawConn, sql, v2, transaction = tx) |> ignore
            Dapper.SqlMapper.Execute(rawConn, sql, v3, transaction = tx) |> ignore
            Dapper.SqlMapper.Execute(rawConn, sql, v4, transaction = tx) |> ignore
            tx.Commit()

            // --- 1. 動画再生 (アクセス記録APIの呼び出し) による再生履歴の記録 ---
            let res1_1 = client.PostAsync("/api/videos/v_test_1/access", null).Result
            let res1_2 = client.PostAsync("/api/videos/v_test_1/access", null).Result
            let res2_1 = client.PostAsync("/api/videos/v_test_2/access", null).Result

            // --- 2. 再生数ランキングAPIの検証 ---
            let rankingRes = client.GetAsync("/api/analysis/ranking").Result
            rankingRes.StatusCode |> should equal HttpStatusCode.OK
            let jsonRanking = rankingRes.Content.ReadAsStringAsync().Result
            let ranking = JsonSerializer.Deserialize<Video list>(jsonRanking, jsonOptions)
            
            ranking.Length |> should be (greaterThanOrEqualTo 2)
            let rank1 = ranking.[0]
            let rank2 = ranking.[1]
            rank1.Id |> should equal "v_test_1"
            rank1.AccessCount |> should equal 2
            rank2.Id |> should equal "v_test_2"
            rank2.AccessCount |> should equal 1

            // --- 3. おすすめ動画APIの検証 ---
            let recommendRes = client.GetAsync("/api/analysis/recommendations").Result
            recommendRes.StatusCode |> should equal HttpStatusCode.OK
            let jsonRecommend = recommendRes.Content.ReadAsStringAsync().Result
            
            let recommendData = JsonSerializer.Deserialize<{| keywords: string list; videos: Video list |}>(jsonRecommend, jsonOptions)
            
            recommendData.keywords |> should contain "anime"
            recommendData.videos.Length |> should be (greaterThanOrEqualTo 0)

            // --- 4. 再生回数クリアAPIの検証 ---
            let clearRes = client.PostAsync("/api/videos/v_test_1/access/clear", null).Result
            clearRes.StatusCode |> should equal HttpStatusCode.OK

            // 再度ランキングを取得し、v_test_1 の再生回数が0（よってランキングに含まれない）になっていることを検証
            let rankingRes2 = client.GetAsync("/api/analysis/ranking").Result
            rankingRes2.StatusCode |> should equal HttpStatusCode.OK
            let jsonRanking2 = rankingRes2.Content.ReadAsStringAsync().Result
            let ranking2 = JsonSerializer.Deserialize<Video list>(jsonRanking2, jsonOptions)
            let v1InRanking = ranking2 |> List.tryFind (fun v -> v.Id = "v_test_1")
            v1InRanking |> should equal None

            // --- 5. エラーログ取得APIの検証 ---
            // モックログを追加
            use dbConn = Db.getConnection dbPath

            Db.saveErrorLog dbConn "Test error message" (Some "Test stack trace") |> Async.RunSynchronously |> ignore
            
            let logsRes = client.GetAsync("/api/analysis/logs").Result
            logsRes.StatusCode |> should equal HttpStatusCode.OK
            let jsonLogs = logsRes.Content.ReadAsStringAsync().Result
            let logs = JsonSerializer.Deserialize<Domain.ErrorLog list>(jsonLogs, jsonOptions)
            
            logs.Length |> should be (greaterThan 0)
            let testLog = logs |> List.tryFind (fun l -> l.Message = "Test error message")
            match testLog with
            | Some l ->
                l.StackTrace |> should equal (Some "Test stack trace")
            | None ->
                failwith "Saved log was not found via API"

            // --- 6. メディア生成停止APIの検証 ---
            let stopRes = client.PostAsync("/api/media/stop", null).Result
            stopRes.StatusCode |> should equal HttpStatusCode.OK
            let jsonStop = stopRes.Content.ReadAsStringAsync().Result
            jsonStop |> should contain "stopped"
        finally
            try if File.Exists(dummyPath1) then File.Delete(dummyPath1) with | _ -> ()
            try if File.Exists(dummyPath2) then File.Delete(dummyPath2) with | _ -> ()
            try if File.Exists(dummyPath3) then File.Delete(dummyPath3) with | _ -> ()
            try if File.Exists(dummyPath4) then File.Delete(dummyPath4) with | _ -> ()

    [<Fact>]
    member _.``日本語ファイル名からハイブリッド方式で的確にキーワードが抽出されること`` () =
        let tags = [
            { Tag.Id = "t1"; Name = "すい"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
            { Tag.Id = "t2"; Name = "時間停止"; ColorCode = "#00ff00"; ParentId = None; VideoCount = 0 }
            { Tag.Id = "t3"; Name = "巨乳人妻"; ColorCode = "#0000ff"; ParentId = None; VideoCount = 0 }
        ]
        
        let title1 = "521MGFX-026 カリスマ巨乳人妻ソープ嬢 (純岡美乃理).mp4"
        let title2 = "DASS-660_月野江すい_時間停止ジム月野江すい感度爆増.mp4"
        let title3 = "義父に10秒だけの約束で挿入を許したら.mp4"

        let kws1 = TagBasedVideoManager.Infrastructure.Db.extractKeywordsFromFileName tags title1
        let kws2 = TagBasedVideoManager.Infrastructure.Db.extractKeywordsFromFileName tags title2
        let kws3 = TagBasedVideoManager.Infrastructure.Db.extractKeywordsFromFileName tags title3

        kws1 |> should contain "巨乳人妻"
        kws1 |> should contain "カリスマ"
        kws1 |> should contain "ソープ"
        kws1 |> should contain "純岡美乃理"
        kws1 |> should not' (contain "嬢")

        kws2 |> should contain "すい"
        kws2 |> should contain "時間停止"
        kws2 |> should contain "感度爆増"

        kws3 |> should contain "義父"
        kws3 |> should contain "約束"
        kws3 |> should contain "挿入"
        kws3 |> should not' (contain "に")
        kws3 |> should not' (contain "だけの")

    [<Fact>]
    member _.``getTagsを呼び出した際に各タグに紐付いている動画件数VideoCountが正しく取得されること`` () =
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let tag1 = { Tag.Id = "t_cnt_1"; Name = "CntTag1"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
        let tag2 = { Tag.Id = "t_cnt_2"; Name = "CntTag2"; ColorCode = "#00ff00"; ParentId = None; VideoCount = 0 }
        let _ = Db.saveTag conn tag1 |> Async.RunSynchronously
        let _ = Db.saveTag conn tag2 |> Async.RunSynchronously

        let now = DateTime.UtcNow.ToString("o")
        let sql = "INSERT INTO Videos (Id, FileName, FilePath, Duration, FileSize, CreatedAt, AccessCount) VALUES (@Id, @FileName, @FilePath, 0, 0, @CreatedAt, 0)"
        Dapper.SqlMapper.Execute(conn, sql, {| Id = "v_cnt_1"; FileName = "v1.mp4"; FilePath = "v1.mp4"; CreatedAt = now |}) |> ignore
        Dapper.SqlMapper.Execute(conn, sql, {| Id = "v_cnt_2"; FileName = "v2.mp4"; FilePath = "v2.mp4"; CreatedAt = now |}) |> ignore

        let _ = Db.addTagToVideo conn "v_cnt_1" "t_cnt_1" |> Async.RunSynchronously
        let _ = Db.addTagToVideo conn "v_cnt_1" "t_cnt_2" |> Async.RunSynchronously
        let _ = Db.addTagToVideo conn "v_cnt_2" "t_cnt_1" |> Async.RunSynchronously

        let getResult = Db.getTags conn |> Async.RunSynchronously
        match getResult with
        | Ok tags ->
            let t1 = tags |> List.find (fun t -> t.Id = "t_cnt_1")
            let t2 = tags |> List.find (fun t -> t.Id = "t_cnt_2")
            t1.VideoCount |> should equal 2
            t2.VideoCount |> should equal 1
        | Error _ -> failwith "Failed to get tags"
