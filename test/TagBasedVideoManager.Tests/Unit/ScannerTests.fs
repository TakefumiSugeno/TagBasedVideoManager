namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Runtime.InteropServices
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

        use initConn = Db.getConnection dbPath
        DbInit.initializeDatabase initConn |> ignore

        let builder =
            WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureServices(fun services ->
                    Environment.SetEnvironmentVariable("DATABASE_PATH", dbPath)
                    Environment.SetEnvironmentVariable("VIDEO_DIRS", videoDir)
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
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
            TagBasedVideoManager.Domain.Video.FileUuid = None
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
    member _.``syncVideoDirectoryは既存動画でDurationがプラスかつサムネイルパスがNoneの場合は再処理キューへ投入する`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        let videoFile = Path.Combine(videoDir, "reprocess_needed_no_thumb.mp4")
        File.WriteAllText(videoFile, "dummy")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "reprocess_needed_no_thumb.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 120L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = None
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
            TagBasedVideoManager.Domain.Video.FileUuid = None
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        MediaQueue.clearQueue()

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

        let mutable thumbUpdated = false
        let limit = DateTime.UtcNow.AddSeconds(3.0)
        while not thumbUpdated && DateTime.UtcNow < limit do
            let checkResult = Db.getVideos conn None |> Async.RunSynchronously
            match checkResult with
            | Ok videos ->
                let vid = videos |> List.tryFind (fun v -> v.FileName = "reprocess_needed_no_thumb.mp4")
                if vid.IsSome && vid.Value.ThumbnailPath.IsSome then
                    thumbUpdated <- true
                else
                    System.Threading.Thread.Sleep(50)
            | Error _ ->
                System.Threading.Thread.Sleep(50)

        thumbUpdated |> should equal true

    [<Fact>]
    member _.``syncVideoDirectoryは既存動画でDurationがプラスかつサムネイル実ファイルが存在する場合は再処理をスキップする`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore
        
        let videoFile = Path.Combine(videoDir, "already_completed.mp4")
        File.WriteAllText(videoFile, "dummy")

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"
        // デフォルトの出力命名 ({videoId}.jpg) とは異なるカスタムパスを指定し、
        // 再処理によって上書きされていないことを厳密に検証可能にする（甘いアサーションの排除）
        let dummyThumbnail = Path.Combine(thumbDir, $"{videoId}_existing_custom.jpg")
        File.WriteAllText(dummyThumbnail, "dummy_thumb_content")

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "already_completed.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 120L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = Some dummyThumbnail
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
            TagBasedVideoManager.Domain.Video.FileUuid = None
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        MediaQueue.clearQueue()

        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

        System.Threading.Thread.Sleep(500)
        let checkResult = Db.getVideos conn None |> Async.RunSynchronously
        match checkResult with
        | Ok videos ->
            let vid = videos |> List.tryFind (fun v -> v.FileName = "already_completed.mp4")
            vid.IsSome |> should equal true
            vid.Value.ThumbnailPath |> should equal (Some dummyThumbnail)
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
            TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
            TagBasedVideoManager.Domain.Video.FileUuid = None
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

    [<Fact>]
    member _.``syncVideoDirectoryは既存動画の再スキャン時にタイムゾーン乖離による不要なバックフィル更新が発生しないこと`` () =
        Directory.CreateDirectory(videoDir) |> ignore
        Directory.CreateDirectory(thumbDir) |> ignore

        let videoFile = Path.Combine(videoDir, "timezone_test.mp4")
        File.WriteAllText(videoFile, "dummy")
        let fi = FileInfo(videoFile)
        let originalUtc = fi.LastWriteTimeUtc

        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        let guidStr = Guid.NewGuid().ToString("N")
        let videoId = $"v_{guidStr.Substring(0, 8)}"
        let dummyThumbnail = Path.Combine(thumbDir, $"{videoId}.jpg")
        File.WriteAllText(dummyThumbnail, "dummy_thumb_content")

        let existingVideo = {
            TagBasedVideoManager.Domain.Video.Id = videoId
            TagBasedVideoManager.Domain.Video.FileName = "timezone_test.mp4"
            TagBasedVideoManager.Domain.Video.FilePath = videoFile
            TagBasedVideoManager.Domain.Video.Duration = 100L
            TagBasedVideoManager.Domain.Video.FileSize = 100L
            TagBasedVideoManager.Domain.Video.ThumbnailPath = Some dummyThumbnail
            TagBasedVideoManager.Domain.Video.IsFavorite = false
            TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
            TagBasedVideoManager.Domain.Video.FileModifiedAt = originalUtc
            TagBasedVideoManager.Domain.Video.AccessCount = 0
            TagBasedVideoManager.Domain.Video.LastAccessedAt = None
            TagBasedVideoManager.Domain.Video.Tags = []
            TagBasedVideoManager.Domain.Video.FileUuid = None
        }
        let _ = Db.saveVideo conn existingVideo |> Async.RunSynchronously

        let checkBefore = Db.getVideo conn videoId |> Async.RunSynchronously
        match checkBefore with
        | Ok loadedVideo ->
            let diff = Math.Abs((loadedVideo.FileModifiedAt.ToUniversalTime() - originalUtc.ToUniversalTime()).TotalSeconds)
            diff |> should be (lessThanOrEqualTo 1.0)
            loadedVideo.FileModifiedAt.Kind |> should equal DateTimeKind.Utc
        | Error err -> failwith $"Failed to get video: {err}"

        MediaQueue.clearQueue()
        let result = Scanner.syncVideoDirectory videoDir thumbDir mockMediaProcessor conn |> Async.RunSynchronously
        result |> should equal (Ok 0 : Result<int, DbError>)

    // =========================================================================
    // multi-video-dirs (Task 2.1): 単体テスト・デシジョンテーブル (UT-11, 12b, 12c, DT-01, DT-02)
    // =========================================================================

    [<Fact>]
    member _.``UT-11: ルートディレクトリとサブディレクトリの相対パスからルート名とサブフォルダ名タグが導出されること`` () =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        let root = if isWindows then @"D:\Anime" else "/data/Anime"
        let file = if isWindows then @"D:\Anime\S1\a.mp4" else "/data/Anime/S1/a.mp4"
        let tags = Scanner.deriveFolderTags root file
        tags |> should equal ["Anime"; "S1"]

    [<Fact>]
    member _.``UT-12b: ルート直下のファイルの場合はルート名タグのみが導出されること`` () =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        let root = if isWindows then @"D:\Anime" else "/data/Anime"
        let file = if isWindows then @"D:\Anime\a.mp4" else "/data/Anime/a.mp4"
        let tags = Scanner.deriveFolderTags root file
        tags |> should equal ["Anime"]

    [<Fact>]
    member _.``UT-12c: 末尾に区切り文字が付いたルートディレクトリでもルート名タグが正しく導出されること`` () =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        let root = if isWindows then @"D:\Anime\" else "/data/Anime/"
        let file = if isWindows then @"D:\Anime\a.mp4" else "/data/Anime/a.mp4"
        let tags = Scanner.deriveFolderTags root file
        tags |> should equal ["Anime"]

    [<Fact>]
    member _.``DT-01: 相対パスがルート配下の場合はルート名およびサブフォルダ名タグが付与されること`` () =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        let root = if isWindows then @"D:\Anime" else "/data/Anime"
        let file = if isWindows then @"D:\Anime\Season1\Part2\ep1.mp4" else "/data/Anime/Season1/Part2/ep1.mp4"
        let tags = Scanner.deriveFolderTags root file
        tags |> should equal ["Anime"; "Season1"; "Part2"]

    [<Fact>]
    member _.``DT-02: ファイルがルート配下にない（相対パスがドットドットで始まる）場合はタグが付与されないこと`` () =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        let root = if isWindows then @"D:\Anime" else "/data/Anime"
        let file = if isWindows then @"D:\Other\movie.mp4" else "/data/Other/movie.mp4"
        let tags = Scanner.deriveFolderTags root file
        tags |> should equal ([] : string list)

    // =========================================================================
    // multi-video-dirs (Task 2.1): 結合テスト (IT-01〜IT-06, IT-05b, IT-06b, IT-09, IT-10, ST-01/02/04)
    // =========================================================================

    [<Fact>]
    member _.``IT-01: 親フォルダ配下のシンボリックリンクまたはジャンクション先の動画が検出されDBに登録されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it01_{Guid.NewGuid():N}")
        let rootDir = Path.Combine(testBase, "Videos")
        let targetDir = Path.Combine(testBase, "Target")
        let linkDir = Path.Combine(rootDir, "LinkedDir")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(rootDir) |> ignore
            Directory.CreateDirectory(targetDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            let realVideo = Path.Combine(targetDir, "link_video.mp4")
            File.WriteAllText(realVideo, "dummy")
            Directory.CreateSymbolicLink(linkDir, targetDir) |> ignore

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [rootDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
            let resolvedTargetVideo = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath realVideo
            videos.Head.FilePath |> should equal resolvedTargetVideo
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-02: 複数の物理フォルダを指定したときに全フォルダ配下の動画が検出されDBに登録されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it02_{Guid.NewGuid():N}")
        let dir1 = Path.Combine(testBase, "Dir1")
        let dir2 = Path.Combine(testBase, "Dir2")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir1) |> ignore
            Directory.CreateDirectory(dir2) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir1, "v1.mp4"), "dummy")
            File.WriteAllText(Path.Combine(dir2, "v2.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [dir1; dir2] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 2 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 2
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-03: 指定されたディレクトリのうち一部が存在しない場合でもスキップされて実在するディレクトリの走査が継続されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it03_{Guid.NewGuid():N}")
        let dir1 = Path.Combine(testBase, "ExistingDir")
        let missingDir = Path.Combine(testBase, "NonExistingDir")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir1) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir1, "v1.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [dir1; missingDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
            videos.Head.FileName |> should equal "v1.mp4"
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-04: 指定された全てのディレクトリが存在しない場合でも例外終了せず走査結果0件として安全に終了すること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it04_{Guid.NewGuid():N}")
        let missing1 = Path.Combine(testBase, "NonExisting1")
        let missing2 = Path.Combine(testBase, "NonExisting2")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(testBase) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [missing1; missing2] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 0 : Result<int, DbError>)
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-05: 親子関係のルートが指定されても同一の物理ファイルは重複登録されず1件のみ登録されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it05_{Guid.NewGuid():N}")
        let parentDir = Path.Combine(testBase, "Parent")
        let subDir = Path.Combine(parentDir, "Sub")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(subDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(subDir, "nested.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [parentDir; subDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-05b: 入れ子関係のルートが指定された場合、所属するすべてのルート名タグが和集合で付与されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it05b_{Guid.NewGuid():N}")
        let parentDir = Path.Combine(testBase, "AnimeParent")
        let subDir = Path.Combine(parentDir, "SpecialSub")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(subDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(subDir, "ova.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [parentDir; subDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
            let tagNames = videos.Head.Tags |> List.map (fun t -> t.Name)
            tagNames |> should contain "AnimeParent"
            tagNames |> should contain "SpecialSub"
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-06: 大文字小文字の表記揺れがある同一パスで再スキャンしても重複登録されず追加件数は0件となること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it06_{Guid.NewGuid():N}")
        let dir1 = Path.Combine(testBase, "Videos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir1) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir1, "sample.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            // 1回目のスキャン
            let res1 = Scanner.syncVideoDirectories [dir1] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res1 |> should equal (Ok 1 : Result<int, DbError>)

            // 2回目のスキャン: 大文字小文字表記を変えてスキャン
            let dirAltCase =
                if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                    dir1.ToUpperInvariant()
                else
                    dir1
            let res2 = Scanner.syncVideoDirectories [dirAltCase] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res2 |> should equal (Ok 0 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-06b: 旧仕様等でルート名タグが付与されていない既存動画に対して再スキャン時に不足タグが安全に追記されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it06b_{Guid.NewGuid():N}")
        let rootDir = Path.Combine(testBase, "AnimeArchive")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(rootDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            let videoFile = Path.Combine(rootDir, "legacy.mp4")
            File.WriteAllText(videoFile, "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            // 既存動画レコードをタグなしで登録
            let existingVideo = {
                TagBasedVideoManager.Domain.Video.Id = "v_legacy01"
                TagBasedVideoManager.Domain.Video.FileName = "legacy.mp4"
                TagBasedVideoManager.Domain.Video.FilePath = Path.GetFullPath(videoFile)
                TagBasedVideoManager.Domain.Video.Duration = 100L
                TagBasedVideoManager.Domain.Video.FileSize = 100L
                TagBasedVideoManager.Domain.Video.ThumbnailPath = None
                TagBasedVideoManager.Domain.Video.IsFavorite = false
                TagBasedVideoManager.Domain.Video.CreatedAt = DateTime.UtcNow
                TagBasedVideoManager.Domain.Video.FileModifiedAt = DateTime.UtcNow
                TagBasedVideoManager.Domain.Video.AccessCount = 0
                TagBasedVideoManager.Domain.Video.LastAccessedAt = None
                TagBasedVideoManager.Domain.Video.Tags = []
                TagBasedVideoManager.Domain.Video.FileUuid = None
            }
            Db.saveVideo conn existingVideo |> Async.RunSynchronously |> ignore

            // 再スキャンを実行
            let res = Scanner.syncVideoDirectories [rootDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res |> should equal (Ok 0 : Result<int, DbError>)

            // 動画を取得し、不足していたルート名タグ "AnimeArchive" が追記されていることを検証
            let videoResult = Db.getVideo conn "v_legacy01" |> Async.RunSynchronously
            match videoResult with
            | Ok v ->
                let tagNames = v.Tags |> List.map (fun t -> t.Name)
                tagNames |> should contain "AnimeArchive"
            | Error err -> failwith $"Failed to get video: {err}"
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-09: ルートフォルダ名および相対サブフォルダ名のタグがDBに作成され動画に紐付いて永続化されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it09_{Guid.NewGuid():N}")
        let rootDir = Path.Combine(testBase, "AnimeSeries")
        let subDir = Path.Combine(rootDir, "Season1")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(subDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(subDir, "ep1.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [rootDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let tags = Db.getTags conn |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            let tagNames = tags |> List.map (fun t -> t.Name)
            tagNames |> should contain "AnimeSeries"
            tagNames |> should contain "Season1"

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
            let vTagNames = videos.Head.Tags |> List.map (fun t -> t.Name)
            vTagNames |> should contain "AnimeSeries"
            vTagNames |> should contain "Season1"
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``IT-10: 異なる親を持つ同名ルート（大文字小文字違い）の動画走査時にタグが1つに統合され重複作成されないこと`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_it10_{Guid.NewGuid():N}")
        let dirA = Path.Combine(testBase, "StoreA", "Clips")
        let dirB = Path.Combine(testBase, "StoreB", "clips")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dirA) |> ignore
            Directory.CreateDirectory(dirB) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dirA, "c1.mp4"), "dummy")
            File.WriteAllText(Path.Combine(dirB, "c2.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let result = Scanner.syncVideoDirectories [dirA; dirB] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 2 : Result<int, DbError>)

            let tags = Db.getTags conn |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            let clipTags = tags |> List.filter (fun t -> String.Equals(t.Name, "Clips", StringComparison.OrdinalIgnoreCase))
            clipTags |> List.length |> should equal 1
            let sharedTagId = clipTags.Head.Id

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 2
            for v in videos do
                v.Tags |> List.exists (fun t -> t.Id = sharedTagId) |> should equal true
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``ST-01: 未スキャン状態から正常設定でのスキャン実行により登録済み状態へ遷移すること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_st01_{Guid.NewGuid():N}")
        let dir = Path.Combine(testBase, "NewVideos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir, "v_init.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let before = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            before |> List.length |> should equal 0

            let result = Scanner.syncVideoDirectories [dir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 1 : Result<int, DbError>)

            let after = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            after |> List.length |> should equal 1
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``ST-02: 登録済み状態から再スキャンを実行しても動画件数は不変（冪等性）で登録済み状態が維持されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_st02_{Guid.NewGuid():N}")
        let dir = Path.Combine(testBase, "IdempotentVideos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir, "idem.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let res1 = Scanner.syncVideoDirectories [dir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res1 |> should equal (Ok 1 : Result<int, DbError>)

            let res2 = Scanner.syncVideoDirectories [dir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res2 |> should equal (Ok 0 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 1
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``ST-04: 登録済みの2ルートのうち1ルートが不在になっても不在ルートの動画レコードはDBから削除されず保護されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_st04_{Guid.NewGuid():N}")
        let dir1 = Path.Combine(testBase, "StableDir")
        let dir2 = Path.Combine(testBase, "RemovableDir")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(dir1) |> ignore
            Directory.CreateDirectory(dir2) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore
            File.WriteAllText(Path.Combine(dir1, "v_stable.mp4"), "dummy")
            File.WriteAllText(Path.Combine(dir2, "v_removable.mp4"), "dummy")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let res1 = Scanner.syncVideoDirectories [dir1; dir2] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res1 |> should equal (Ok 2 : Result<int, DbError>)

            Directory.Delete(dir2, true)

            let res2 = Scanner.syncVideoDirectories [dir1; dir2] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            res2 |> should equal (Ok 0 : Result<int, DbError>)

            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos |> List.length |> should equal 2
            videos |> List.exists (fun v -> v.FileName = "v_stable.mp4") |> should equal true
            videos |> List.exists (fun v -> v.FileName = "v_removable.mp4") |> should equal true
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``UT-SCN-02: ファイル名が UTF-8 で 255 バイトを超える動画をスキャンした場合 DB 登録を維持しつつ ErrorLogs に警告ログを記録し MediaQueue 投入をスキップする`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_utscn02_{Guid.NewGuid():N}")
        let scanDir = Path.Combine(testBase, "videos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(scanDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore

            // 86文字のひらがな（86 * 3 = 258バイト）+ ".mp4" = 262バイト (> 255B)
            let longFileName = String('あ', 86) + ".mp4"
            let longFilePath = Path.Combine(scanDir, longFileName)
            File.WriteAllText(longFilePath, "dummy content")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let processedPaths = System.Collections.Concurrent.ConcurrentBag<string>()
            let spyMediaProcessor = {
                new IMediaProcessor with
                    member _.GetMetadata(p) =
                        processedPaths.Add(p)
                        async { return Ok (120L, 1024L) }
                    member _.GenerateThumbnail(p, o) =
                        async { return Ok o }
                    member _.CancelAll() = ()
            }

            let res = Scanner.syncVideoDirectories [scanDir] testThumb spyMediaProcessor conn |> Async.RunSynchronously
            res |> should equal (Ok 1 : Result<int, DbError>)

            // 1. DB に動画レコードが登録されていること（ファイル名・パス維持）
            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos.Length |> should equal 1
            videos.[0].FileName |> should equal longFileName

            // 2. ErrorLogs に 255バイト制限 (NAME_MAX) 超過警告が記録されていること
            let logsRes = Db.getErrorLogs conn |> Async.RunSynchronously
            match logsRes with
            | Ok logs ->
                logs.Length |> should be (greaterThanOrEqualTo 1)
                let warnLog = logs |> List.find (fun l -> l.Message.Contains("255") || l.Message.Contains("NAME_MAX"))
                warnLog.Message.Contains("Linux") |> should equal true
                warnLog.Message.Contains(longFileName) |> should equal true
            | Error e -> failwithf "Failed to get error logs: %A" e

            // 3. MediaQueue に投入されていないこと（キュー処理が実行されない）
            System.Threading.Thread.Sleep(200) // キューワーカーの猶予
            processedPaths |> Seq.contains longFilePath |> should equal false
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``UT-SCN-01: ファイル名が UTF-8 で 255 バイト以内の正常な動画をスキャンした場合 DB 登録され MediaQueue に投入され ErrorLogs に警告は記録されない`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_utscn01_{Guid.NewGuid():N}")
        let scanDir = Path.Combine(testBase, "videos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(scanDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore

            let normalFileName = "normal_video.mp4"
            let normalFilePath = Path.Combine(scanDir, normalFileName)
            File.WriteAllText(normalFilePath, "dummy content")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            let processedPaths = System.Collections.Concurrent.ConcurrentBag<string>()
            let spyMediaProcessor = {
                new IMediaProcessor with
                    member _.GetMetadata(p) =
                        processedPaths.Add(p)
                        async { return Ok (120L, 1024L) }
                    member _.GenerateThumbnail(p, o) =
                        async { return Ok o }
                    member _.CancelAll() = ()
            }

            let res = Scanner.syncVideoDirectories [scanDir] testThumb spyMediaProcessor conn |> Async.RunSynchronously
            res |> should equal (Ok 1 : Result<int, DbError>)

            // 1. DB に登録されていること
            let videos = Db.getVideos conn None |> Async.RunSynchronously |> Result.defaultWith (fun _ -> [])
            videos.Length |> should equal 1
            videos.[0].FileName |> should equal normalFileName

            // 2. ErrorLogs に警告が記録されていないこと
            let logsRes = Db.getErrorLogs conn |> Async.RunSynchronously
            match logsRes with
            | Ok logs ->
                let warnLogs = logs |> List.filter (fun l -> l.Message.Contains("255") || l.Message.Contains("NAME_MAX"))
                warnLogs |> should be Empty
            | Error e -> failwithf "Failed to get error logs: %A" e

            // 3. MediaQueue に投入されて処理されること
            // キューワーカーが非同期で処理するのを最大2秒待機
            let mutable waitCount = 0
            while processedPaths.IsEmpty && waitCount < 20 do
                System.Threading.Thread.Sleep(100)
                waitCount <- waitCount + 1
            processedPaths.IsEmpty |> should equal false
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``SE-201: 走査中にサブディレクトリ走査で例外が発生した場合に配下の動画レコードが保護され誤削除されないこと`` () =
        if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then ()
        else
            let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_se201_{Guid.NewGuid():N}")
            let scanDir = Path.Combine(testBase, "Videos")
            let lockedDir = Path.Combine(scanDir, "LockedDir")
            let testDb = Path.Combine(testBase, "test.db")
            let testThumb = Path.Combine(testBase, "thumbs")
            try
                Directory.CreateDirectory(scanDir) |> ignore
                Directory.CreateDirectory(lockedDir) |> ignore
                Directory.CreateDirectory(testThumb) |> ignore

                let protectedFile = Path.Combine(lockedDir, "protected.mp4")
                File.WriteAllText(protectedFile, "protected content")

                use conn = Db.getConnection testDb
                DbInit.initializeDatabase conn |> ignore

                // DB に登録
                let v = {
                    Id = "v_se201"
                    FileName = "protected.mp4"
                    FilePath = Path.GetFullPath protectedFile
                    Duration = 60L
                    FileSize = (FileInfo protectedFile).Length
                    ThumbnailPath = None
                    IsFavorite = false
                    CreatedAt = DateTime.UtcNow
                    FileModifiedAt = (FileInfo protectedFile).LastWriteTimeUtc
                    AccessCount = 0
                    LastAccessedAt = None
                    Tags = []
                    FileUuid = None
                }
                Db.saveVideo conn v |> Async.RunSynchronously |> ignore

                // icacls で lockedDir のアクセスを拒否 (*S-1-1-0 = Everyone SID)
                let psi = System.Diagnostics.ProcessStartInfo("icacls", $"\"{lockedDir}\" /deny *S-1-1-0:(OI)(CI)(F)")
                psi.RedirectStandardOutput <- true
                psi.UseShellExecute <- false
                let p = System.Diagnostics.Process.Start(psi)
                p.WaitForExit()

                try
                    // スキャン実行（lockedDir はアクセス拒否により走査失敗する）
                    let result = Scanner.syncVideoDirectories [scanDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
                    result |> should equal (Ok 0 : Result<int, DbError>)

                    // lockedDir 配下の protected.mp4 は誤削除防止ガードにより DB に保持されていること
                    let videoRes = Db.getVideo conn "v_se201" |> Async.RunSynchronously
                    match videoRes with
                    | Ok found -> found.Id |> should equal "v_se201"
                    | Error e -> failwithf "Expected video to be preserved, but got: %A" e

                    // エラーログが記録されていること
                    let logsRes = Db.getErrorLogs conn |> Async.RunSynchronously
                    match logsRes with
                    | Ok logs ->
                        logs |> List.exists (fun l -> l.Message.Contains("誤削除防止ガード")) |> should equal true
                    | Error e -> failwithf "Failed to get error logs: %A" e
                finally
                    // 権限をリセットしてクリーンアップ可能にする
                    let resetPsi = System.Diagnostics.ProcessStartInfo("icacls", $"\"{lockedDir}\" /grant *S-1-1-0:(OI)(CI)(F)")
                    resetPsi.RedirectStandardOutput <- true
                    resetPsi.UseShellExecute <- false
                    let rp = System.Diagnostics.Process.Start(resetPsi)
                    rp.WaitForExit()
            finally
                if Directory.Exists(testBase) then
                    try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``SE-202: 不在動画削除時にDB削除が成功した場合のみサムネイル物理ファイルが削除されること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_se202_{Guid.NewGuid():N}")
        let scanDir = Path.Combine(testBase, "Videos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(scanDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore

            let absentFile = Path.Combine(scanDir, "absent.mp4")
            let thumbPath = Path.Combine(testThumb, "v_se202.jpg")
            File.WriteAllText(thumbPath, "dummy thumbnail content")

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            // DB に実体のない動画とサムネイルパスを登録
            let v = {
                Id = "v_se202"
                FileName = "absent.mp4"
                FilePath = Path.GetFullPath absentFile
                Duration = 60L
                FileSize = 1000L
                ThumbnailPath = Some thumbPath
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            Db.saveVideo conn v |> Async.RunSynchronously |> ignore

            // スキャン実行（absent.mp4 は存在しないため Phase 3 で削除される）
            let result = Scanner.syncVideoDirectories [scanDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 0 : Result<int, DbError>)

            // DB からレコードが削除されていること
            let videoRes = Db.getVideo conn "v_se202" |> Async.RunSynchronously
            match videoRes with
            | Error (RecordNotFound _) -> ()
            | Ok _ -> failwith "Video record should be deleted"
            | Error e -> failwithf "Unexpected error: %A" e

            // サムネイルファイルも削除されていること
            File.Exists(thumbPath) |> should equal false
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()

    [<Fact>]
    member _.``PG-01: 未マッチファイルのフォールバック候補探索で同名・同日時の複数候補が存在する場合にId順で決定論的にマッチすること`` () =
        let testBase = Path.Combine(Path.GetTempPath(), $"tbvm_pg01_{Guid.NewGuid():N}")
        let scanDir = Path.Combine(testBase, "Videos")
        let testDb = Path.Combine(testBase, "test.db")
        let testThumb = Path.Combine(testBase, "thumbs")
        try
            Directory.CreateDirectory(scanDir) |> ignore
            Directory.CreateDirectory(testThumb) |> ignore

            let movedFile = Path.Combine(scanDir, "candidate_new.mp4")
            File.WriteAllText(movedFile, "shared content")
            let fi = FileInfo(movedFile)

            use conn = Db.getConnection testDb
            DbInit.initializeDatabase conn |> ignore

            // 同一サイズ・同名・同日時の 2 つの旧レコード（Id: v_cand_b, v_cand_a）
            let vB = {
                Id = "v_cand_b"
                FileName = "candidate_new.mp4"
                FilePath = Path.Combine(testBase, "old_b", "candidate_new.mp4")
                Duration = 50L
                FileSize = fi.Length
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = fi.LastWriteTimeUtc
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            let vA = {
                Id = "v_cand_a"
                FileName = "candidate_new.mp4"
                FilePath = Path.Combine(testBase, "old_a", "candidate_new.mp4")
                Duration = 50L
                FileSize = fi.Length
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = fi.LastWriteTimeUtc
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            Db.saveVideo conn vB |> Async.RunSynchronously |> ignore
            Db.saveVideo conn vA |> Async.RunSynchronously |> ignore

            // スキャン実行
            let result = Scanner.syncVideoDirectories [scanDir] testThumb mockMediaProcessor conn |> Async.RunSynchronously
            result |> should equal (Ok 0 : Result<int, DbError>) // 新規追加ではなく引き継ぎ

            // タイブレークキー (nameMatch, timeDiff, v.Id) により辞書順で若い v_cand_a が優先して引き継がれていること
            let videoA = Db.getVideo conn "v_cand_a" |> Async.RunSynchronously
            match videoA with
            | Ok v ->
                v.FilePath |> should equal (Path.GetFullPath movedFile)
            | Error e -> failwithf "Expected v_cand_a to be matched, but got: %A" e

            // v_cand_b はマッチせず削除（古いパスが config 外または非存在のため）
            let videoB = Db.getVideo conn "v_cand_b" |> Async.RunSynchronously
            match videoB with
            | Error (RecordNotFound _) -> ()
            | Ok _ -> failwith "v_cand_b should not be matched and should be cleaned up"
            | Error e -> failwithf "Unexpected error: %A" e
        finally
            if Directory.Exists(testBase) then
                try Directory.Delete(testBase, true) with _ -> ()
