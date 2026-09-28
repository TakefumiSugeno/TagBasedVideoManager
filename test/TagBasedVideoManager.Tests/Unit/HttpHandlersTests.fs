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
/// HttpHandlers の各エンドポイントロジックやエラーハンドラーを検証する単体テスト。
/// </summary>
module HttpHandlersTests =

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
