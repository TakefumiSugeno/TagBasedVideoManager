namespace TagBasedVideoManager.Application

open System
open System.IO
open System.Data
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
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
    /// 走査例外が発生したディレクトリパスは failedDirs に記録される (SE-201)。
    let rec private enumerateFilesSafe (dir: string) (failedDirs: System.Collections.Generic.HashSet<string>) : string list =
        try
            let resolvedDir = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath dir
            if not (Directory.Exists(resolvedDir)) then []
            else
                let files =
                    try
                        Directory.GetFiles(resolvedDir) |> Array.toList
                    with ex ->
                        eprintfn $"[Scanner Warning] Failed to enumerate files in '%s{dir}' (%s{resolvedDir}): %s{ex.Message}"
                        failedDirs.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))) |> ignore
                        failedDirs.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedDir))) |> ignore
                        []

                let subDirs =
                    try
                        Directory.GetDirectories(resolvedDir)
                    with ex ->
                        eprintfn $"[Scanner Warning] Failed to enumerate subdirectories in '%s{dir}' (%s{resolvedDir}): %s{ex.Message}"
                        failedDirs.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))) |> ignore
                        failedDirs.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedDir))) |> ignore
                        [||]

                let subFiles = 
                    subDirs 
                    |> Array.toList 
                    |> List.collect (fun sub -> enumerateFilesSafe sub failedDirs)

                List.append files subFiles
        with ex ->
            eprintfn $"[Scanner Warning] Error accessing directory '%s{dir}': %s{ex.Message}"
            failedDirs.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))) |> ignore
            []

    /// サムネイルの期待パスと既存パスの実在を確認し、利用可能なサムネイルパスと存在有無を解決する (DRY化)
    let private resolveThumbnail (thumbDir: string) (videoId: string) (currentThumbPath: string option) : string option * bool =
        let expectedThumbPath = Path.Combine(thumbDir, $"{videoId}.jpg")
        let expectedThumbExists = File.Exists(expectedThumbPath)
        if expectedThumbExists then
            Some expectedThumbPath, true
        else
            match currentThumbPath with
            | Some p when File.Exists(p) -> Some p, true
            | _ -> None, false

    /// メディア処理キュー投入判定（Linux 255バイト制限チェックとログ記録を含む）(DRY化)
    let private enqueueMediaIfNeeded
        (conn: IDbConnection)
        (mediaProc: IMediaProcessor)
        (thumbDir: string)
        (videoId: string)
        (filePath: string)
        (fileName: string)
        (hasThumbnail: bool)
        (duration: int64)
        : Async<unit> =
        async {
            let fnBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
            if fnBytes > 255 then
                let msg = $"[Scanner Warning] 動画ファイル名が Linux の 255 バイト制限 (NAME_MAX) を超過しているため、Linux/WSL/Docker 環境で再生やメディア処理が失敗する可能性があります。VideoId={videoId}, FilePath={filePath}, Bytes={fnBytes}"
                let! _ = Db.saveErrorLog conn msg None
                ()
            elif (not hasThumbnail) || duration = 0L then
                MediaQueue.enqueue {
                    VideoId = videoId
                    FilePath = filePath
                    ThumbDir = thumbDir
                    MediaProc = mediaProc
                    ConnectionString = conn.ConnectionString
                }
        }

    /// 指定されたファイルパスが基準ディレクトリ配下に存在するかを判定する。
    let private isSubPathOf (baseDir: string) (filePath: string) : bool =
        try
            if String.IsNullOrWhiteSpace baseDir || String.IsNullOrWhiteSpace filePath then false
            else
                let normBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath (baseDir.Trim())))
                let normFile = Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath (filePath.Trim()))
                let rel = Path.GetRelativePath(normBase, normFile)
                if rel = "." then false
                elif rel = ".."
                     || rel.StartsWith(".." + string Path.DirectorySeparatorChar)
                     || rel.StartsWith(".." + string Path.AltDirectorySeparatorChar)
                     || Path.IsPathRooted(rel) then
                    false
                else
                    true
        with _ -> false

    /// ルートディレクトリとファイルパスから付与すべきフォルダ名タグ（ルート名＋サブフォルダ名）を導出する。
    /// ファイルがルート配下にない場合は空リストを返す（DT-01/DT-02）。
    let deriveFolderTags (rootDir: string) (filePath: string) : string list =
        try
            if String.IsNullOrWhiteSpace(rootDir) || String.IsNullOrWhiteSpace(filePath) then
                []
            else
                let normRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir))
                let leaf = Path.GetFileName(normRoot)

                if String.IsNullOrEmpty(leaf) then
                    []
                else
                    let normFile = Path.GetFullPath(filePath)
                    let fileDir = Path.GetDirectoryName(normFile)

                    if String.IsNullOrEmpty(fileDir) then
                        []
                    else
                        let checkRel (baseDir: string) =
                            try
                                let rel = Path.GetRelativePath(baseDir, fileDir)

                                if rel = "." then
                                    Some "."
                                elif
                                    rel = ".."
                                    || rel.StartsWith(".." + string Path.DirectorySeparatorChar)
                                    || rel.StartsWith(".." + string Path.AltDirectorySeparatorChar)
                                    || Path.IsPathRooted(rel)
                                then
                                    None
                                else
                                    Some rel
                            with _ ->
                                None

                        let relOpt =
                            match checkRel normRoot with
                            | Some r -> Some r
                            | None ->
                                let resolvedRoot =
                                    Path.TrimEndingDirectorySeparator(
                                        TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath normRoot
                                    )

                                if resolvedRoot <> normRoot then
                                    checkRel resolvedRoot
                                else
                                    None

                        match relOpt with
                        | None -> []
                        | Some "." -> [ leaf ]
                        | Some rel ->
                            let subFolders =
                                rel.Split(
                                    [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |],
                                    StringSplitOptions.RemoveEmptyEntries
                                )
                                |> Array.map (fun s -> s.Trim())
                                |> Array.filter (fun s -> not (String.IsNullOrEmpty(s)))
                                |> Array.toList

                            let seen = System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            (leaf :: subFolders) |> List.filter (fun s -> seen.Add(s))
        with _ ->
            []

    /// 複数のルートディレクトリから、ファイルが所属するすべてのルートのタグを和集合（重複なし、順序安定）で導出する。
    let deriveAllFolderTags (rootDirs: string list) (filePath: string) : string list =
        let seen = System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let result = System.Collections.Generic.List<string>()

        for r in rootDirs do
            let tags = deriveFolderTags r filePath

            for t in tags do
                if seen.Add(t) then
                    result.Add(t)

        result |> Seq.toList

    /// 複数の動画ディレクトリを再帰走査し、正規化物理パスで重複を除去してDB同期を行う。
    let syncVideoDirectories
        (videoDirs: string list)
        (thumbDir: string)
        (mediaProc: IMediaProcessor)
        (conn: IDbConnection)
        : Async<Result<int, DbError>> =
        async {
            try
                if conn.State <> ConnectionState.Open then
                    conn.Open()

                if not (String.IsNullOrEmpty(thumbDir)) && not (Directory.Exists(thumbDir)) then
                    try
                        Directory.CreateDirectory(thumbDir) |> ignore
                    with ex ->
                        eprintfn $"[Scanner Warning] Failed to create thumbDir '%s{thumbDir}': %s{ex.Message}"

                // 1. 存在するルートディレクトリのみを抽出（不在ルートは警告ログを出力してスキップ）
                let existingRoots =
                    videoDirs
                    |> List.choose (fun dir ->
                        if String.IsNullOrWhiteSpace(dir) then
                            None
                        else
                            let trimmed = dir.Trim()

                            if Directory.Exists(trimmed) then
                                Some trimmed
                            else
                                eprintfn $"[Scanner Warning] Directory does not exist, skipping: %s{trimmed}"
                                None)

                if List.isEmpty existingRoots then
                    return Ok 0
                else
                    // 2. 全ルート配下のファイルを走査し、対応拡張子かつ物理パス正規化・重複排除
                    let pathComparer =
                        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                            StringComparer.OrdinalIgnoreCase
                        else
                            StringComparer.Ordinal

                    let seenFiles = System.Collections.Generic.HashSet<string>(pathComparer)
                    let failedDirs = System.Collections.Generic.HashSet<string>(pathComparer)

                    let uniqueFiles =
                        [
                            for root in existingRoots do
                                for file in enumerateFilesSafe root failedDirs do
                                    let ext = Path.GetExtension(file).ToLowerInvariant()

                                    if Array.contains ext supportedExtensions then
                                        let physical = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath file
                                        let norm = Path.GetFullPath(physical)

                                        if seenFiles.Add(norm) then
                                            yield norm
                        ]

                    if failedDirs.Count > 0 then
                        for fd in failedDirs do
                            do! Db.saveErrorLog conn $"[Scanner Warning] ディレクトリの走査中に例外が発生したため、誤削除防止ガードにより配下の動画レコードを保護しました: {fd}" None

                    // 3. 既存の動画一覧を取得して、正規化物理パス -> Video のディクショナリを作成
                    let! existingVideosResult = Db.getVideos conn None

                    match existingVideosResult with
                    | Error err -> return Error err
                    | Ok existingVideosList ->

                    let existingVideoDict = System.Collections.Generic.Dictionary<string, Video>(pathComparer)
                    let unmatchedDbVideos = System.Collections.Generic.Dictionary<string, Video>()

                    for v in existingVideosList do
                        let normKey =
                            Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath v.FilePath)

                        existingVideoDict.[normKey] <- v
                        unmatchedDbVideos.[v.Id] <- v

                    // 4. 自動タグ付けルール一覧を取得
                    let! rulesResult = Db.getTaggingRules conn

                    match rulesResult with
                    | Error err -> return Error err
                    | Ok rules ->

                    // 5. タグ一覧をキャッシュ化してフォルダタグ自動作成に備える
                    let! tagsResult = Db.getTags conn

                    match tagsResult with
                    | Error err -> return Error err
                    | Ok tagsList ->

                    let mutable tagCache =
                        tagsList
                        |> List.map (fun t -> t.Name.ToLowerInvariant(), t.Id)
                        |> Map.ofList

                    let getOrCreateTagId (tagName: string) =
                        async {
                            let key = tagName.ToLowerInvariant()

                            match tagCache |> Map.tryFind key with
                            | Some id -> return id
                            | None ->
                                let guidStr = Guid.NewGuid().ToString("N")
                                let newId = $"t_{guidStr.Substring(0, 8)}"

                                let newTag = {
                                    Tag.Id = newId
                                    Name = tagName
                                    ColorCode = "#64748b" // デフォルトのグレー
                                    ParentId = None
                                    VideoCount = 0
                                }

                                let! saveRes = Db.saveTag conn newTag

                                match saveRes with
                                | Ok () ->
                                    tagCache <- tagCache |> Map.add key newId
                                    return newId
                                | Error _ ->
                                    let! currentTagsRes = Db.getTags conn

                                    match currentTagsRes with
                                    | Ok currentTags ->
                                        match
                                            currentTags
                                            |> List.tryFind (fun t ->
                                                String.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase))
                                        with
                                        | Some existingTag ->
                                            tagCache <- tagCache |> Map.add key existingTag.Id
                                            return existingTag.Id
                                        | None ->
                                            tagCache <- tagCache |> Map.add key newId
                                            return newId
                                    | Error _ -> return newId
                        }

                    let activeVideoIds = System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    let mutable addedCount = 0

                    // タグを動画に関連付け、インメモリ辞書も同期更新するヘルパー
                    let attachTag (videoId: string) (normFilePath: string) (tagId: string) (tagName: string) =
                        async {
                            let alreadyHasTag =
                                match existingVideoDict.TryGetValue(normFilePath) with
                                | true, v -> v.Tags |> List.exists (fun t -> t.Id = tagId)
                                | false, _ -> false

                            if not alreadyHasTag then
                                let! _ = Db.addTagToVideo conn videoId tagId

                                match existingVideoDict.TryGetValue(normFilePath) with
                                | true, v ->
                                    let updated = {
                                        v with
                                            Tags = {
                                                Tag.Id = tagId
                                                Name = tagName
                                                ColorCode = "#64748b"
                                                ParentId = None
                                                VideoCount = 0
                                            }
                                                   :: v.Tags
                                    }

                                    existingVideoDict.[normFilePath] <- updated
                                | false, _ -> ()
                        }

                    let applyTagsForVideo (videoId: string) (normFilePath: string) (fileName: string) (fileSize: int64) =
                        async {
                            // フォルダ名からタグを自動作成・付与（入れ子ルート対応・全所属ルートの和集合）
                            let folderTags = deriveAllFolderTags videoDirs normFilePath

                            for folderName in folderTags do
                                let folderNameTrimmed = folderName.Trim()

                                if not (String.IsNullOrEmpty(folderNameTrimmed)) then
                                    let! tagId = getOrCreateTagId folderNameTrimmed
                                    do! attachTag videoId normFilePath tagId folderNameTrimmed

                            // 自動タグ付けルールの評価と適用 (毎回 getVideos せず fileSize をそのまま利用)
                            let autoTagIds =
                                TaggingEngine.evaluateRulesFull rules fileName normFilePath fileSize

                            for tagId in autoTagIds do
                                do! attachTag videoId normFilePath tagId ""
                        }

                    // ファイルを matchedFiles (Phase 1) と unmatchedFiles (Phase 2) に分離
                    let matchedFiles = ResizeArray<string>()
                    let unmatchedFiles = ResizeArray<string>()

                    for normFilePath in uniqueFiles do
                        if existingVideoDict.ContainsKey(normFilePath) then
                            matchedFiles.Add(normFilePath)
                        else
                            unmatchedFiles.Add(normFilePath)

                    // -----------------------------------------------------------------
                    // Phase 1: 正規化パス一致・UUIDバックフィル・サムネイル解決
                    // -----------------------------------------------------------------
                    for normFilePath in matchedFiles do
                        let fi = FileInfo(normFilePath)
                        if not fi.Exists then
                            ()
                        else
                            let v = existingVideoDict.[normFilePath]
                            activeVideoIds.Add(v.Id) |> ignore
                            unmatchedDbVideos.Remove(v.Id) |> ignore

                            let fileName = Path.GetFileName normFilePath
                            let fileSize = fi.Length
                            let fileModifiedAt = fi.LastWriteTimeUtc

                            // UUID バックフィル
                            if v.FileUuid.IsNone then
                                match FileIdentity.computeFileUuid normFilePath with
                                | Ok uuid ->
                                    let! updateUuidRes = Db.updateVideoFileUuid conn v.Id uuid
                                    match updateUuidRes with
                                    | Ok _ ->
                                        let updated = { v with FileUuid = Some uuid }
                                        existingVideoDict.[normFilePath] <- updated
                                    | Error err ->
                                        do! Db.saveErrorLog conn $"Failed to update file UUID for video {v.Id}: {err}" None
                                | Error _ -> ()

                            let currentVideo = existingVideoDict.[normFilePath]

                            // サムネイル解決 (DRY)
                            let finalThumbOpt, thumbExists = resolveThumbnail thumbDir currentVideo.Id currentVideo.ThumbnailPath

                            if thumbExists && currentVideo.ThumbnailPath <> finalThumbOpt then
                                let! updateMetaRes = Db.updateVideoMetadata conn currentVideo.Id currentVideo.Duration finalThumbOpt
                                match updateMetaRes with
                                | Ok _ ->
                                    let updated = { currentVideo with ThumbnailPath = finalThumbOpt }
                                    existingVideoDict.[normFilePath] <- updated
                                | Error err ->
                                    do! Db.saveErrorLog conn $"Failed to update video metadata for {currentVideo.Id}: {err}" None

                            let latestVideo = existingVideoDict.[normFilePath]

                            // 再処理判定 (DRY)
                            do! enqueueMediaIfNeeded conn mediaProc thumbDir latestVideo.Id normFilePath fileName thumbExists latestVideo.Duration

                            // 更新日時バックフィル（UTC基準で厳密に比較し無駄な更新を抑止）
                            let diffSeconds =
                                Math.Abs(
                                    (latestVideo.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime())
                                        .TotalSeconds
                                )

                            if diffSeconds > 1.0 then
                                let! updateModRes = Db.updateVideoFileModifiedAt conn latestVideo.Id fileModifiedAt
                                match updateModRes with
                                | Ok _ -> ()
                                | Error err ->
                                    do! Db.saveErrorLog conn $"Failed to update fileModifiedAt for {latestVideo.Id}: {err}" None

                            // フォルダタグおよび自動ルールタグ適用
                            do! applyTagsForVideo latestVideo.Id normFilePath fileName fileSize

                    // -----------------------------------------------------------------
                    // Phase 2: 未登録パスファイルの UUID 移動・リネーム追従（または旧レコードフォールバック）
                    // -----------------------------------------------------------------
                    // unmatchedDbVideos の O(1) ルックアップインデックス構築
                    let uuidIndex = System.Collections.Generic.Dictionary<string, ResizeArray<Video>>(StringComparer.OrdinalIgnoreCase)
                    let fileSizeIndex = System.Collections.Generic.Dictionary<int64, ResizeArray<Video>>()

                    for v in unmatchedDbVideos.Values do
                        match v.FileUuid with
                        | Some u ->
                            match uuidIndex.TryGetValue(u) with
                            | true, list -> list.Add(v)
                            | false, _ ->
                                let list = ResizeArray<Video>()
                                list.Add(v)
                                uuidIndex.[u] <- list
                        | None -> ()

                        match fileSizeIndex.TryGetValue(v.FileSize) with
                        | true, list -> list.Add(v)
                        | false, _ ->
                            let list = ResizeArray<Video>()
                            list.Add(v)
                            fileSizeIndex.[v.FileSize] <- list

                    for normFilePath in unmatchedFiles do
                        let fi = FileInfo(normFilePath)
                        if not fi.Exists then
                            () // 走査後に外部削除されたファイルはスキップ
                        else
                            let fileName = Path.GetFileName normFilePath
                            let fileSize = fi.Length
                            let fileModifiedAt = fi.LastWriteTimeUtc
                            let computedUuidOpt =
                                match FileIdentity.computeFileUuid normFilePath with
                                | Ok u -> Some u
                                | Error _ -> None

                            // 候補検索
                            let candidateOpt =
                                // 1. UUID 一致候補
                                let uuidCandidates =
                                    match computedUuidOpt with
                                    | Some uuid ->
                                        match uuidIndex.TryGetValue(uuid) with
                                        | true, list ->
                                            list
                                            |> Seq.filter (fun c -> unmatchedDbVideos.ContainsKey(c.Id))
                                            |> Seq.toList
                                        | false, _ -> []
                                    | None -> []

                                if not (List.isEmpty uuidCandidates) then
                                    uuidCandidates
                                    |> List.sortBy (fun v ->
                                        let nameMatch = if String.Equals(v.FileName, fileName, StringComparison.OrdinalIgnoreCase) then 0 else 1
                                        let timeDiff = Math.Abs((v.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime()).TotalSeconds)
                                        (nameMatch, timeDiff, v.Id))
                                    |> List.tryHead
                                else
                                    // 2. 旧レコードフォールバック (FileUuid が None のレコードのみ)
                                    let fallbackCandidates =
                                        match fileSizeIndex.TryGetValue(fileSize) with
                                        | true, list ->
                                            list
                                            |> Seq.filter (fun c ->
                                                unmatchedDbVideos.ContainsKey(c.Id)
                                                && c.FileUuid.IsNone
                                                && (String.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                                    || Math.Abs((c.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime()).TotalSeconds) <= 2.0))
                                            |> Seq.toList
                                        | false, _ -> []

                                    fallbackCandidates
                                    |> List.sortBy (fun v ->
                                        let nameMatch = if String.Equals(v.FileName, fileName, StringComparison.OrdinalIgnoreCase) then 0 else 1
                                        let timeDiff = Math.Abs((v.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime()).TotalSeconds)
                                        (nameMatch, timeDiff, v.Id))
                                    |> List.tryHead

                            match candidateOpt with
                            | Some v ->
                                // 移動・リネーム追従（引き継ぎ）
                                unmatchedDbVideos.Remove(v.Id) |> ignore
                                activeVideoIds.Add(v.Id) |> ignore

                                let finalThumbOpt, thumbExists = resolveThumbnail thumbDir v.Id v.ThumbnailPath

                                let! updateLocRes =
                                    Db.updateVideoLocation conn v.Id fileName normFilePath fileSize fileModifiedAt computedUuidOpt finalThumbOpt

                                match updateLocRes with
                                | Ok _ ->
                                    let updatedVideo = {
                                        v with
                                            FileName = fileName
                                            FilePath = normFilePath
                                            FileSize = fileSize
                                            FileModifiedAt = fileModifiedAt
                                            FileUuid = computedUuidOpt
                                            ThumbnailPath = finalThumbOpt
                                    }
                                    existingVideoDict.[normFilePath] <- updatedVideo

                                    do! enqueueMediaIfNeeded conn mediaProc thumbDir v.Id normFilePath fileName thumbExists v.Duration
                                    do! applyTagsForVideo v.Id normFilePath fileName fileSize
                                | Error err ->
                                    do! Db.saveErrorLog conn $"Failed to update video location for {v.Id}: {err}" None

                            | None ->
                                // 完全新規動画
                                let guidStr = Guid.NewGuid().ToString("N")
                                let videoId = $"v_{guidStr.Substring(0, 8)}"
                                activeVideoIds.Add(videoId) |> ignore

                                let newVideo = {
                                    Id = videoId
                                    FileName = fileName
                                    FilePath = normFilePath
                                    Duration = 0L
                                    FileSize = fileSize
                                    ThumbnailPath = None
                                    IsFavorite = false
                                    CreatedAt = DateTime.UtcNow
                                    FileModifiedAt = fileModifiedAt
                                    AccessCount = 0
                                    LastAccessedAt = None
                                    Tags = []
                                    FileUuid = computedUuidOpt
                                }

                                let! saveResult = Db.saveVideo conn newVideo

                                match saveResult with
                                | Ok rows when rows > 0 ->
                                    addedCount <- addedCount + 1
                                    existingVideoDict.[normFilePath] <- newVideo

                                    do! enqueueMediaIfNeeded conn mediaProc thumbDir videoId normFilePath fileName false 0L
                                    do! applyTagsForVideo videoId normFilePath fileName fileSize
                                | Ok _ -> ()
                                | Error err ->
                                    do! Db.saveErrorLog conn $"Failed to save new video {videoId}: {err}" None

                    // -----------------------------------------------------------------
                    // Phase 3: 不在 DB レコードおよび孤児サムネイルの自動削除
                    // -----------------------------------------------------------------
                    for KeyValue(unmatchedId, v) in unmatchedDbVideos do
                        let isInFailedDir =
                            failedDirs
                            |> Seq.exists (fun failedDir ->
                                isSubPathOf failedDir v.FilePath
                                || (
                                    try
                                        let normDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath failedDir))
                                        let fileDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath (Path.GetDirectoryName v.FilePath)))
                                        String.Equals(normDir, fileDir, StringComparison.OrdinalIgnoreCase)
                                    with _ -> false
                                ))

                        if isInFailedDir then
                            // 走査例外が発生したディレクトリ配下のファイルは誤削除防止のため保護する (SE-201)
                            activeVideoIds.Add(v.Id) |> ignore
                        else
                            let belongsToExistingRoot = existingRoots |> List.exists (fun root -> isSubPathOf root v.FilePath)
                            let belongsToAnyConfiguredRoot = videoDirs |> List.exists (fun d -> not (String.IsNullOrWhiteSpace d) && isSubPathOf (d.Trim()) v.FilePath)
                            let shouldDelete = belongsToExistingRoot || not belongsToAnyConfiguredRoot

                            if shouldDelete then
                                let! delRes = Db.deleteVideo conn v.Id
                                match delRes with
                                | Ok () ->
                                    let expectedThumb = Path.Combine(thumbDir, $"{v.Id}.jpg")
                                    if File.Exists(expectedThumb) then
                                        try File.Delete(expectedThumb) with _ -> ()
                                    match v.ThumbnailPath with
                                    | Some p when File.Exists(p) && not (String.Equals(Path.GetFullPath(p), Path.GetFullPath(expectedThumb), StringComparison.OrdinalIgnoreCase)) ->
                                        try File.Delete(p) with _ -> ()
                                    | _ -> ()
                                | Error err ->
                                    do! Db.saveErrorLog conn $"Failed to delete video {v.Id}: {err}" None
                            else
                                activeVideoIds.Add(v.Id) |> ignore

                    // 孤児サムネイル画像の削除
                    if not (String.IsNullOrEmpty(thumbDir)) && Directory.Exists(thumbDir) then
                        for filePath in Directory.GetFiles(thumbDir, "*.jpg") do
                            let fn = Path.GetFileNameWithoutExtension(filePath)
                            if fn.StartsWith("v_") && not (activeVideoIds.Contains(fn)) then
                                try File.Delete(filePath) with _ -> ()

                    return Ok addedCount
            with ex ->
                do! Db.saveErrorLog conn ex.Message (Some(ex.ToString()))
                return Error(QueryError ex.Message)
        }

    /// スキャン処理のメインワークフロー (単一パス互換用)
    let syncVideoDirectory
        (videoDir: string)
        (thumbDir: string)
        (mediaProc: IMediaProcessor)
        (conn: IDbConnection)
        : Async<Result<int, DbError>> =
        syncVideoDirectories [ videoDir ] thumbDir mediaProc conn


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

