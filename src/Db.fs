namespace TagBasedVideoManager.Infrastructure

open System
open System.Data
open Microsoft.Data.Sqlite
open Dapper
open TagBasedVideoManager.Domain

/// データベース内の物理カラムとマッピングするためのフラットなDTO定義 (F# option型を避ける)
[<CLIMutable>]
type private VideoDbDto = {
    Id: string
    FileName: string
    FilePath: string
    Duration: int64
    FileSize: int64
    ThumbnailPath: string
    IsFavorite: int
    CreatedAt: string
    AccessCount: int
    LastAccessedAt: string
}

[<CLIMutable>]
type private TagDbDto = {
    Id: string
    Name: string
    ColorCode: string
    ParentId: string
    VideoCount: int
}

[<CLIMutable>]
type private TaggingRuleDbDto = {
    Id: string
    Pattern: string
    TagId: string
    MatchType: string
    TargetField: string
    MinSize: Nullable<int64>
    MaxSize: Nullable<int64>
    CreatedAt: string
}

[<CLIMutable>]
type private SmartFolderDbDto = {
    Id: string
    Name: string
    Query: string
    CreatedAt: string
}

[<CLIMutable>]
type private ErrorLogDbDto = {
    Id: int
    Message: string
    StackTrace: string
    CreatedAt: string
}


