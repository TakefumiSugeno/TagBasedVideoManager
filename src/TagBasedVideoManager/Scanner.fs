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

                    let uniqueFiles =
                        [
                            for root in existingRoots do
                                for file in enumerateFiles root do
                                    let ext = Path.GetExtension(file).ToLowerInvariant()

                                    if Array.contains ext supportedExtensions then
                                        let physical = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath file
                                        let norm = Path.GetFullPath(physical)

                                        if seenFiles.Add(norm) then
                                            yield norm
                        ]

                    // 3. 既存の動画一覧を取得して、正規化物理パス -> Video のディクショナリを作成
                    let! existingVideosResult = Db.getVideos conn None

                    match existingVideosResult with
                    | Error err -> return Error err
                    | Ok existingVideosList ->

                    let existingVideoDict = System.Collections.Generic.Dictionary<string, Video>(pathComparer)

                    for v in existingVideosList do
                        let normKey =
                            Path.GetFullPath(TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath v.FilePath)

                        existingVideoDict.[normKey] <- v

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

                    let mutable addedCount = 0

                    for normFilePath in uniqueFiles do
                        let fileName = Path.GetFileName normFilePath
                        let fi = FileInfo(normFilePath)
                        let fileSize = if fi.Exists then fi.Length else 0L
                        let fileModifiedAt = if fi.Exists then fi.LastWriteTimeUtc else DateTime.UtcNow

                        let existingOpt =
                            match existingVideoDict.TryGetValue(normFilePath) with
                            | true, v -> Some v
                            | false, _ -> None

                        let isNew, targetVideoId =
                            match existingOpt with
                            | Some v -> false, v.Id
                            | None ->
                                let guidStr = Guid.NewGuid().ToString("N")
                                let videoId = $"v_{guidStr.Substring(0, 8)}"
                                true, videoId

                        if isNew then
                            // 新規登録時は重いメディア処理を待たず、まずファイル情報のみで即座にDB保存してスキャン・表示を優先
                            let newVideo = {
                                Id = targetVideoId
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
                            }

                            let! saveResult = Db.saveVideo conn newVideo

                            match saveResult with
                            | Ok rows when rows > 0 ->
                                addedCount <- addedCount + 1
                                existingVideoDict.[normFilePath] <- newVideo

                                let fileName = Path.GetFileName(normFilePath)
                                let fnBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
                                if fnBytes > 255 then
                                    // 255バイト制限 (NAME_MAX) 超過警告を記録し、外部メディアキューへの投入をスキップ (FFmpegハング防止)
                                    do! Db.saveErrorLog conn $"[Scanner Warning] 動画ファイル名が Linux の 255 バイト制限 (NAME_MAX) を超過しているため、Linux/WSL/Docker 環境で再生やメディア処理が失敗する可能性があります。VideoId={targetVideoId}, FilePath={normFilePath}, Bytes={fnBytes}" None
                                else
                                    // 非同期バックグラウンドのサムネイル・メタデータ生成キューへ投入
                                    MediaQueue.enqueue {
                                        VideoId = targetVideoId
                                        FilePath = normFilePath
                                        ThumbDir = thumbDir
                                        MediaProc = mediaProc
                                        ConnectionString = conn.ConnectionString
                                    }
                            | _ -> ()
                        else
                            match existingOpt with
                            | Some v ->
                                let fileName = Path.GetFileName(normFilePath)
                                let fnBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
                                let isFileNameTooLong = fnBytes > 255

                                // 既存動画の場合でも、サムネイル画像が未作成・実ファイルが存在しない、またはメタデータが未取得 (Duration = 0L) の場合はキューへ再投入
                                let thumbMissing =
                                    match v.ThumbnailPath with
                                    | None -> true
                                    | Some path -> not (File.Exists(path))

                                let durationMissing = v.Duration = 0L

                                if (thumbMissing || durationMissing) && not isFileNameTooLong then
                                    MediaQueue.enqueue {
                                        VideoId = targetVideoId
                                        FilePath = normFilePath
                                        ThumbDir = thumbDir
                                        MediaProc = mediaProc
                                        ConnectionString = conn.ConnectionString
                                    }

                                // 既存動画の FileModifiedAt バックフィル（UTC基準で厳密に比較し無駄な更新を抑止）
                                let diffSeconds =
                                    Math.Abs(
                                        (v.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime())
                                            .TotalSeconds
                                    )

                                if diffSeconds > 1.0 then
                                    let! _ = Db.updateVideoFileModifiedAt conn targetVideoId fileModifiedAt
                                    ()
                            | None -> ()

                        // タグを動画に関連付け、インメモリ辞書も同期更新するヘルパー
                        let attachTag (tagId: string) (tagName: string) =
                            async {
                                let alreadyHasTag =
                                    match existingVideoDict.TryGetValue(normFilePath) with
                                    | true, v -> v.Tags |> List.exists (fun t -> t.Id = tagId)
                                    | false, _ -> false

                                if not alreadyHasTag then
                                    let! _ = Db.addTagToVideo conn targetVideoId tagId

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

                        // フォルダ名からタグを自動作成・付与（入れ子ルート対応・全所属ルートの和集合）
                        let folderTags = deriveAllFolderTags videoDirs normFilePath

                        for folderName in folderTags do
                            let folderNameTrimmed = folderName.Trim()

                            if not (String.IsNullOrEmpty(folderNameTrimmed)) then
                                let! tagId = getOrCreateTagId folderNameTrimmed
                                do! attachTag tagId folderNameTrimmed

                        // 自動タグ付けルールの評価と適用 (毎回 getVideos せず fileSize をそのまま利用)
                        let autoTagIds =
                            TaggingEngine.evaluateRulesFull rules fileName normFilePath fileSize

                        for tagId in autoTagIds do
                            do! attachTag tagId ""

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

