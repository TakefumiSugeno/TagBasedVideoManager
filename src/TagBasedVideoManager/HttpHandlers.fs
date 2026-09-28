namespace TagBasedVideoManager

open System
open System.IO
open System.Data
open Microsoft.Data.Sqlite
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Mvc.Infrastructure
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Giraffe
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Application
open TagBasedVideoManager.Infrastructure


/// APIリクエスト用DTO (標準 of System.Text.Jsonでバインドするため、F# option型は避け、null許容stringを使用)
[<CLIMutable>]
type CreateTagRequest = {
    Name: string
    ColorCode: string
    ParentId: string
}

[<CLIMutable>]
type UpdateTagParentRequest = {
    ParentId: string
}

/// 新規ルール作成用DTO
[<CLIMutable>]
type CreateRuleRequest = {
    Pattern: string
    TagId: string
    MatchType: string
    TargetField: string
    MinSize: System.Nullable<int64>
    MaxSize: System.Nullable<int64>
}

[<CLIMutable>]
type VideoTagRequest = {
    TagId: string
}

module HttpHandlers =
    
    /// 動画ストリーミングハンドラー
    let streamVideo (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! videoResult = Db.getVideo conn id
                
                let filePath = 
                    match videoResult with
                    | Ok v ->
                        let resolved = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath v.FilePath
                        if File.Exists(resolved) then resolved
                        else v.FilePath
                    | _ ->
                        let testDummyPath = Path.Combine(System.AppContext.BaseDirectory, "stream_test.mp4")
                        let resolvedDummy = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath testDummyPath
                        if File.Exists(resolvedDummy) then resolvedDummy
                        else $"/app/data/videos/{id}.mp4"

                let resolvedFinalPath = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath filePath
                let result = PhysicalFileResult(resolvedFinalPath, "video/mp4")
                result.EnableRangeProcessing <- true

                let executor = ctx.RequestServices.GetRequiredService<IActionResultExecutor<PhysicalFileResult>>()
                let actionContext = ActionContext(ctx, ctx.GetRouteData(), Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())
                
                do! executor.ExecuteAsync(actionContext, result)
                return! next ctx
            }

    /// 再生回数を記録するAPIハンドラー
    let recordVideoAccess (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                try
                    let! result = Db.recordVideoAccess conn id
                    match result with
                    | Ok () -> return! json {| status = "recorded"; videoId = id |} next ctx
                    | Error err -> 
                        do! Db.saveErrorLog conn (sprintf "%A" err) None |> Async.StartAsTask :> Task
                        return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    do! Db.saveErrorLog conn ex.Message (Some (ex.ToString())) |> Async.StartAsTask :> Task
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 再生回数をクリアするAPIハンドラー
    let clearVideoAccess (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                try
                    let! result = Db.clearVideoAccess conn id
                    match result with
                    | Ok () -> return! json {| status = "cleared"; videoId = id |} next ctx
                    | Error err -> 
                        do! Db.saveErrorLog conn (sprintf "%A" err) None |> Async.StartAsTask :> Task
                        return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    do! Db.saveErrorLog conn ex.Message (Some (ex.ToString())) |> Async.StartAsTask :> Task
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 未処理例外を捕捉してDBログ出力するGiraffe例外ハンドラー
    let giraffeErrorHandler (ex : Exception) (logger : ILogger) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    do! Db.saveErrorLog conn ex.Message (Some (ex.ToString())) |> Async.StartAsTask :> Task
                with _ -> ()
                logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
                return! (clearResponse >=> setStatusCode 500 >=> json {| error = ex.Message |}) next ctx
            }


    // --- 動画スキャンハンドラー ---

    /// 手動スキャン実行ハンドラー (ビデオディレクトリの同期)
    let triggerScan : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let videoDir = 
                    let env = Environment.GetEnvironmentVariable("VIDEO_DIR")
                    if String.IsNullOrWhiteSpace(env) then "videos" else env
                let thumbDir = 
                    let env = Environment.GetEnvironmentVariable("THUMBNAIL_DIR")
                    if String.IsNullOrWhiteSpace(env) then "thumbnails" else env
                
                let mediaProc = ctx.GetService<IMediaProcessor>()
                let! result = Scanner.syncVideoDirectory videoDir thumbDir mediaProc conn
                match result with
                | Ok count -> return! json {| addedCount = count |} next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    // --- 動画一覧・検索用ハンドラー ---
    
    /// 動画一覧および検索結果を取得する (qパラメータ指定時はDSLパースによる階層タグ考慮検索)
    let getVideos : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                
                let qOpt = 
                    match ctx.Request.Query.TryGetValue("q") with
                    | true, values when values.Count > 0 && not (String.IsNullOrWhiteSpace(values.[0])) -> 
                        Some (values.[0])
                    | _ -> None

                let! result = 
                    match qOpt with
                    | Some q -> 
                        let tokens = SearchDsl.parse q
                        Db.searchVideos conn tokens
                    | None -> 
                        Db.getVideos conn None

                match result with
                | Ok videos ->
                    let dtos = videos |> List.map (fun v -> {|
                        id = v.Id
                        fileName = v.FileName
                        filePath = v.FilePath
                        duration = v.Duration
                        fileSize = v.FileSize
                        thumbnailPath = Option.toObj v.ThumbnailPath
                        isFavorite = v.IsFavorite
                        createdAt = v.CreatedAt.ToString("o")
                        accessCount = v.AccessCount
                        lastAccessedAt = match v.LastAccessedAt with Some dt -> dt.ToString("o") | None -> null
                        tags = v.Tags |> List.map (fun t -> {|
                            id = t.Id
                            name = t.Name
                            colorCode = t.ColorCode
                            parentId = Option.toObj t.ParentId
                        |})
                    |})

                    return! json dtos next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    // --- 動画タグ手動紐付けハンドラー ---

    /// 動画に手動でタグを紐付ける
    let addVideoTag (videoId: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! req = ctx.BindJsonAsync<VideoTagRequest>()
                let! result = Db.addTagToVideo conn videoId req.TagId
                match result with
                | Ok () -> return! json {| videoId = videoId; tagId = req.TagId |} next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 動画から手動でタグを紐付け解除する
    let removeVideoTag (videoId: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! req = ctx.BindJsonAsync<VideoTagRequest>()
                let! result = Db.removeTagFromVideo conn videoId req.TagId
                match result with
                | Ok () -> return! json {| videoId = videoId; tagId = req.TagId |} next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    // --- タグCRUD用ハンドラー ---
    
    /// 全タグ一覧を取得する
    let getTags : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! result = Db.getTags conn
                match result with
                | Ok tags -> 
                    let dtos = tags |> List.map (fun t -> {|
                        id = t.Id
                        name = t.Name
                        colorCode = t.ColorCode
                        parentId = Option.toObj t.ParentId
                        videoCount = t.VideoCount
                    |})
                    return! json dtos next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 新規タグを作成する
    let createTag : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! req = ctx.BindJsonAsync<CreateTagRequest>()
                let guidStr = Guid.NewGuid().ToString("N")
                let tagId = $"t_{guidStr.Substring(0, 8)}"
                let newTag = {
                    Id = tagId
                    Name = req.Name
                    ColorCode = req.ColorCode
                    ParentId = Option.ofObj req.ParentId
                    VideoCount = 0
                }
                let! result = Db.saveTag conn newTag
                match result with
                | Ok () -> 
                    let responseDto = {|
                        id = newTag.Id
                        name = newTag.Name
                        colorCode = newTag.ColorCode
                        parentId = Option.toObj newTag.ParentId
                    |}
                    return! json responseDto next ctx
                | Error (UniqueConstraintViolation msg) -> return! RequestErrors.BAD_REQUEST msg next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// タグの親子関係(階層)を更新する
    let updateTagParent (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! req = ctx.BindJsonAsync<UpdateTagParentRequest>()
                let! result = Db.updateTagParent conn id (Option.ofObj req.ParentId)
                match result with
                | Ok () -> 
                    return! json {| id = id; parentId = req.ParentId |} next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// タグを削除する
    let deleteTag (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! result = Db.deleteTag conn id
                match result with
                | Ok () -> return! (setStatusCode 204) next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    // --- 自動タグ付けルール用ハンドラー ---
    
    /// 全自動ルール一覧を取得する
    let getRules : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! result = Db.getTaggingRules conn
                match result with
                | Ok rules ->
                    let dtos = rules |> List.map (fun r -> {|
                        id = r.Id
                        pattern = r.Pattern
                        tagId = r.TagId
                        matchType = r.MatchType
                        targetField = r.TargetField
                        minSize = match r.MinSize with Some s -> Nullable(s) | None -> Nullable()
                        maxSize = match r.MaxSize with Some s -> Nullable(s) | None -> Nullable()
                        createdAt = r.CreatedAt.ToString("o")
                    |})
                    return! json dtos next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 新規自動ルールを作成する (既存動画への遡及適用を非同期で実行)
    let createRule : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! req = ctx.BindJsonAsync<CreateRuleRequest>()
                let guidStr = Guid.NewGuid().ToString("N")
                let ruleId = $"r_{guidStr.Substring(0, 8)}"
                let newRule = {
                    Id = ruleId
                    Pattern = req.Pattern
                    TagId = req.TagId
                    MatchType = if String.IsNullOrWhiteSpace(req.MatchType) then "partial" else req.MatchType
                    TargetField = if String.IsNullOrWhiteSpace(req.TargetField) then "fileName" else req.TargetField
                    MinSize = if req.MinSize.HasValue then Some req.MinSize.Value else None
                    MaxSize = if req.MaxSize.HasValue then Some req.MaxSize.Value else None
                    CreatedAt = DateTime.UtcNow
                }
                let! result = Db.saveTaggingRule conn newRule
                match result with
                | Ok () ->
                    let dbPath = 
                        let builder = SqliteConnectionStringBuilder(conn.ConnectionString)
                        builder.DataSource
                    Async.Start(async {
                        do! Async.Sleep 200
                        use bgConn = Db.getConnection dbPath
                        let! _ = Scanner.applyRuleToExistingVideos bgConn newRule
                        ()
                    })
                    
                    let responseDto = {|
                        id = newRule.Id
                        pattern = newRule.Pattern
                        tagId = newRule.TagId
                        matchType = newRule.MatchType
                        targetField = newRule.TargetField
                        minSize = match newRule.MinSize with Some s -> Nullable(s) | None -> Nullable()
                        maxSize = match newRule.MaxSize with Some s -> Nullable(s) | None -> Nullable()
                        createdAt = newRule.CreatedAt.ToString("o")
                    |}
                    return! json responseDto next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 自動ルールを削除する
    let deleteRule (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! result = Db.deleteTaggingRule conn id
                match result with
                | Ok () -> return! (setStatusCode 204) next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 自動ルールのパラメータ更新用DTO
    [<CLIMutable>]
    type UpdateRuleDto = {
        pattern : string
        tagId : string
        matchType : string
        targetField : string
        minSize : System.Nullable<int64>
        maxSize : System.Nullable<int64>
    }

    /// 自動ルールを更新する
    let updateRule (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! dto = ctx.BindJsonAsync<UpdateRuleDto>()
                let matchType = if String.IsNullOrWhiteSpace(dto.matchType) then "partial" else dto.matchType
                let targetField = if String.IsNullOrWhiteSpace(dto.targetField) then "fileName" else dto.targetField
                let minSize = if dto.minSize.HasValue then Some dto.minSize.Value else None
                let maxSize = if dto.maxSize.HasValue then Some dto.maxSize.Value else None
                let! result = Db.updateTaggingRule conn id dto.pattern dto.tagId matchType targetField minSize maxSize
                match result with
                | Ok () ->
                    // 更新成功後、遡及適用をバックグラウンドで走らせる
                    let dbPath = 
                        let connectionString = conn.ConnectionString
                        let builder = Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString)
                        builder.DataSource
                    Async.Start(async {
                        do! Async.Sleep 200
                        use bgConn = Db.getConnection dbPath
                        let updatedRule = {
                            Id = id
                            Pattern = dto.pattern
                            TagId = dto.tagId
                            MatchType = matchType
                            TargetField = targetField
                            MinSize = minSize
                            MaxSize = maxSize
                            CreatedAt = DateTime.UtcNow
                        }
                        let! _ = Scanner.applyRuleToExistingVideos bgConn updatedRule
                        ()
                    })
                    return! (setStatusCode 204) next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    // --- お気に入り・一括操作用ハンドラー ---

    /// 動画のお気に入りステータス更新用DTO
    [<CLIMutable>]
    type FavoriteUpdateDto = {
        isFavorite : bool
    }

    /// 動画のお気に入りステータスを更新する
    let updateFavorite (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! dto = ctx.BindJsonAsync<FavoriteUpdateDto>()
                let! result = Db.updateVideoFavorite conn id dto.isFavorite
                match result with
                | Ok () -> return! json {| id = id; isFavorite = dto.isFavorite |} next ctx
                | Error err -> return! ServerErrors.INTERNAL_ERROR (string err) next ctx
            }

    /// 一括タグ操作用DTO
    [<CLIMutable>]
    type BatchTagDto = {
        videoIds : string[]
        tagId : string
    }

    /// 一括お気に入り操作用DTO
    [<CLIMutable>]
    type BatchFavoriteDto = {
        videoIds : string[]
        isFavorite : bool
    }

    [<CLIMutable>]
    type SaveSmartFolderDto = {
        id: string option
        name: string
        query: string
    }

    [<CLIMutable>]
    type ImportTagDto = {
        id: string
        name: string
        colorCode: string
        parentId: string
    }

    [<CLIMutable>]
    type ImportRuleDto = {
        id: string
        pattern: string
        tagId: string
        matchType: string
        targetField: string
        minSize: Nullable<int64>
        maxSize: Nullable<int64>
    }

    [<CLIMutable>]
    type ImportSmartFolderDto = {
        id: string
        name: string
        query: string
    }

    /// 複数動画への一括タグ追加
    let batchAddTag : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! dto = ctx.BindJsonAsync<BatchTagDto>()
                    if box dto = null then
                        return! RequestErrors.BAD_REQUEST "DTO is null" next ctx
                    elif box dto.videoIds = null then
                        return! RequestErrors.BAD_REQUEST "videoIds is null" next ctx
                    else
                        let mutable count = 0
                        for videoId in dto.videoIds do
                            let! _ = Db.addTagToVideo conn videoId dto.tagId
                            count <- count + 1
                        return! json {| count = count |} next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 複数動画からの一括タグ削除
    let batchRemoveTag : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! dto = ctx.BindJsonAsync<BatchTagDto>()
                    if box dto = null then
                        return! RequestErrors.BAD_REQUEST "DTO is null" next ctx
                    elif box dto.videoIds = null then
                        return! RequestErrors.BAD_REQUEST "videoIds is null" next ctx
                    else
                        let mutable count = 0
                        for videoId in dto.videoIds do
                            let! _ = Db.removeTagFromVideo conn videoId dto.tagId
                            count <- count + 1
                        return! json {| count = count |} next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 複数動画のお気に入り状態を一括更新
    let batchUpdateFavorite : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let conn = ctx.GetService<IDbConnection>()
                let! dto = ctx.BindJsonAsync<BatchFavoriteDto>()
                let mutable count = 0
                for videoId in dto.videoIds do
                    let! _ = Db.updateVideoFavorite conn videoId dto.isFavorite
                    count <- count + 1
                return! json {| count = count |} next ctx
            }

    /// サムネイル・メタデータ生成処理をストップさせる
    let stopMediaProcessing : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let mediaProc = ctx.GetService<IMediaProcessor>()
                    MediaQueue.stopProcessing mediaProc
                    return! json {| status = "stopped" |} next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// スマートフォルダ一覧を取得
    let getSmartFolders : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! result = Db.getSmartFolders conn
                    match result with
                    | Ok folders -> return! json folders next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// スマートフォルダを保存
    let saveSmartFolder : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! dto = ctx.BindJsonAsync<SaveSmartFolderDto>()
                    if box dto = null then
                        return! RequestErrors.BAD_REQUEST "DTO is null" next ctx
                    elif String.IsNullOrEmpty(dto.name) then
                        return! RequestErrors.BAD_REQUEST "name is required" next ctx
                    else
                        let id = 
                            match dto.id with
                            | Some existingId when not (String.IsNullOrEmpty(existingId)) -> existingId
                            | _ -> "sf_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                        let folder = {
                            Id = id
                            Name = dto.name
                            Query = dto.query
                            CreatedAt = DateTime.UtcNow
                        }
                        let! result = Db.saveSmartFolder conn folder
                        match result with
                        | Ok () -> return! json folder next ctx
                        | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// スマートフォルダを削除
    let deleteSmartFolder (id: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! result = Db.deleteSmartFolder conn id
                    match result with
                    | Ok () -> return! json {| status = "deleted" |} next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// タグの一括インポート
    let importTags : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let contentType = ctx.Request.ContentType
                    let! tags = 
                        task {
                            if not (String.IsNullOrEmpty(contentType)) && contentType.StartsWith("text/plain") then
                                // TSV 形式のパース
                                let! body = ctx.ReadBodyFromRequestAsync()
                                let lines = body.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
                                let list = 
                                    lines 
                                    |> Array.skip 1 // ヘッダー行をスキップ
                                    |> Array.map (fun line -> line.Split('\t'))
                                    |> Array.filter (fun cols -> cols.Length >= 2)
                                    |> Array.map (fun cols ->
                                        let rawId = cols.[0].Trim()
                                        let id = if String.IsNullOrEmpty(rawId) then "t_" + Guid.NewGuid().ToString("N").Substring(0, 8) else rawId
                                        let name = cols.[1].Trim()
                                        let color = if cols.Length > 2 && not (String.IsNullOrEmpty(cols.[2])) then cols.[2].Trim() else "#71717a"
                                        let parentId = if cols.Length > 3 && not (String.IsNullOrEmpty(cols.[3])) then Some (cols.[3].Trim()) else None
                                        { Id = id; Name = name; ColorCode = color; ParentId = parentId; VideoCount = 0 }
                                    )
                                    |> Array.toList
                                return list
                            else
                                // JSON 形式のパース
                                let! dtos = ctx.BindJsonAsync<ImportTagDto list>()
                                let list = 
                                    dtos 
                                    |> List.map (fun dto ->
                                        let id = if String.IsNullOrEmpty(dto.id) then "t_" + Guid.NewGuid().ToString("N").Substring(0, 8) else dto.id
                                        let color = if String.IsNullOrEmpty(dto.colorCode) then "#71717a" else dto.colorCode
                                        let parent = if String.IsNullOrEmpty(dto.parentId) then None else Some dto.parentId
                                        { Id = id; Name = dto.name; ColorCode = color; ParentId = parent; VideoCount = 0 }
                                    )
                                return list
                        }
                    
                    let! result = Db.importTags conn tags
                    match result with
                    | Ok importedList -> return! json importedList next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 自動適用ルールの一括インポート
    let importRules : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let contentType = ctx.Request.ContentType
                    let! rules = 
                        task {
                            if not (String.IsNullOrEmpty(contentType)) && contentType.StartsWith("text/plain") then
                                // TSV 形式のパース
                                let! body = ctx.ReadBodyFromRequestAsync()
                                let lines = body.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
                                let list = 
                                    lines 
                                    |> Array.skip 1 // ヘッダー行をスキップ
                                    |> Array.map (fun line -> line.Split('\t'))
                                    |> Array.filter (fun cols -> cols.Length >= 3)
                                    |> Array.map (fun cols ->
                                        let rawId = cols.[0].Trim()
                                        let id = if String.IsNullOrEmpty(rawId) then "r_" + Guid.NewGuid().ToString("N").Substring(0, 8) else rawId
                                        let pattern = cols.[1].Trim()
                                        let tagId = cols.[2].Trim()
                                        let matchType = if cols.Length > 3 && not (String.IsNullOrEmpty(cols.[3])) then cols.[3].Trim() else "partial"
                                        let targetField = if cols.Length > 4 && not (String.IsNullOrEmpty(cols.[4])) then cols.[4].Trim() else "fileName"
                                        let minSize = 
                                            if cols.Length > 5 && not (String.IsNullOrEmpty(cols.[5])) then 
                                                match Int64.TryParse(cols.[5].Trim()) with | true, v -> Some v | _ -> None
                                            else None
                                        let maxSize = 
                                            if cols.Length > 6 && not (String.IsNullOrEmpty(cols.[6])) then 
                                                match Int64.TryParse(cols.[6].Trim()) with | true, v -> Some v | _ -> None
                                            else None
                                        { Id = id; Pattern = pattern; TagId = tagId; MatchType = matchType; TargetField = targetField; MinSize = minSize; MaxSize = maxSize; CreatedAt = DateTime.UtcNow }
                                    )
                                    |> Array.toList
                                return list
                            else
                                // JSON 形式のパース
                                let! dtos = ctx.BindJsonAsync<ImportRuleDto list>()
                                let list = 
                                    dtos 
                                    |> List.map (fun dto ->
                                        let id = if String.IsNullOrEmpty(dto.id) then "r_" + Guid.NewGuid().ToString("N").Substring(0, 8) else dto.id
                                        let matchType = if String.IsNullOrEmpty(dto.matchType) then "partial" else dto.matchType
                                        let targetField = if String.IsNullOrEmpty(dto.targetField) then "fileName" else dto.targetField
                                        let minSize = if dto.minSize.HasValue then Some dto.minSize.Value else None
                                        let maxSize = if dto.maxSize.HasValue then Some dto.maxSize.Value else None
                                        { Id = id; Pattern = dto.pattern; TagId = dto.tagId; MatchType = matchType; TargetField = targetField; MinSize = minSize; MaxSize = maxSize; CreatedAt = DateTime.UtcNow }
                                    )
                                return list
                        }
                    
                    let! result = Db.importRules conn rules
                    match result with
                    | Ok importedList -> return! json importedList next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// スマートフォルダの一括インポート
    let importSmartFolders : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let contentType = ctx.Request.ContentType
                    let! folders = 
                        task {
                            if not (String.IsNullOrEmpty(contentType)) && contentType.StartsWith("text/plain") then
                                // TSV 形式のパース
                                let! body = ctx.ReadBodyFromRequestAsync()
                                let lines = body.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
                                let list = 
                                    lines 
                                    |> Array.skip 1 // ヘッダー行をスキップ
                                    |> Array.map (fun line -> line.Split('\t'))
                                    |> Array.filter (fun cols -> cols.Length >= 3)
                                    |> Array.map (fun cols ->
                                        let rawId = cols.[0].Trim()
                                        let id = if String.IsNullOrEmpty(rawId) then "sf_" + Guid.NewGuid().ToString("N").Substring(0, 8) else rawId
                                        let name = cols.[1].Trim()
                                        let query = cols.[2].Trim()
                                        { Id = id; Name = name; Query = query; CreatedAt = DateTime.UtcNow }
                                    )
                                    |> Array.toList
                                return list
                            else
                                // JSON 形式のパース
                                let! dtos = ctx.BindJsonAsync<ImportSmartFolderDto list>()
                                let list = 
                                    dtos 
                                    |> List.map (fun dto ->
                                        let id = if String.IsNullOrEmpty(dto.id) then "sf_" + Guid.NewGuid().ToString("N").Substring(0, 8) else dto.id
                                        { Id = id; Name = dto.name; Query = dto.query; CreatedAt = DateTime.UtcNow }
                                    )
                                return list
                        }
                    
                    let! result = Db.importSmartFolders conn folders
                    match result with
                    | Ok importedList -> return! json importedList next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 再生数ランキング上位100件の取得
    let getVideoRanking : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! result = Db.getVideoRanking conn
                    match result with
                    | Ok videos ->
                        let dtos = videos |> List.map (fun v -> {|
                            id = v.Id
                            fileName = v.FileName
                            filePath = v.FilePath
                            duration = v.Duration
                            fileSize = v.FileSize
                            thumbnailPath = Option.toObj v.ThumbnailPath
                            isFavorite = v.IsFavorite
                            createdAt = v.CreatedAt.ToString("o")
                            accessCount = v.AccessCount
                            lastAccessedAt = match v.LastAccessedAt with Some d -> d.ToString("o") | None -> null
                            tags = v.Tags |> List.map (fun t -> {|
                                id = t.Id
                                name = t.Name
                                colorCode = t.ColorCode
                                parentId = Option.toObj t.ParentId
                            |})
                        |})
                        return! json dtos next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 嗜好キーワードとおすすめ動画20件の取得
    let getVideoRecommendations : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! result = Db.getVideoRecommendations conn
                    match result with
                    | Ok (keywords, videos) ->
                        let videoDtos = videos |> List.map (fun v -> {|
                            id = v.Id
                            fileName = v.FileName
                            filePath = v.FilePath
                            duration = v.Duration
                            fileSize = v.FileSize
                            thumbnailPath = Option.toObj v.ThumbnailPath
                            isFavorite = v.IsFavorite
                            createdAt = v.CreatedAt.ToString("o")
                            accessCount = v.AccessCount
                            lastAccessedAt = match v.LastAccessedAt with Some d -> d.ToString("o") | None -> null
                            tags = v.Tags |> List.map (fun t -> {|
                                id = t.Id
                                name = t.Name
                                colorCode = t.ColorCode
                                parentId = Option.toObj t.ParentId
                            |})
                        |})
                        let res = {| keywords = keywords; videos = videoDtos |}
                        return! json res next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// 例外エラーログ一覧（最大100件）の取得
    let getErrorLogs : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                try
                    let conn = ctx.GetService<IDbConnection>()
                    let! result = Db.getErrorLogs conn
                    match result with
                    | Ok logs ->
                        let dtos = logs |> List.map (fun l -> {|
                            id = l.Id
                            message = l.Message
                            stackTrace = Option.toObj l.StackTrace
                            createdAt = l.CreatedAt.ToString("o")
                        |})
                        return! json dtos next ctx
                    | Error err -> return! ServerErrors.INTERNAL_ERROR (sprintf "%A" err) next ctx
                with ex ->
                    return! ServerErrors.INTERNAL_ERROR (ex.ToString()) next ctx
            }

    /// アプリケーションのルーティング定義

    let webApp : HttpHandler =
        choose [
            route "/api/ping"   >=> text "pong"
            routef "/api/videos/%s/stream" streamVideo
            
            // スキャン同期実行ルート
            route "/api/scan"   >=> choose [
                POST >=> triggerScan
            ]
            route "/api/media/stop"   >=> choose [
                POST >=> stopMediaProcessing
            ]
            route "/api/smart-folders" >=> choose [
                GET >=> getSmartFolders
                POST >=> saveSmartFolder
            ]
            routef "/api/smart-folders/%s" (fun id ->
                choose [
                    DELETE >=> deleteSmartFolder id
                ]
            )
            route "/api/tags/import"          >=> choose [ POST >=> importTags ]
            route "/api/rules/import"         >=> choose [ POST >=> importRules ]
            route "/api/smart-folders/import" >=> choose [ POST >=> importSmartFolders ]
            
            // 分析・ランキングルート
            route "/api/analysis/ranking"         >=> choose [ GET >=> getVideoRanking ]
            route "/api/analysis/recommendations" >=> choose [ GET >=> getVideoRecommendations ]
            route "/api/analysis/logs"            >=> choose [ GET >=> getErrorLogs ]


            // 動画タグ紐付けルート
            routef "/api/videos/%s/tags" (fun videoId ->
                choose [
                    POST   >=> addVideoTag videoId
                    DELETE >=> removeVideoTag videoId
                ]
            )

            // 動画お気に入りルート
            routef "/api/videos/%s/favorite" (fun videoId ->
                choose [
                    PUT >=> updateFavorite videoId
                ]
            )

            // 動画再生回数記録ルート
            routef "/api/videos/%s/access" (fun videoId ->
                choose [
                    POST >=> recordVideoAccess videoId
                ]
            )
            routef "/api/videos/%s/access/clear" (fun videoId ->
                choose [
                    POST >=> clearVideoAccess videoId
                ]
            )


            // 複数動画一括操作ルート (routef より先に置くことで /batch/ を先にマッチさせる)
            route "/api/videos/batch/tags/add"    >=> choose [ POST >=> batchAddTag ]
            route "/api/videos/batch/tags/remove" >=> choose [ POST >=> batchRemoveTag ]
            route "/api/videos/batch/favorite"    >=> choose [ POST >=> batchUpdateFavorite ]

            // 動画検索ルート
            route "/api/videos" >=> choose [
                GET >=> getVideos
            ]
            
            // タグCRUDルート
            route "/api/tags"   >=> choose [
                GET  >=> getTags
                POST >=> createTag
            ]
            routef "/api/tags/%s" (fun id ->
                choose [
                    PATCH  >=> updateTagParent id
                    DELETE >=> deleteTag id
                ]
            )

            // 自動タグ付けルールルート
            route "/api/rules"  >=> choose [
                GET  >=> getRules
                POST >=> createRule
            ]
            routef "/api/rules/%s" (fun id ->
                choose [
                    PUT    >=> updateRule id
                    DELETE >=> deleteRule id
                ]
            )
        ]
