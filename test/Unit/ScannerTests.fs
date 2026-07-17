namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Xunit
open FsUnit
open TagBasedVideoManager
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure
open TagBasedVideoManager.Application

type ScannerTests () =
    let videoDir = Path.Combine(AppContext.BaseDirectory, $"temp_videos_{Guid.NewGuid():N}")
    let thumbDir = Path.Combine(AppContext.BaseDirectory, $"temp_thumbs_{Guid.NewGuid():N}")
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_scanner_{Guid.NewGuid():N}.db")

    // テスト用のIMediaProcessorのスタブ (F#のオブジェクト式を利用)
    let mockMediaProcessor = {
        new IMediaProcessor with
            member _.GetMetadata(videoPath) =
                async { return Ok (120L, 1048576L) } // 常に120秒、1MBを返す
            member _.GenerateThumbnail(videoPath, outputPath) =
                async { return Ok outputPath } // 出力パスをそのまま返す
            member _.CancelAll() = ()
    }

    interface IDisposable with
        member _.Dispose() =
            // テスト用ディレクトリとファイルのクリーンアップ
            if Directory.Exists(videoDir) then
                try Directory.Delete(videoDir, true) with | _ -> ()
            if Directory.Exists(thumbDir) then
                try Directory.Delete(thumbDir, true) with | _ -> ()
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``syncVideoDirectoryを実行すると対応形式の動画のみがスキャンされDBに登録される`` () =
        // 1. ディレクトリとダミーファイルの作成
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        // 対応形式 (.mp4)
        File.WriteAllText(Path.Combine(videoDir, "video1.mp4"), "dummy")
        let subDir = Path.Combine(videoDir, "sub")
        Directory.CreateDirectory(subDir) |> ignore
        File.WriteAllText(Path.Combine(subDir, "video2.mp4"), "dummy")
        
        // 非対応形式 (.txt, .avi)
        File.WriteAllText(Path.Combine(videoDir, "doc.txt"), "dummy")
        File.WriteAllText(Path.Combine(videoDir, "video3.avi"), "dummy")

        // 2. データベース初期化
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 3. スキャン同期実行
        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        
        // 4. アサーション (最初はScannerがNotImplementedを返すため失敗する)
        result |> should equal (Ok 2 : Result<int, DbError>)

    [<Fact>]
    member _.``空のディレクトリに対してsyncVideoDirectoryを実行すると登録件数0で正常終了する`` () =
        // 境界値: ファイルが存在しない空ディレクトリ
        let emptyDir = Path.Combine(videoDir, "empty_dir")
        Directory.CreateDirectory(emptyDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let result = Scanner.syncVideoDirectory emptyDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

    [<Fact>]
    member _.``同じ動画ファイルを2回スキャンしても重複登録されず追加件数は0になる`` () =
        // 境界値/冪等性: 重複スキャン時の排除検証
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        File.WriteAllText(Path.Combine(videoDir, "duplicate_test.mp4"), "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 1回目のスキャン: 新規追加され、登録数 = 1 となる
        let result1 = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result1 |> should equal (Ok 1 : Result<int, DbError>)

        // 2回目のスキャン: 重複が検知され、登録数 = 0 となる
        let result2 = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result2 |> should equal (Ok 0 : Result<int, DbError>)

    [<Fact>]
    member _.``POST_api_scanを実行するとディレクトリの同期が行われ追加件数が返却される`` () =
        // 統合APIテスト: POST /api/scan
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        File.WriteAllText(Path.Combine(videoDir, "api_test.mp4"), "dummy")

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)
                    Environment.SetEnvironmentVariable("VIDEO_DIR", videoDir)
                    Environment.SetEnvironmentVariable("THUMBNAIL_DIR", thumbDir)
                    Program.configureServices services
                )
                .Configure(Program.configureApp)

        use server = new TestServer(builder)
        use client = server.CreateClient()

        // APIを呼び出し
        let response = client.PostAsync("/api/scan", new StringContent("", Encoding.UTF8, "application/json")).Result
        response.StatusCode |> should equal HttpStatusCode.OK
        
        let json = response.Content.ReadAsStringAsync().Result
        let res = JsonSerializer.Deserialize<{| addedCount: int |}>(json)
        res.addedCount |> should be (greaterThanOrEqualTo 0)

    [<Fact>]
    member _.``syncVideoDirectoryは即座にDB登録し非同期キューにより後からメタデータが更新される`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        File.WriteAllText(Path.Combine(videoDir, "async_test.mp4"), "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // スキャン実行
        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 1 : Result<int, DbError>)

        // バックグラウンドワーカーが処理するのを待つ (最大5秒のポーリング)
        let mutable durationUpdated = false
        let limit = DateTime.UtcNow.AddSeconds(5.0)
        while not durationUpdated && DateTime.UtcNow < limit do
            let checkResult = Db.getVideos conn None |> Async.RunSynchronously
            match checkResult with
            | Ok videos ->
                let vid = videos |> List.tryFind (fun v -> v.FileName = "async_test.mp4")
                if vid.IsSome && vid.Value.Duration = 120L then
                    durationUpdated <- true
                else
                    System.Threading.Thread.Sleep(50)
            | Error _ ->
                System.Threading.Thread.Sleep(50)

        durationUpdated |> should equal true

    [<Fact>]
    member _.``syncVideoDirectoryは既存動画でもメタデータDurationが0の場合は非同期キューに再投入してメタデータを更新する`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        let videoFile = Path.Combine(videoDir, "meta_missing.mp4")
        File.WriteAllText(videoFile, "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"
        let dummyThumbnail = Path.Combine(thumbDir, $"{videoId}.jpg")
        File.WriteAllText(dummyThumbnail, "dummy_thumb")

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "meta_missing.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 0L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = Some dummyThumbnail
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

        let mutable durationUpdated = false
        let limit = DateTime.UtcNow.AddSeconds(5.0)
        while not durationUpdated && DateTime.UtcNow < limit do
            let checkResult = Db.getVideos conn None |> Async.RunSynchronously
            match checkResult with
            | Ok videos ->
                let vid = videos |> List.tryFind (fun v -> v.FileName = "meta_missing.mp4")
                if vid.IsSome && vid.Value.Duration = 120L then
                    durationUpdated <- true
                else
                    System.Threading.Thread.Sleep(50)
            | Error _ ->
                System.Threading.Thread.Sleep(50)

        durationUpdated |> should equal true

    [<Fact>]
    member _.``syncVideoDirectoryは既存動画でDurationがプラスかつサムネイルパスがNoneの場合は再処理をスキップする`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        let videoFile = Path.Combine(videoDir, "reprocess_skipped.mp4")
        File.WriteAllText(videoFile, "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "reprocess_skipped.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 120L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = None
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        MediaQueue.clearQueue()

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

        System.Threading.Thread.Sleep(500)
        let checkResult = Db.getVideos conn None |> Async.RunSynchronously
        match checkResult with
        | Ok videos ->
            let vid = videos |> List.tryFind (fun v -> v.FileName = "reprocess_skipped.mp4")
            vid.IsSome |> should equal true
            vid.Value.Duration |> should equal 120L
            vid.Value.ThumbnailPath |> should equal None
        | Error _ -> failwith "Failed to get videos"

    [<Fact>]
    member _.``syncVideoDirectoryは既存動画でサムネイルパスはあるが画像ファイルが削除された場合は再処理する`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        let videoFile = Path.Combine(videoDir, "reprocess_needed.mp4")
        File.WriteAllText(videoFile, "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"
        
        let missingThumbnailPath = Path.Combine(thumbDir, $"{videoId}_missing.jpg")
        if File.Exists(missingThumbnailPath) then File.Delete(missingThumbnailPath)

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "reprocess_needed.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 120L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = Some missingThumbnailPath
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        MediaQueue.clearQueue()

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

        let mutable thumbRestored = false
        let limit = DateTime.UtcNow.AddSeconds(3.0)
        while not thumbRestored && DateTime.UtcNow < limit do
            let checkResult = Db.getVideos conn None |> Async.RunSynchronously
            match checkResult with
            | Ok videos ->
                let vid = videos |> List.tryFind (fun v -> v.FileName = "reprocess_needed.mp4")
                if vid.IsSome && vid.Value.ThumbnailPath.IsSome && vid.Value.ThumbnailPath.Value <> missingThumbnailPath then
                    thumbRestored <- true
                else
                    System.Threading.Thread.Sleep(50)
            | Error _ ->
                System.Threading.Thread.Sleep(50)

        thumbRestored |> should equal true

    [<Fact>]
    member _.``syncVideoDirectoryを実行すると動画が格納されている各フォルダが自動的にタグとして作成・付与されること`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore

        let subDir = Path.Combine(videoDir, "Anime", "2026")
        Directory.CreateDirectory(subDir) |> ignore
        let videoFile = Path.Combine(subDir, "folder_tag_test.mp4")
        File.WriteAllText(videoFile, "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        MediaQueue.clearQueue()

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 1 : Result<int, DbError>)

        let checkResult = Db.getVideos conn None |> Async.RunSynchronously
        match checkResult with
        | Ok videos ->
            let vid = videos |> List.tryFind (fun v -> v.FileName = "folder_tag_test.mp4")
            vid.IsSome |> should equal true
            
            let tagsResult = Db.getTags conn |> Async.RunSynchronously
            match tagsResult with
            | Ok tags ->
                let animeTag = tags |> List.tryFind (fun t -> t.Name = "Anime")
                let yearTag = tags |> List.tryFind (fun t -> t.Name = "2026")
                
                animeTag.IsSome |> should equal true
                yearTag.IsSome |> should equal true
                
                animeTag.Value.ParentId |> should equal None
                yearTag.Value.ParentId |> should equal None

                let videoWithTagsResult = Db.getVideos conn None |> Async.RunSynchronously
                match videoWithTagsResult with
                | Ok vList ->
                    let targetVideo : TagBasedVideoManager.Domain.Video = vList |> List.find (fun v -> v.Id = vid.Value.Id)
                    let tagNames = targetVideo.Tags |> List.map (fun t -> t.Name)
                    tagNames |> should contain "Anime"
                    tagNames |> should contain "2026"
                | Error _ -> failwith "Failed to get videos with tags"
            | Error _ -> failwith "Failed to get tags"
        | Error _ -> failwith "Failed to get videos"
