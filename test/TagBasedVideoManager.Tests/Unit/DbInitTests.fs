namespace TagBasedVideoManager.Tests

open System
open System.IO
open Xunit
open FsUnit
open Dapper
open TagBasedVideoManager.Infrastructure

type DbInitTests () =
    let dbPath = Path.Combine(AppContext.BaseDirectory, $"test_{Guid.NewGuid():N}.db")

    interface IDisposable with
        member _.Dispose() =
            // テスト終了後に一時データベースファイルを削除
            if File.Exists(dbPath) then
                try File.Delete(dbPath) with | _ -> ()

    [<Fact>]
    member _.``initializeDatabaseを実行すると必要なすべてのテーブルが正常に作成される`` () =
        // 1. 接続を取得し、初期状態を確認 (ファイルが存在しない、または空)
        use conn = Db.getConnection dbPath
        
        // 2. 初期化を実行
        let result = DbInit.initializeDatabase conn
        result |> should equal (Ok () : Result<unit, string>)

        // 3. 各テーブルの存在を検証
        let checkTableExists tableName =
            let query = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name;"
            conn.ExecuteScalar<int64>(query, {| name = tableName |}) |> int

        checkTableExists "Videos" |> should equal 1
        checkTableExists "Tags" |> should equal 1
        checkTableExists "VideoTags" |> should equal 1
        checkTableExists "TaggingRules" |> should equal 1
        checkTableExists "ErrorLogs" |> should equal 1


    [<Fact>]
    member _.``データベース初期化時に外部キー制約が有効化される`` () =
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 外部キー制約が有効になっているかをクエリして確認
        let fkEnabled = conn.ExecuteScalar<int64>("PRAGMA foreign_keys;")
        fkEnabled |> should equal 1L

    [<Fact>]
    member _.``すでに初期化済みのデータベースに対して再度initializeDatabaseを実行しても正常に動作する`` () =
        // 境界値/冪等性: 二重に初期化を実行した場合の挙動検証
        use conn = Db.getConnection dbPath
        
        // 1回目の初期化
        let result1 = DbInit.initializeDatabase conn
        result1 |> should equal (Ok () : Result<unit, string>)
        
        // 2回目の初期化
        let result2 = DbInit.initializeDatabase conn
        result2 |> should equal (Ok () : Result<unit, string>) // エラーにならずに正常終了すること

    [<Fact>]
    member _.``例外ログが正常にインサートされ、初期化時にすべてクリアされる`` () =
        use conn = Db.getConnection dbPath
        let _ = DbInit.initializeDatabase conn |> ignore

        // 1. ログを保存
        Db.saveErrorLog conn "Test Error Message" (Some "Test Stack Trace") |> Async.RunSynchronously

        // 2. ログが保存されていることを確認
        let countQuery = "SELECT count(*) FROM ErrorLogs;"
        let countBefore = conn.ExecuteScalar<int64>(countQuery)
        countBefore |> should equal 1L

        // 3. 再度 initializeDatabase を呼び出す (起動時クリアを想定)
        let _ = DbInit.initializeDatabase conn |> ignore

        // 4. ログが削除されていることを確認
        let countAfter = conn.ExecuteScalar<int64>(countQuery)
        countAfter |> should equal 0L


