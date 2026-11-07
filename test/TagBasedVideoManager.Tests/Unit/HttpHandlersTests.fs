namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open System.Data
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Data.Sqlite
open Giraffe
open TagBasedVideoManager
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure

/// <summary>
/// ダミーのロガー実装。テスト内のログ出力を無視または検証するために使用する。
/// </summary>
type DummyLogger() =
    interface ILogger with
        member _.Log<'TState>(logLevel: LogLevel, eventId: EventId, state: 'TState, ex: Exception, formatter: Func<'TState, Exception, string>) = ()
        member _.IsEnabled(logLevel: LogLevel) = false
        member _.BeginScope<'TState>(state: 'TState) = null

/// <summary>
/// テスト用の IMediaProcessor スタブ実装。
/// </summary>
type private DummyMediaProcessor() =
    interface IMediaProcessor with
        member _.GetMetadata(_path) = async { return Ok (120L, 1024L) }
        member _.GenerateThumbnail(_videoPath, thumbPath) = async { return Ok thumbPath }
        member _.CancelAll() = ()

/// <summary>
/// HttpHandlers の各エンドポイントロジックやエラーハンドラーを検証する単体テスト。
/// </summary>
module HttpHandlersTests =

    let private envLock = new System.Threading.SemaphoreSlim(1, 1)

    /// <summary>
    /// 環境変数を一時的に設定して非同期アクションを実行し、完了後に元の環境変数に復元するヘルパー。
    /// </summary>
    let private withEnvVars (vars: (string * string option) list) (action: unit -> Task) : Task =
        task {
            do! envLock.WaitAsync()
            let backups = vars |> List.map (fun (k, _) -> (k, Environment.GetEnvironmentVariable(k)))
            try
                for (k, vOpt) in vars do
                    match vOpt with
                    | Some v -> Environment.SetEnvironmentVariable(k, v)
                    | None -> Environment.SetEnvironmentVariable(k, null)
                do! action ()
            finally
                for (k, orig) in backups do
                    Environment.SetEnvironmentVariable(k, orig)
                envLock.Release() |> ignore
        }

    /// <summary>
    /// テスト用のインメモリ SQLite コネクションを生成し初期化するヘルパー。
    /// </summary>
    let createInMemoryDb () =
        let conn = new SqliteConnection("Data Source=:memory:")
        conn.Open()
        match DbInit.initializeDatabase conn with
        | Ok () -> conn
        | Error msg -> failwithf "Failed to initialize: %s" msg

    /// <summary>
    /// HttpContext および Response Body ストリームを初期化するヘルパー。
    /// </summary>
    let private setupTestContext (conn: IDbConnection) =
        let services = ServiceCollection()
        services.AddSingleton<IDbConnection>(conn) |> ignore
        services.AddSingleton<IMediaProcessor>(DummyMediaProcessor()) |> ignore
        services.AddGiraffe() |> ignore
        let serviceProvider = services.BuildServiceProvider()

        let ctx = DefaultHttpContext()
        ctx.RequestServices <- serviceProvider
        let memoryStream = new System.IO.MemoryStream()
        ctx.Response.Body <- memoryStream
        (ctx, memoryStream)

    /// <summary>
    /// MemoryStream から文字列レスポンスを読み取るヘルパー。
    /// </summary>
    let private readResponseBody (stream: System.IO.MemoryStream) =
        task {
            stream.Position <- 0L
            use reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8, false, 1024, true)
            return! reader.ReadToEndAsync()
        }

    /// <summary>
    /// giraffeErrorHandler が例外を正しく捕捉し、ログをデータベースに書き込んだ上で
    /// HTTP 500 ステータスコードを返却することを検証する。
    /// </summary>
    [<Fact>]
    let ``giraffeErrorHandler should record log to DB and return HTTP 500`` () =
        task {
            // 1. テスト用のインメモリ DB および DI サービスコンテナを構築
            use conn = createInMemoryDb ()
            let services = ServiceCollection()
            services.AddSingleton<IDbConnection>(conn) |> ignore
            services.AddGiraffe() |> ignore // Giraffe 依存関係 (ISerializer など) を登録
            let serviceProvider = services.BuildServiceProvider()

            // 2. HttpContext のモック化と DI サービスプロバイダーの紐付け
            let ctx = DefaultHttpContext()
            ctx.RequestServices <- serviceProvider

            // Giraffe レスポンスに必要な Body ストリームなどのセットアップ
            let memoryStream = new System.IO.MemoryStream()
            ctx.Response.Body <- memoryStream

            // 3. 例外とロガーの準備
            let testException = Exception("Unhandled handler test exception")
            let logger = DummyLogger()

            // 4. ハンドラーの呼び出し
            let handler = HttpHandlers.giraffeErrorHandler testException logger
            let next : HttpFunc = fun c -> task { return Some c }
            
            let! resCtxOpt = handler next ctx
            
            // 5. アサーション
            resCtxOpt.IsSome |> should be True
            let resCtx = resCtxOpt.Value

            // HTTP 500 エラーが返されていること
            resCtx.Response.StatusCode |> should equal 500

            // データベースに例外エラーが保存されていること
            let! dbLogsRes = Db.getErrorLogs conn
            match dbLogsRes with
            | Ok (logs: ErrorLog list) ->
                logs.Length |> should equal 1
                logs.[0].Message |> should equal "Unhandled handler test exception"
            | Error e -> failwithf "Failed to fetch error logs from DB: %A" e
        } :> Task

    /// <summary>
    /// getTags エンドポイントが JSON レスポンスの中に videoCount プロパティを含み、
    /// 正しい件数が格納されていることを検証する。
    /// </summary>
    [<Fact>]
    let ``getTags handler should include videoCount in JSON response`` () =
        task {
            // 1. テスト用のインメモリ DB および DI サービスコンテナを構築
            use conn = createInMemoryDb ()
            
            let testTag = { Id = "t_test_1"; Name = "TestTag"; ColorCode = "#ffffff"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn testTag
            
            let testVideo = {
                Id = "v_test_1"
                FileName = "test.mp4"
                FilePath = "/test.mp4"
                Duration = 10L
                FileSize = 100L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
            }
            let! _ = Db.saveVideo conn testVideo
            let! _ = Db.addTagToVideo conn "v_test_1" "t_test_1"

            let services = ServiceCollection()
            services.AddSingleton<IDbConnection>(conn) |> ignore
            services.AddGiraffe() |> ignore
            let serviceProvider = services.BuildServiceProvider()

            // 2. HttpContext のモック化
            let ctx = DefaultHttpContext()
            ctx.RequestServices <- serviceProvider

            let memoryStream = new System.IO.MemoryStream()
            ctx.Response.Body <- memoryStream

            // 3. ハンドラーの呼び出し
            let handler = HttpHandlers.getTags
            let next : HttpFunc = fun c -> task { return Some c }
            
            let! resCtxOpt = handler next ctx
            
            // 4. アサーション
            resCtxOpt.IsSome |> should be True
            let resCtx = resCtxOpt.Value
            resCtx.Response.StatusCode |> should equal 200

            // レスポンスボディの検証
            memoryStream.Position <- 0L
            use reader = new System.IO.StreamReader(memoryStream)
            let! jsonString = reader.ReadToEndAsync()
            
            jsonString.Contains("\"videoCount\"") |> should be True
            jsonString.Contains("\"videoCount\":1") |> should be True
        } :> Task

    /// <summary>
    /// IT-07: VIDEO_DIRS 未設定（または空白のみ）の状態で triggerScan を実行した際、
    /// HTTP 500 が返り、レスポンス本文に VIDEO_DIRS が未設定である旨と設定例（VIDEO_DIRS=D:\Anime;F:\Movie）が含まれること。
    /// </summary>
    [<Fact>]
    let ``IT-07: triggerScan should return 500 with example when VIDEO_DIRS is not set`` () =
        withEnvVars [ ("VIDEO_DIRS", None); ("VIDEO_DIR", None) ] (fun () ->
            task {
                use conn = createInMemoryDb ()
                let (ctx, stream) = setupTestContext conn
                let handler = HttpHandlers.triggerScan
                let next : HttpFunc = fun c -> task { return Some c }

                let! resCtxOpt = handler next ctx

                resCtxOpt.IsSome |> should be True
                let resCtx = resCtxOpt.Value
                resCtx.Response.StatusCode |> should equal 500

                let! body = readResponseBody stream
                body.Contains("VIDEO_DIRS") |> should be True
                body.Contains(@"VIDEO_DIRS=D:\Anime;F:\Movie") |> should be True
            } :> Task
        )

    /// <summary>
    /// IT-07b: VIDEO_DIRS にドライブ直下（例: D:\）が指定されている状態で triggerScan を実行した際、
    /// HTTP 500 が返り、フォルダ名を持たないパスは不可である旨のメッセージが含まれること。
    /// </summary>
    [<Fact>]
    let ``IT-07b: triggerScan should return 500 when VIDEO_DIRS contains root path without folder name`` () =
        withEnvVars [ ("VIDEO_DIRS", Some @"D:\"); ("VIDEO_DIR", None) ] (fun () ->
            task {
                use conn = createInMemoryDb ()
                let (ctx, stream) = setupTestContext conn
                let handler = HttpHandlers.triggerScan
                let next : HttpFunc = fun c -> task { return Some c }

                let! resCtxOpt = handler next ctx

                resCtxOpt.IsSome |> should be True
                let resCtx = resCtxOpt.Value
                resCtx.Response.StatusCode |> should equal 500

                let! body = readResponseBody stream
                (body.Contains("フォルダ名") || body.Contains("ドライブ直下") || body.Contains("ルート"))
                |> should be True
                body.Contains(@"VIDEO_DIRS=D:\Anime;F:\Movie") |> should be True
            } :> Task
        )

    /// <summary>
    /// IT-08: 旧変数 VIDEO_DIR のみが設定され VIDEO_DIRS が未設定の状態で triggerScan を実行した際、
    /// HTTP 500 が返り、旧変数が廃止された旨と VIDEO_DIRS への移行案内メッセージが含まれること。
    /// </summary>
    [<Fact>]
    let ``IT-08: triggerScan should return 500 with migration guidance when only legacy VIDEO_DIR is set`` () =
        withEnvVars [ ("VIDEO_DIRS", None); ("VIDEO_DIR", Some @"C:\OldVideos") ] (fun () ->
            task {
                use conn = createInMemoryDb ()
                let (ctx, stream) = setupTestContext conn
                let handler = HttpHandlers.triggerScan
                let next : HttpFunc = fun c -> task { return Some c }

                let! resCtxOpt = handler next ctx

                resCtxOpt.IsSome |> should be True
                let resCtx = resCtxOpt.Value
                resCtx.Response.StatusCode |> should equal 500

                let! body = readResponseBody stream
                body.Contains("VIDEO_DIR") |> should be True
                body.Contains("廃止") |> should be True
                body.Contains("VIDEO_DIRS") |> should be True
                body.Contains(@"VIDEO_DIRS=D:\Anime;F:\Movie") |> should be True
            } :> Task
        )

    /// <summary>
    /// ST-03: VIDEO_DIRS 未設定でのスキャン失敗時、既存の動画・タグレコードが削除・変更されず
    /// そのまま不変で保護されること（異常遷移拒否）。
    /// </summary>
    [<Fact>]
    let ``ST-03: triggerScan should preserve existing videos and tags unchanged when scan fails`` () =
        withEnvVars [ ("VIDEO_DIRS", None); ("VIDEO_DIR", None) ] (fun () ->
            task {
                use conn = createInMemoryDb ()

                // 1. 既存のタグと動画を準備して紐付け
                let existingTag = {
                    Id = "t_preserved_1"
                    Name = "PreservedTag"
                    ColorCode = "#123456"
                    ParentId = None
                    VideoCount = 1
                }
                let! tagRes = Db.saveTag conn existingTag
                match tagRes with
                | Ok () -> ()
                | Error e -> failwithf "Failed to save initial tag: %A" e

                let existingVideo = {
                    Id = "v_preserved_1"
                    FileName = "existing_movie.mp4"
                    FilePath = @"D:\Anime\existing_movie.mp4"
                    Duration = 600L
                    FileSize = 104857600L
                    ThumbnailPath = Some @"D:\Anime\thumbnails\existing.jpg"
                    IsFavorite = true
                    CreatedAt = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    FileModifiedAt = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    AccessCount = 5
                    LastAccessedAt = Some (DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))
                    Tags = [ existingTag ]
                }
                let! vidRes = Db.saveVideo conn existingVideo
                match vidRes with
                | Ok _ -> ()
                | Error e -> failwithf "Failed to save initial video: %A" e

                let! linkRes = Db.addTagToVideo conn existingVideo.Id existingTag.Id
                match linkRes with
                | Ok _ -> ()
                | Error e -> failwithf "Failed to link initial tag: %A" e

                // 2. VIDEO_DIRS 未設定の状態で triggerScan 実行
                let (ctx, _stream) = setupTestContext conn
                let handler = HttpHandlers.triggerScan
                let next : HttpFunc = fun c -> task { return Some c }

                let! resCtxOpt = handler next ctx

                // 3. スキャン要求自体が 500 で拒否されること
                resCtxOpt.IsSome |> should be True
                let resCtx = resCtxOpt.Value
                resCtx.Response.StatusCode |> should equal 500

                // 4. 既存レコード（動画・タグ）が一切削除・変更されていないこと（異常遷移拒否）
                let! videosRes = Db.getVideos conn None
                match videosRes with
                | Ok videos ->
                    videos.Length |> should equal 1
                    let v = videos.[0]
                    v.Id |> should equal existingVideo.Id
                    v.FileName |> should equal existingVideo.FileName
                    v.FilePath |> should equal existingVideo.FilePath
                    v.Duration |> should equal existingVideo.Duration
                    v.FileSize |> should equal existingVideo.FileSize
                    v.IsFavorite |> should equal true
                    v.AccessCount |> should equal 5
                    v.Tags.Length |> should equal 1
                    v.Tags.[0].Id |> should equal existingTag.Id
                | Error e -> failwithf "Failed to fetch videos from DB: %A" e

                let! tagsRes = Db.getTags conn
                match tagsRes with
                | Ok tags ->
                    tags.Length |> should equal 1
                    let t = tags.[0]
                    t.Id |> should equal existingTag.Id
                    t.Name |> should equal existingTag.Name
                | Error e -> failwithf "Failed to fetch tags from DB: %A" e
            } :> Task
        )
