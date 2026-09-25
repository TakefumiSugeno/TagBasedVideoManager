namespace TagBasedVideoManager.Application

open System
open System.IO
open System.Data
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure

module MediaQueue =
    type QueueItem = {
        VideoId: string
        FilePath: string
        ThumbDir: string
        MediaProc: IMediaProcessor
        ConnectionString: string
    }

    let private queue = ConcurrentQueue<QueueItem>()
    let private workerSemaphore = new SemaphoreSlim(1, 1)

    let rec private processQueue () =
        async {
            let mutable item = { VideoId = ""; FilePath = ""; ThumbDir = ""; MediaProc = null; ConnectionString = "" }
            if queue.TryDequeue(&item) then
                try
                    let! metaResult = item.MediaProc.GetMetadata item.FilePath
                    let duration = match metaResult with Ok (d, _) -> d | _ -> 0L
                    
                    let thumbPath = Path.Combine(item.ThumbDir, $"{item.VideoId}.jpg")
                    let! thumbResult = item.MediaProc.GenerateThumbnail(item.FilePath, thumbPath)
                    let finalThumb = match thumbResult with Ok path -> Some path | _ -> None

                    use newConn = new SqliteConnection(item.ConnectionString)
                    let! _ = Db.updateVideoMetadata newConn item.VideoId duration finalThumb
                    ()
                with
                | ex ->
                    try
                        use logConn = new SqliteConnection(item.ConnectionString)
                        do! Db.saveErrorLog logConn ex.Message (Some (ex.ToString()))
                    with _ -> ()
                return! processQueue ()

            else
                workerSemaphore.Release() |> ignore
        }

    let enqueue (item: QueueItem) =
        queue.Enqueue(item)
        if workerSemaphore.Wait(0) then
            Async.Start (processQueue ())

    let clearQueue () =
        let mutable item = { VideoId = ""; FilePath = ""; ThumbDir = ""; MediaProc = null; ConnectionString = "" }
        while queue.TryDequeue(&item) do ()

    let stopProcessing (mediaProc: IMediaProcessor) =
        clearQueue ()
        mediaProc.CancelAll()