module Db =
    /// データベース接続を取得する
    let getConnection (dbPath: string) : IDbConnection =
        new SqliteConnection($"Data Source={dbPath}")

    let importTags (conn: IDbConnection) (tags: Tag list) : Async<Result<Tag list, DbError>> =
        async {
            try
                if conn.State <> ConnectionState.Open then conn.Open()
                use tx = conn.BeginTransaction()
                let sql = "INSERT OR REPLACE INTO Tags (Id, Name, ColorCode, ParentId) VALUES (@Id, @Name, @ColorCode, @ParentId)"
                for tag in tags do
                    let! _ = conn.ExecuteAsync(sql, {| Id = tag.Id; Name = tag.Name; ColorCode = tag.ColorCode; ParentId = Option.toObj tag.ParentId |}, transaction = tx) |> Async.AwaitTask
                    ()
                tx.Commit()
                return Ok tags
            with ex ->
                return Error (QueryError ex.Message)
        }

    let importRules (conn: IDbConnection) (rules: TaggingRule list) : Async<Result<TaggingRule list, DbError>> =
        async {
            try
                if conn.State <> ConnectionState.Open then conn.Open()
                use tx = conn.BeginTransaction()
                let sql = "INSERT OR REPLACE INTO TaggingRules (Id, Pattern, TagId, MatchType, TargetField, MinSize, MaxSize, CreatedAt) VALUES (@Id, @Pattern, @TagId, @MatchType, @TargetField, @MinSize, @MaxSize, @CreatedAt)"
                for rule in rules do
                    let! _ = conn.ExecuteAsync(sql, {| 
                        Id = rule.Id
                        Pattern = rule.Pattern
                        TagId = rule.TagId
                        MatchType = rule.MatchType
                        TargetField = rule.TargetField
                        MinSize = (match rule.MinSize with Some v -> Nullable(v) | None -> Nullable())
                        MaxSize = (match rule.MaxSize with Some v -> Nullable(v) | None -> Nullable())
                        CreatedAt = rule.CreatedAt.ToString("o")
                    |}, transaction = tx) |> Async.AwaitTask
                    ()
                tx.Commit()
                return Ok rules
            with ex ->
                return Error (QueryError ex.Message)
        }

    let importSmartFolders (conn: IDbConnection) (folders: SmartFolder list) : Async<Result<SmartFolder list, DbError>> =
        async {
            try
                if conn.State <> ConnectionState.Open then conn.Open()
                use tx = conn.BeginTransaction()
                let sql = "INSERT OR REPLACE INTO SmartFolders (Id, Name, Query, CreatedAt) VALUES (@Id, @Name, @Query, @CreatedAt)"
                for folder in folders do
                    let! _ = conn.ExecuteAsync(sql, {|
                        Id = folder.Id
                        Name = folder.Name
                        Query = folder.Query
                        CreatedAt = folder.CreatedAt.ToString("o")
                    |}, transaction = tx) |> Async.AwaitTask
                    ()
                tx.Commit()
                return Ok folders
            with ex ->
                return Error (QueryError ex.Message)
        }

    /// 動画一覧の取得
    let getVideos (conn: IDbConnection) (searchQuery: string option) : Async<Result<Video list, DbError>> =
        async {
            try
                let sql = "SELECT Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, AccessCount, LastAccessedAt FROM Videos"
                let! dtos = Async.AwaitTask (conn.QueryAsync<VideoDbDto>(sql))
                let videos = 
                    dtos
                    |> Seq.map (fun dto -> {
                        Id = dto.Id
                        FileName = dto.FileName
                        FilePath = dto.FilePath
                        Duration = dto.Duration
                        FileSize = dto.FileSize
                        ThumbnailPath = Option.ofObj dto.ThumbnailPath
                        IsFavorite = dto.IsFavorite <> 0
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                        AccessCount = dto.AccessCount
                        LastAccessedAt = if String.IsNullOrEmpty(dto.LastAccessedAt) then None else Some (DateTime.Parse(dto.LastAccessedAt))
                        Tags = []
                    })
                    |> Seq.toList

                // 各動画に紐付くタグ情報を結合取得する
                let! videosWithTags = 
                    videos 
                    |> List.map (fun v -> async {
                        let tagSql = """
                            SELECT t.Id, t.Name, t.ColorCode, t.ParentId 
                            FROM Tags t
                            INNER JOIN VideoTags vt ON t.Id = vt.TagId
                            WHERE vt.VideoId = @VideoId
                        """
                        let! tagDtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(tagSql, {| VideoId = v.Id |}))
                        let tags = 
                            tagDtos 
                            |> Seq.map (fun td -> {
                                Tag.Id = td.Id
                                Name = td.Name
                                ColorCode = td.ColorCode
                                ParentId = Option.ofObj td.ParentId
                                VideoCount = td.VideoCount
                            })
                            |> Seq.toList
                        return { v with Tags = tags }
                    })
                    |> Async.Sequential
                
                return Ok (Array.toList videosWithTags)
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 指定されたID of 動画情報を取得する
    let getVideo (conn: IDbConnection) (videoId: string) : Async<Result<Video, DbError>> =
        async {
            try
                let sql = "SELECT Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, AccessCount, LastAccessedAt FROM Videos WHERE Id = @Id"
                let! dto = Async.AwaitTask (conn.QuerySingleOrDefaultAsync<VideoDbDto>(sql, {| Id = videoId |}))
                if box dto = null then
                    return Error (RecordNotFound $"Video with id {videoId} not found")
                else
                    let video = {
                        Id = dto.Id
                        FileName = dto.FileName
                        FilePath = dto.FilePath
                        Duration = dto.Duration
                        FileSize = dto.FileSize
                        ThumbnailPath = Option.ofObj dto.ThumbnailPath
                        IsFavorite = dto.IsFavorite <> 0
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                        AccessCount = dto.AccessCount
                        LastAccessedAt = if String.IsNullOrEmpty(dto.LastAccessedAt) then None else Some (DateTime.Parse(dto.LastAccessedAt))
                        Tags = []
                    }

                    // タグ情報を結合
                    let tagSql = """
                        SELECT t.Id, t.Name, t.ColorCode, t.ParentId 
                        FROM Tags t
                        INNER JOIN VideoTags vt ON t.Id = vt.TagId
                        WHERE vt.VideoId = @VideoId
                    """
                    let! tagDtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(tagSql, {| VideoId = video.Id |}))
                    let tags = 
                        tagDtos 
                        |> Seq.map (fun td -> {
                            Tag.Id = td.Id
                            Name = td.Name
                            ColorCode = td.ColorCode
                            ParentId = Option.ofObj td.ParentId
                            VideoCount = td.VideoCount
                        })
                        |> Seq.toList

                    return Ok { video with Tags = tags }
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 指定された検索トークンリストに一致する動画一覧を取得する (親子階層タグ再帰ヒットを含む)
    let searchVideos (conn: IDbConnection) (tokens: SearchToken list) : Async<Result<Video list, DbError>> =
        async {
            try
                let mutable sql = """
                    SELECT DISTINCT v.Id, v.FileName, v.FilePath, v.Duration, v.FileSize, v.ThumbnailPath, v.IsFavorite, v.CreatedAt, v.AccessCount, v.LastAccessedAt
                    FROM Videos v
                """
                let parameters = DynamicParameters()

                // OR でトークンリストを分割する
                let rec splitByOr currentGroup acc list =
                    match list with
                    | [] -> 
                        if List.isEmpty currentGroup then acc else List.rev currentGroup :: acc
                    | OrOp :: tail ->
                        let nextAcc = if List.isEmpty currentGroup then acc else List.rev currentGroup :: acc
                        splitByOr [] nextAcc tail
                    | AndOp :: tail ->
                        splitByOr currentGroup acc tail
                    | head :: tail ->
                        splitByOr (head :: currentGroup) acc tail

                let groups = splitByOr [] [] tokens |> List.rev
                let mutable groupSqls = []
                let mutable paramCounter = 0

                for group in groups do
                    let mutable groupClauses = []
                    for token in group do
                        paramCounter <- paramCounter + 1
                        match token with
                        | Keyword kw ->
                            let paramName = $"@kw_{paramCounter}"
                            groupClauses <- $"v.FileName LIKE {paramName}" :: groupClauses
                            parameters.Add(paramName, "%" + kw + "%")
                        | FavoriteOnly true ->
                            groupClauses <- "v.IsFavorite = 1" :: groupClauses
                        | FavoriteOnly false ->
                            ()
                        | TagName tagName ->
                            let paramName = $"@tagName_{paramCounter}"
                            let subQuery = 
                                "EXISTS (" +
                                "  WITH RECURSIVE SubTags(Id) AS (" +
                                "    SELECT Id FROM Tags WHERE Name = " + paramName + " " +
                                "    UNION ALL " +
                                "    SELECT t.Id FROM Tags t INNER JOIN SubTags st ON t.ParentId = st.Id" +
                                "  )" +
                                "  SELECT 1 FROM VideoTags vt " +
                                "  WHERE vt.VideoId = v.Id AND vt.TagId IN (SELECT Id FROM SubTags)" +
                                ")"
                            groupClauses <- subQuery :: groupClauses
                            parameters.Add(paramName, tagName)
                        | AndOp | OrOp -> ()

                    if not (List.isEmpty groupClauses) then
                        let groupSql = "(" + String.concat " AND " (List.rev groupClauses) + ")"
                        groupSqls <- groupSql :: groupSqls

                if not (List.isEmpty groupSqls) then
                    let whereSql = String.concat " OR " (List.rev groupSqls)
                    sql <- sql + " WHERE " + whereSql

                let! dtos = Async.AwaitTask (conn.QueryAsync<VideoDbDto>(sql, parameters))
                let videos = 
                    dtos
                    |> Seq.map (fun dto -> {
                        Id = dto.Id
                        FileName = dto.FileName
                        FilePath = dto.FilePath
                        Duration = dto.Duration
                        FileSize = dto.FileSize
                        ThumbnailPath = Option.ofObj dto.ThumbnailPath
                        IsFavorite = dto.IsFavorite <> 0
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                        AccessCount = dto.AccessCount
                        LastAccessedAt = if String.IsNullOrEmpty(dto.LastAccessedAt) then None else Some (DateTime.Parse(dto.LastAccessedAt))
                        Tags = []
                    })
                    |> Seq.toList

                // 各動画に紐付くタグ情報を結合取得する
                let! videosWithTags = 
                    videos 
                    |> List.map (fun v -> async {
                        let tagSql = """
                            SELECT t.Id, t.Name, t.ColorCode, t.ParentId 
                            FROM Tags t
                            INNER JOIN VideoTags vt ON t.Id = vt.TagId
                            WHERE vt.VideoId = @VideoId
                        """
                        let! tagDtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(tagSql, {| VideoId = v.Id |}))
                        let tags = 
                            tagDtos 
                            |> Seq.map (fun td -> {
                                Tag.Id = td.Id
                                Name = td.Name
                                ColorCode = td.ColorCode
                                ParentId = Option.ofObj td.ParentId
                                VideoCount = td.VideoCount
                            })
                            |> Seq.toList
                        return { v with Tags = tags }
                    })
                    |> Async.Sequential
                
                return Ok (Array.toList videosWithTags)
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 全タグ一覧の取得
    let getTags (conn: IDbConnection) : Async<Result<Tag list, DbError>> =
        async {
            try
                let sql = 
                    "WITH RECURSIVE TagHierarchy(ParentId, ChildId) AS ( " +
                    "    SELECT Id, Id FROM Tags " +
                    "    UNION ALL " +
                    "    SELECT th.ParentId, t.Id FROM TagHierarchy th JOIN Tags t ON t.ParentId = th.ChildId " +
                    ") " +
                    "SELECT t.Id, t.Name, t.ColorCode, t.ParentId, " +
                    "       (SELECT COUNT(DISTINCT vt.VideoId) FROM TagHierarchy th JOIN VideoTags vt ON th.ChildId = vt.TagId WHERE th.ParentId = t.Id) AS VideoCount " +
                    "FROM Tags t " +
                    "GROUP BY t.Id, t.Name, t.ColorCode, t.ParentId"
                let! dtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(sql))
                let tags = 
                    dtos
                    |> Seq.map (fun dto -> {
                        Tag.Id = dto.Id
                        Name = dto.Name
                        ColorCode = dto.ColorCode
                        ParentId = Option.ofObj dto.ParentId
                        VideoCount = dto.VideoCount
                    })
                    |> Seq.toList
                return Ok tags
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 新規タグの作成・保存
    let saveTag (conn: IDbConnection) (tag: Tag) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "INSERT INTO Tags (Id, Name, ColorCode, ParentId) VALUES (@Id, @Name, @ColorCode, @ParentId)"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {|
                    Id = tag.Id
                    Name = tag.Name
                    ColorCode = tag.ColorCode
                    ParentId = Option.toObj tag.ParentId
                |}))
                return Ok ()
            with
            | :? SqliteException as ex when ex.SqliteErrorCode = 19 ->
                return Error (UniqueConstraintViolation ex.Message)
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画へのタグ紐付け保存
    let addTagToVideo (conn: IDbConnection) (videoId: string) (tagId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "INSERT OR IGNORE INTO VideoTags (VideoId, TagId) VALUES (@VideoId, @TagId)"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| VideoId = videoId; TagId = tagId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画からタグの紐付けを解除
    let removeTagFromVideo (conn: IDbConnection) (videoId: string) (tagId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "DELETE FROM VideoTags WHERE VideoId = @VideoId AND TagId = @TagId"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| VideoId = videoId; TagId = tagId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// タグ階層の更新 (親タグの変更による整理)
    let updateTagParent (conn: IDbConnection) (tagId: string) (parentId: string option) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE Tags SET ParentId = @ParentId WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = tagId; ParentId = Option.toObj parentId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// タグの削除 (自己参照であるため、子タグ of ParentId は SET NULL される)
    let deleteTag (conn: IDbConnection) (tagId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "DELETE FROM Tags WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = tagId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画メタデータの保存
    let saveVideo (conn: IDbConnection) (video: Video) : Async<Result<int, DbError>> =
        async {
            try
                let sql = """
                    INSERT OR IGNORE INTO Videos (Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, AccessCount, LastAccessedAt)
                    VALUES (@Id, @FileName, @FilePath, @Duration, @FileSize, @ThumbnailPath, @IsFavorite, @CreatedAt, @AccessCount, @LastAccessedAt)
                """
                let task = conn.ExecuteAsync(sql, {|
                    Id = video.Id
                    FileName = video.FileName
                    FilePath = video.FilePath
                    Duration = video.Duration
                    FileSize = video.FileSize
                    ThumbnailPath = Option.toObj video.ThumbnailPath
                    IsFavorite = if video.IsFavorite then 1 else 0
                    CreatedAt = video.CreatedAt.ToString("o")
                    AccessCount = video.AccessCount
                    LastAccessedAt = match video.LastAccessedAt with Some d -> d.ToString("o") | None -> null
                |})
                let! rows = Async.AwaitTask task
                return Ok rows
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画の再生時間およびサムネイルパスを更新する
    let updateVideoMetadata (conn: IDbConnection) (videoId: string) (duration: int64) (thumbnailPath: string option) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE Videos SET Duration = @Duration, ThumbnailPath = @ThumbnailPath WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = videoId; Duration = duration; ThumbnailPath = Option.toObj thumbnailPath |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 自動タグ付けルール一覧の取得
    let getTaggingRules (conn: IDbConnection) : Async<Result<TaggingRule list, DbError>> =
        async {
            try
                let sql = "SELECT Id, Pattern, TagId, MatchType, TargetField, MinSize, MaxSize, CreatedAt FROM TaggingRules"
                let! dtos = Async.AwaitTask (conn.QueryAsync<TaggingRuleDbDto>(sql))
                let rules : TaggingRule list = 
                    dtos
                    |> Seq.map (fun dto -> {
                        TaggingRule.Id = dto.Id
                        Pattern = dto.Pattern
                        TagId = dto.TagId
                        MatchType = if String.IsNullOrEmpty(dto.MatchType) then "partial" else dto.MatchType
                        TargetField = if String.IsNullOrEmpty(dto.TargetField) then "fileName" else dto.TargetField
                        MinSize = if dto.MinSize.HasValue then Some dto.MinSize.Value else None
                        MaxSize = if dto.MaxSize.HasValue then Some dto.MaxSize.Value else None
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                    })
                    |> Seq.toList
                return Ok rules
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 自動タグ付けルールの保存
    let saveTaggingRule (conn: IDbConnection) (rule: TaggingRule) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "INSERT INTO TaggingRules (Id, Pattern, TagId, MatchType, TargetField, MinSize, MaxSize, CreatedAt) VALUES (@Id, @Pattern, @TagId, @MatchType, @TargetField, @MinSize, @MaxSize, @CreatedAt)"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {|
                    Id = rule.Id
                    Pattern = rule.Pattern
                    TagId = rule.TagId
                    MatchType = rule.MatchType
                    TargetField = rule.TargetField
                    MinSize = match rule.MinSize with Some s -> Nullable<int64>(s) | None -> Nullable<int64>()
                    MaxSize = match rule.MaxSize with Some s -> Nullable<int64>(s) | None -> Nullable<int64>()
                    CreatedAt = rule.CreatedAt.ToString("o")
                |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 自動タグ付けルールの削除
    let deleteTaggingRule (conn: IDbConnection) (ruleId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "DELETE FROM TaggingRules WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = ruleId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 自動タグ付けルールの更新
    let updateTaggingRule (conn: IDbConnection) (ruleId: string) (pattern: string) (tagId: string) (matchType: string) (targetField: string) (minSize: int64 option) (maxSize: int64 option) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE TaggingRules SET Pattern = @Pattern, TagId = @TagId, MatchType = @MatchType, TargetField = @TargetField, MinSize = @MinSize, MaxSize = @MaxSize WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| 
                    Id = ruleId
                    Pattern = pattern
                    TagId = tagId
                    MatchType = matchType
                    TargetField = targetField
                    MinSize = match minSize with Some s -> Nullable<int64>(s) | None -> Nullable<int64>()
                    MaxSize = match maxSize with Some s -> Nullable<int64>(s) | None -> Nullable<int64>()
                |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画のお気に入りステータスの更新
    let updateVideoFavorite (conn: IDbConnection) (videoId: string) (isFavorite: bool) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE Videos SET IsFavorite = @IsFavorite WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = videoId; IsFavorite = if isFavorite then 1 else 0 |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// スマートフォルダ一覧の取得
    let getSmartFolders (conn: IDbConnection) : Async<Result<SmartFolder list, DbError>> =
        async {
            try
                let sql = "SELECT Id, Name, Query, CreatedAt FROM SmartFolders ORDER BY CreatedAt DESC"
                let! dtos = Async.AwaitTask (conn.QueryAsync<SmartFolderDbDto>(sql))
                let folders = 
                    dtos
                    |> Seq.map (fun dto -> {
                        SmartFolder.Id = dto.Id
                        Name = dto.Name
                        Query = dto.Query
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                    })
                    |> Seq.toList
                return Ok folders
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// スマートフォルダの保存
    let saveSmartFolder (conn: IDbConnection) (folder: SmartFolder) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "INSERT OR REPLACE INTO SmartFolders (Id, Name, Query, CreatedAt) VALUES (@Id, @Name, @Query, @CreatedAt)"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = folder.Id; Name = folder.Name; Query = folder.Query; CreatedAt = folder.CreatedAt.ToString("o") |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// スマートフォルダの削除
    let deleteSmartFolder (conn: IDbConnection) (id: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "DELETE FROM SmartFolders WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = id |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画の再生回数をインクリメントし最終アクセス日時を記録
    let recordVideoAccess (conn: IDbConnection) (videoId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE Videos SET AccessCount = AccessCount + 1, LastAccessedAt = @LastAccessedAt WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = videoId; LastAccessedAt = DateTime.UtcNow.ToString("o") |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 動画の再生回数をクリア（0にリセット）
    let clearVideoAccess (conn: IDbConnection) (videoId: string) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "UPDATE Videos SET AccessCount = 0, LastAccessedAt = NULL WHERE Id = @Id"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {| Id = videoId |}))
                return Ok ()
            with
            | ex -> return Error (QueryError ex.Message)
        }

    /// 例外ログの保存
    let saveErrorLog (conn: IDbConnection) (message: string) (stackTrace: string option) : Async<unit> =
        async {
            try
                let sql = "INSERT INTO ErrorLogs (Message, StackTrace, CreatedAt) VALUES (@Message, @StackTrace, @CreatedAt)"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql, {|
                    Message = message
                    StackTrace = Option.toObj stackTrace
                    CreatedAt = DateTime.UtcNow.ToString("o")
                |}))
                ()
            with _ -> ()
        }

    /// 例外ログの全削除
    let clearErrorLogs (conn: IDbConnection) : Async<Result<unit, DbError>> =
        async {
            try
                let sql = "DELETE FROM ErrorLogs"
                let! _ = Async.AwaitTask (conn.ExecuteAsync(sql))
                return Ok ()
            with ex ->
                return Error (QueryError ex.Message)
        }

    /// 例外ログの取得（最大100件）
    let getErrorLogs (conn: IDbConnection) : Async<Result<ErrorLog list, DbError>> =
        async {
            try
                let sql = "SELECT Id, Message, StackTrace, CreatedAt FROM ErrorLogs ORDER BY CreatedAt DESC LIMIT 100"
                let! (rows: seq<ErrorLogDbDto>) = Async.AwaitTask (conn.QueryAsync<ErrorLogDbDto>(sql))
                let logs =
                    rows
                    |> Seq.map (fun r -> {
                        ErrorLog.Id = r.Id
                        Message = r.Message
                        StackTrace = Option.ofObj r.StackTrace
                        CreatedAt = DateTime.Parse(r.CreatedAt)
                    })
                    |> Seq.toList
                return Ok logs
            with ex ->
                return Error (QueryError ex.Message)
        }




    /// 再生回数ランキング上位100件を取得
    let getVideoRanking (conn: IDbConnection) : Async<Result<Video list, DbError>> =
        async {
            try
                let sql = "SELECT Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, AccessCount, LastAccessedAt FROM Videos WHERE AccessCount > 0 ORDER BY AccessCount DESC, LastAccessedAt DESC LIMIT 100"
                let! dtos = Async.AwaitTask (conn.QueryAsync<VideoDbDto>(sql))
                let videos = 
                    dtos
                    |> Seq.map (fun dto -> {
                        Id = dto.Id
                        FileName = dto.FileName
                        FilePath = dto.FilePath
                        Duration = dto.Duration
                        FileSize = dto.FileSize
                        ThumbnailPath = Option.ofObj dto.ThumbnailPath
                        IsFavorite = dto.IsFavorite <> 0
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                        AccessCount = dto.AccessCount
                        LastAccessedAt = if String.IsNullOrEmpty(dto.LastAccessedAt) then None else Some (DateTime.Parse(dto.LastAccessedAt))
                        Tags = []
                    })
                    |> Seq.toList

                let! videosWithTags = 
                    videos 
                    |> List.map (fun v -> async {
                        let tagSql = "SELECT t.Id, t.Name, t.ColorCode, t.ParentId FROM Tags t INNER JOIN VideoTags vt ON t.Id = vt.TagId WHERE vt.VideoId = @VideoId"
                        let! tagDtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(tagSql, {| VideoId = v.Id |}))
                        let tags = tagDtos |> Seq.map (fun td -> { Tag.Id = td.Id; Name = td.Name; ColorCode = td.ColorCode; ParentId = Option.ofObj td.ParentId; VideoCount = td.VideoCount }) |> Seq.toList
                        return { v with Tags = tags }
                    })
                    |> Async.Sequential
                return Ok (Array.toList videosWithTags)
            with
            | ex -> return Error (QueryError ex.Message)
        }
    type CharType =
        | Kanji
        | Katakana
        | Hiragana
        | AlphaNum
        | Other

    let getCharType (c: char) =
        if c >= '\u4e00' && c <= '\u9faf' then Kanji
        elif c >= '\u30a0' && c <= '\u30ff' then Katakana
        elif c >= '\u3040' && c <= '\u309f' then Hiragana
        elif System.Char.IsLetterOrDigit(c) then AlphaNum
        else Other

    let private stopWords = 
        set [
            "する"; "した"; "から"; "での"; "への"; "ある"; "ない"; "れる"; "られ"; "こと"; "もの"; "とき"; 
            "あり"; "の"; "に"; "で"; "を"; "が"; "は"; "と"; "て"; "た"; "へ"; "も"; "や"; "か"; "な"; "ね";
            "し"; "れる"; "られ"; "せる"; "させ"; "いる"; "いた"; "お"; "ご"; "にて"; "など"; "まで"; "より"
        ]

    /// 日本語ファイル名から、タグ辞書と文字種境界分割を組み合わせたハイブリッド方式でキーワードを抽出する
    let extractKeywordsFromFileName (tags: Tag list) (fileName: string) : string list =
        let nameWithoutExt = IO.Path.GetFileNameWithoutExtension(fileName)
        
        let mutable remainingText = nameWithoutExt
        let mutable extractedFromTags = []
        
        let sortedTags = tags |> List.sortByDescending (fun t -> t.Name.Length)
        for tag in sortedTags do
            if tag.Name.Length >= 2 then
                let tagLower = tag.Name.ToLower()
                if remainingText.ToLower().Contains(tagLower) then
                    extractedFromTags <- tag.Name :: extractedFromTags
                    let idx = remainingText.ToLower().IndexOf(tagLower)
                    if idx >= 0 then
                        remainingText <- remainingText.Remove(idx, tag.Name.Length).Insert(idx, " ")
        
        let mutable tokens = []
        if not (String.IsNullOrEmpty(remainingText)) then
            let chars = remainingText.ToCharArray()
            let mutable currentToken = System.Text.StringBuilder()
            let mutable currentType = getCharType chars.[0]
            currentToken.Append(chars.[0]) |> ignore
            
            for i in 1 .. chars.Length - 1 do
                let t = getCharType chars.[i]
                if t = currentType then
                    currentToken.Append(chars.[i]) |> ignore
                else
                    tokens <- (currentToken.ToString(), currentType) :: tokens
                    currentToken <- System.Text.StringBuilder()
                    currentToken.Append(chars.[i]) |> ignore
                    currentType <- t
            tokens <- (currentToken.ToString(), currentType) :: tokens

        let extractedFromText =
            tokens
            |> List.filter (fun (tok, t) ->
                let cleanTok = tok.Trim()
                if cleanTok.Length < 2 then false
                else
                    match t with
                    | Kanji | Katakana -> cleanTok.Length >= 2 && cleanTok.Length <= 10
                    | Hiragana -> false
                    | AlphaNum -> 
                        cleanTok.Length >= 3 && cleanTok.Length <= 12 && 
                        not (cleanTok.ToLower() = "mp4" || cleanTok.ToLower() = "avi" || cleanTok.ToLower() = "mkv" || cleanTok.ToLower() = "wmv")
                    | _ -> false
            )
            |> List.map (fun (tok, _) -> tok.Trim())
        
        List.append extractedFromTags extractedFromText
        |> List.distinct

    /// 嗜好キーワードとおすすめ動画20件を取得
    let getVideoRecommendations (conn: IDbConnection) : Async<Result<string list * Video list, DbError>> =
        async {
            try
                let! tagsResult = getTags conn
                let tags = match tagsResult with Ok t -> t | Error _ -> []

                let sql = "SELECT Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, AccessCount, LastAccessedAt FROM Videos"
                let! dtos = Async.AwaitTask (conn.QueryAsync<VideoDbDto>(sql))
                let allVideos = 
                    dtos
                    |> Seq.map (fun dto -> {
                        Id = dto.Id
                        FileName = dto.FileName
                        FilePath = dto.FilePath
                        Duration = dto.Duration
                        FileSize = dto.FileSize
                        ThumbnailPath = Option.ofObj dto.ThumbnailPath
                        IsFavorite = dto.IsFavorite <> 0
                        CreatedAt = DateTime.Parse(dto.CreatedAt)
                        AccessCount = dto.AccessCount
                        LastAccessedAt = if String.IsNullOrEmpty(dto.LastAccessedAt) then None else Some (DateTime.Parse(dto.LastAccessedAt))
                        Tags = []
                    })
                    |> Seq.toList

                let playedVideos = allVideos |> List.filter (fun v -> v.AccessCount > 0)
                
                let keywords =
                    playedVideos
                    |> List.collect (fun v ->
                        extractKeywordsFromFileName tags v.FileName
                        |> List.map (fun tok -> tok.ToLower())
                    )
                    |> List.groupBy id
                    |> List.map (fun (word, instances) -> (word, List.length instances))
                    |> List.sortByDescending snd
                    |> List.map fst
                    |> List.truncate 5

                let recommendations =
                    if List.isEmpty keywords then
                        []
                    else
                        allVideos
                        |> List.filter (fun v ->
                            let nameLower = v.FileName.ToLower()
                            keywords |> List.exists (fun kw -> nameLower.Contains(kw))
                        )
                        |> List.sortBy (fun v -> (v.AccessCount, -v.CreatedAt.Ticks))
                        |> List.truncate 20

                let! recommendationsWithTags = 
                    recommendations 
                    |> List.map (fun v -> async {
                        let tagSql = "SELECT t.Id, t.Name, t.ColorCode, t.ParentId FROM Tags t INNER JOIN VideoTags vt ON t.Id = vt.TagId WHERE vt.VideoId = @VideoId"
                        let! tagDtos = Async.AwaitTask (conn.QueryAsync<TagDbDto>(tagSql, {| VideoId = v.Id |}))
                        let tags = tagDtos |> Seq.map (fun td -> { Tag.Id = td.Id; Name = td.Name; ColorCode = td.ColorCode; ParentId = Option.ofObj td.ParentId; VideoCount = td.VideoCount }) |> Seq.toList
                        return { v with Tags = tags }
                    })
                    |> Async.Sequential

                return Ok (keywords, Array.toList recommendationsWithTags)
            with
            | ex -> return Error (QueryError ex.Message)
        }

module DbInit =
    
    /// テーブル作成用 DDL
    let private ddlQueries = [
        """
        CREATE TABLE IF NOT EXISTS Videos (
            Id TEXT PRIMARY KEY,
            FileName TEXT NOT NULL,
            FilePath TEXT NOT NULL UNIQUE,
            Duration INTEGER NOT NULL,
            FileSize INTEGER NOT NULL,
            ThumbnailPath TEXT,
            IsFavorite INTEGER DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            AccessCount INTEGER DEFAULT 0,
            LastAccessedAt TEXT
        );
        """
        """
        CREATE TABLE IF NOT EXISTS Tags (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE,
            ColorCode TEXT DEFAULT '#71717a',
            ParentId TEXT,
            FOREIGN KEY (ParentId) REFERENCES Tags(Id) ON DELETE SET NULL
        );
        """
        """
        CREATE TABLE IF NOT EXISTS VideoTags (
            VideoId TEXT NOT NULL,
            TagId TEXT NOT NULL,
            PRIMARY KEY (VideoId, TagId),
            FOREIGN KEY (VideoId) REFERENCES Videos(Id) ON DELETE CASCADE,
            FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
        );
        """
        """
        CREATE TABLE IF NOT EXISTS TaggingRules (
            Id TEXT PRIMARY KEY,
            Pattern TEXT NOT NULL,
            TagId TEXT NOT NULL,
            MatchType TEXT NOT NULL DEFAULT 'partial',
            TargetField TEXT NOT NULL DEFAULT 'fileName',
            MinSize INTEGER,
            MaxSize INTEGER,
            CreatedAt TEXT NOT NULL,
            FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
        );
        """
        """
        CREATE TABLE IF NOT EXISTS SmartFolders (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            Query TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );
        """
        """
        CREATE TABLE IF NOT EXISTS ErrorLogs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Message TEXT NOT NULL,
            StackTrace TEXT,
            CreatedAt TEXT NOT NULL
        );
        """
    ]

    /// データベースの初期化マイグレーションを実行する
    let initializeDatabase (conn: IDbConnection) : Result<unit, string> =
        try
            // WALモードの有効化および外部キー制約の有効化
            conn.Execute("PRAGMA journal_mode = WAL;") |> ignore
            conn.Execute("PRAGMA foreign_keys = ON;") |> ignore
            
            // 各DDLの実行
            for ddl in ddlQueries do
                conn.Execute(ddl) |> ignore

            // 例外ログのクリア (起動時クリア)
            conn.Execute("DELETE FROM ErrorLogs;") |> ignore
            
            // TaggingRulesテーブルへのカラム追加マイグレーション
            let columnInfosRules = conn.Query("PRAGMA table_info(TaggingRules)")
            let existingRulesColNames =
                columnInfosRules
                |> Seq.map (fun row -> (row :?> System.Collections.Generic.IDictionary<string, obj>).["name"] |> string)
                |> Set.ofSeq

            let alterRulesIfMissing colName colDef =
                if not (Set.contains colName existingRulesColNames) then
                    conn.Execute($"ALTER TABLE TaggingRules ADD COLUMN {colDef}") |> ignore

            alterRulesIfMissing "MatchType"   "MatchType TEXT NOT NULL DEFAULT 'partial'"
            alterRulesIfMissing "TargetField" "TargetField TEXT NOT NULL DEFAULT 'fileName'"
            alterRulesIfMissing "MinSize"     "MinSize INTEGER"
            alterRulesIfMissing "MaxSize"     "MaxSize INTEGER"
            
            // Videosテーブルへのカラム追加マイグレーション
            let columnInfosVideos = conn.Query("PRAGMA table_info(Videos)")
            let existingVideosColNames =
                columnInfosVideos
                |> Seq.map (fun row -> (row :?> System.Collections.Generic.IDictionary<string, obj>).["name"] |> string)
                |> Set.ofSeq

            let alterVideosIfMissing colName colDef =
                if not (Set.contains colName existingVideosColNames) then
                    conn.Execute($"ALTER TABLE Videos ADD COLUMN {colDef}") |> ignore

            alterVideosIfMissing "AccessCount"    "AccessCount INTEGER DEFAULT 0"
            alterVideosIfMissing "LastAccessedAt" "LastAccessedAt TEXT"

            Ok ()
        with
        | ex -> Error ex.Message