module Scanner =
    
    /// スキャンがサポートされている動画拡張子の一覧
    let private supportedExtensions = [| ".mp4" |]

    /// ディレクトリ配下のすべてのファイルを再帰的に走査する（シンボリックリンク・ReparsePointのディレクトリも解決して追跡する）。
    let rec private enumerateFiles (dir: string) : string list =
        try
            let resolvedDir = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath dir
            if not (Directory.Exists(resolvedDir)) then []
            else
                let files = Directory.GetFiles(resolvedDir) |> Array.toList
                let subDirs = Directory.GetDirectories(resolvedDir)
                let subFiles = 
                    subDirs 
                    |> Array.toList 
                    |> List.collect enumerateFiles
                List.append files subFiles
        with
        | _ -> []

    /// スキャン処理のメインワークフロー
    let syncVideoDirectory (videoDir: string) (thumbDir: string) (mediaProc: IMediaProcessor) (conn: IDbConnection) : Async<Result<int, DbError>> =
        async {
            try
                // 1. ディレクトリ内の全ファイルを再帰走査
                if not (Directory.Exists(videoDir)) then
                    return Ok 0
                else
                    let allFiles = enumerateFiles videoDir |> List.toArray
                    
                    // 2. 対応拡張子（.mp4）のみにフィルタリング
                    let videoFiles = 
                        allFiles 
                        |> Array.filter (fun file -> 
                            let ext = Path.GetExtension(file).ToLower()
                            Array.contains ext supportedExtensions
                        )

                    // 2.5 既存の動画一覧を取得して、FilePath -> Id のマップを作成
                    let! existingVideosResult = Db.getVideos conn None
                    let existingVideoMap =
                        match existingVideosResult with
                        | Ok list -> list |> List.map (fun v -> v.FilePath, v.Id) |> Map.ofList
                        | Error _ -> Map.empty

                    // 3. 自動タグ付けルール一覧を取得
                    let! rulesResult = Db.getTaggingRules conn
                    let rules = 
                        match rulesResult with
                        | Ok r -> r
                        | Error _ -> []

                    // 3.5 タグ一覧をキャッシュ化してフォルダタグ自動作成に備える
                    let! tagsResult = Db.getTags conn
                    let mutable tagCache =
                        match tagsResult with
                        | Ok list -> list |> List.map (fun t -> t.Name.ToLower(), t.Id) |> Map.ofList
                        | Error _ -> Map.empty

                    let mutable addedCount = 0

                    for filePath in videoFiles do
                        let fileName = Path.GetFileName filePath
                        let fi = FileInfo(filePath)
                        let fileSize = if fi.Exists then fi.Length else 0L
                        
                        let isNew, targetVideoId =
                            match Map.tryFind filePath existingVideoMap with
                            | Some id -> false, id
                            | None ->
                                let guidStr = Guid.NewGuid().ToString("N")
                                let videoId = $"v_{guidStr.Substring(0, 8)}"
                                true, videoId

                        if isNew then
                            // 新規登録時は重いメディア処理を待たず、まずファイル情報のみで即座にDB保存してスキャン・表示を優先
                            let newVideo = {
                                Id = targetVideoId
                                FileName = fileName
                                FilePath = filePath
                                Duration = 0L
                                FileSize = fileSize
                                ThumbnailPath = None
                                IsFavorite = false
                                CreatedAt = DateTime.UtcNow
                                AccessCount = 0
                                LastAccessedAt = None
                                Tags = []
                            }
                            let! saveResult = Db.saveVideo conn newVideo
                            match saveResult with
                            | Ok rows when rows > 0 ->
                                addedCount <- addedCount + 1
                                // 非同期バックグラウンドのサムネイル・メタデータ生成キューへ投入
                                MediaQueue.enqueue {
                                    VideoId = targetVideoId
                                    FilePath = filePath
                                    ThumbDir = thumbDir
                                    MediaProc = mediaProc
                                    ConnectionString = conn.ConnectionString
                                }
                            | _ -> ()
                        else
                            // 既存動画の場合でも、サムネイル画像が未作成・実ファイルが存在しない、またはメタデータが未取得 (Duration = 0L) の場合はキューへ再投入
                            let needsReprocessing =
                                match existingVideosResult with
                                | Ok list ->
                                    match list |> List.tryFind (fun v -> v.Id = targetVideoId) with
                                    | Some v ->
                                        let thumbMissing = 
                                            match v.ThumbnailPath with
                                            | None -> v.Duration = 0L
                                            | Some path -> not (File.Exists(path))
                                        let durationMissing = v.Duration = 0L
                                        thumbMissing || durationMissing
                                    | None -> true
                                | Error _ -> true
                            if needsReprocessing then
                                MediaQueue.enqueue {
                                    VideoId = targetVideoId
                                    FilePath = filePath
                                    ThumbDir = thumbDir
                                    MediaProc = mediaProc
                                    ConnectionString = conn.ConnectionString
                                }


                        // フォルダ名からタグを自動作成・付与（階層構造は維持しない、全子フォルダ対象）
                        let relativeDir = 
                            try
                                Path.GetRelativePath(videoDir, Path.GetDirectoryName(filePath))
                            with
                            | _ -> "."
                        
                        if relativeDir <> "." && relativeDir <> ".." && not (relativeDir.StartsWith(".." + string Path.DirectorySeparatorChar)) then
                            let folders = relativeDir.Split([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |], StringSplitOptions.RemoveEmptyEntries)
                            for folderName in folders do
                                let folderNameTrimmed = folderName.Trim()
                                if not (String.IsNullOrEmpty(folderNameTrimmed)) then
                                    let tagId =
                                        match tagCache |> Map.tryFind (folderNameTrimmed.ToLower()) with
                                        | Some id -> id
                                        | None ->
                                            let guidStr = Guid.NewGuid().ToString("N")
                                            let newId = $"t_{guidStr.Substring(0, 8)}"
                                            let newTag = {
                                                Tag.Id = newId
                                                Name = folderNameTrimmed
                                                ColorCode = "#64748b" // デフォルトのグレー
                                                ParentId = None
                                                VideoCount = 0
                                            }
                                            match Db.saveTag conn newTag |> Async.RunSynchronously with
                                            | Ok () ->
                                                tagCache <- tagCache |> Map.add (folderNameTrimmed.ToLower()) newId
                                                newId
                                            | Error _ -> 
                                                match Db.getTags conn |> Async.RunSynchronously with
                                                | Ok currentTags ->
                                                    match currentTags |> List.tryFind (fun t -> t.Name.ToLower() = folderNameTrimmed.ToLower()) with
                                                    | Some existingTag -> 
                                                        tagCache <- tagCache |> Map.add (folderNameTrimmed.ToLower()) existingTag.Id
                                                        existingTag.Id
                                                    | None -> newId
                                                | Error _ -> newId

                                    let! _ = Db.addTagToVideo conn targetVideoId tagId
                                    ()

                        // 自動タグ付けルールの評価と適用 (毎回 getVideos せず fileSize をそのまま利用)
                        let autoTagIds = TaggingEngine.evaluateRulesFull rules fileName filePath fileSize
                        for tagId in autoTagIds do
                            let! _ = Db.addTagToVideo conn targetVideoId tagId
                            ()

                    return Ok addedCount
            with
            | ex ->
                do! Db.saveErrorLog conn ex.Message (Some (ex.ToString()))
                return Error (QueryError ex.Message)
        }


    /// 新規作成・更新された自動タグ付けルールを、既存のすべての登録済み動画に逶及適用する
    let applyRuleToExistingVideos (conn: IDbConnection) (rule: TaggingRule) : Async<Result<int, DbError>> =
        async {
            try
                // 1. データベースからすべての動画レコードを取得
                let! videosResult = Db.getVideos conn None
                match videosResult with
                | Error err -> return Error err
                | Ok videos ->
                    let mutable appliedCount = 0
                    for video in videos do
                        // 2. ルールに合致するか判定（ファイル名・パス・サイズすべてを考慮）
                        if TaggingEngine.isRuleMatch rule video.FileName video.FilePath video.FileSize then
                            // すでに同じタグが付与されているかチェック
                            let alreadyHasTag = video.Tags |> List.exists (fun t -> t.Id = rule.TagId)
                            if not alreadyHasTag then
                                // 3. 中間テーブルにタグ紐付けを保存 (重複防止)
                                let! addResult = Db.addTagToVideo conn video.Id rule.TagId
                                match addResult with
                                | Ok () -> appliedCount <- appliedCount + 1
                                | Error _ -> ()
                    return Ok appliedCount
            with
            | ex ->
                do! Db.saveErrorLog conn ex.Message (Some (ex.ToString()))
                return Error (QueryError ex.Message)
        }

