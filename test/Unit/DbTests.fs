namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open Microsoft.Data.Sqlite
open TagBasedVideoManager.Domain
open TagBasedVideoManager.Infrastructure

/// <summary>
/// Db モジュールにおけるデータベース操作やキーワード抽出アルゴリズムを検証する単体テスト。
/// </summary>
module DbTests =

    /// <summary>
    /// テスト用のインメモリ SQLite コネクションを生成し、初期データベースマイグレーションを適用するヘルパー。
    /// </summary>
    let createInMemoryDb () =
        let conn = new SqliteConnection("Data Source=:memory:")
        conn.Open()
        match DbInit.initializeDatabase conn with
        | Ok () -> conn
        | Error msg -> failwithf "Failed to initialize in-memory database: %s" msg

    /// <summary>
    /// Tags テーブル of CRUD および親子関係の設定に関する検証。
    /// </summary>
    [<Fact>]
    let ``Tag CRUD operations should work properly`` () =
        async {
            use conn = createInMemoryDb ()
            
            // 1. 保存 (saveTag)
            let tag1 = { Id = "t1"; Name = "ツーリング"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
            let! resSave = Db.saveTag conn tag1
            match resSave with
            | Ok () -> ()
            | Error e -> failwithf "saveTag failed: %A" e

            // 2. 取得 (getTags)
            let! resGet = Db.getTags conn
            match resGet with
            | Ok (tags: Tag list) ->
                tags.Length |> should equal 1
                tags.[0].Name |> should equal "ツーリング"
                tags.[0].ColorCode |> should equal "#ff0000"
            | Error e -> failwithf "getTags failed: %A" e

            // 3. 親タグの設定 (updateTagParent)
            let tag2 = { Id = "t2"; Name = "日常"; ColorCode = "#00ff00"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag2
            let! resParent = Db.updateTagParent conn "t2" (Some "t1")
            match resParent with
            | Ok () -> ()
            | Error e -> failwithf "updateTagParent failed: %A" e

            let! resGet2 = Db.getTags conn
            match resGet2 with
            | Ok (tags: Tag list) ->
                let t2Opt = tags |> List.tryFind (fun (t: Tag) -> t.Id = "t2")
                t2Opt.IsSome |> should be True
                t2Opt.Value.ParentId |> should equal (Some "t1")
            | Error e -> failwithf "getTags failed: %A" e

            // 4. 削除 (deleteTag)
            let! resDel = Db.deleteTag conn "t2"
            match resDel with
            | Ok () -> ()
            | Error e -> failwithf "deleteTag failed: %A" e
            let! resGet3 = Db.getTags conn
            match resGet3 with
            | Ok (tags: Tag list) ->
                tags |> List.exists (fun (t: Tag) -> t.Id = "t2") |> should be False
            | Error e -> failwithf "getTags failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// Video アクセス回数（再生回数）のインクリメントおよびクリア処理の検証。
    /// </summary>
    [<Fact>]
    let ``Video access counting and resetting should work`` () =
        async {
            use conn = createInMemoryDb ()
            
            let video = {
                Id = "v1"
                FileName = "test_video.mp4"
                FilePath = "C:\\test_video.mp4"
                Duration = 100L
                FileSize = 5000000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
            }

            let! resSave = Db.saveVideo conn video
            match resSave with
            | Ok _ -> ()
            | Error e -> failwithf "saveVideo failed: %A" e

            // 1. アクセス記録 (recordVideoAccess)
            let! resAccess = Db.recordVideoAccess conn "v1"
            match resAccess with
            | Ok () -> ()
            | Error e -> failwithf "recordVideoAccess failed: %A" e

            let! resGet = Db.getVideo conn "v1"
            match resGet with
            | Ok v ->
                v.AccessCount |> should equal 1
                v.LastAccessedAt.IsSome |> should be True
            | Error e -> failwithf "getVideo failed: %A" e

            // 2. アクセスクリア (clearVideoAccess)
            let! resClear = Db.clearVideoAccess conn "v1"
            match resClear with
            | Ok () -> ()
            | Error e -> failwithf "clearVideoAccess failed: %A" e

            let! resGet2 = Db.getVideo conn "v1"
            match resGet2 with
            | Ok v ->
                v.AccessCount |> should equal 0
                v.LastAccessedAt.IsNone |> should be True
            | Error e -> failwithf "getVideo failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// エラーログの追加とクリア機能の検証。
    /// </summary>
    [<Fact>]
    let ``Error log tracing should work`` () =
        async {
            use conn = createInMemoryDb ()

            // 1. 保存 (saveErrorLog)
            do! Db.saveErrorLog conn "Test Exception" (Some "at test location")
            
            let! resGet = Db.getErrorLogs conn
            match resGet with
            | Ok (logs: ErrorLog list) ->
                logs.Length |> should equal 1
                logs.[0].Message |> should equal "Test Exception"
                logs.[0].StackTrace |> should equal (Some "at test location")
            | Error e -> failwithf "getErrorLogs failed: %A" e

            // 2. 全クリア (clearErrorLogs)
            let! resClear = Db.clearErrorLogs conn
            match resClear with
            | Ok () -> ()
            | Error e -> failwithf "clearErrorLogs failed: %A" e

            let! resGet2 = Db.getErrorLogs conn
            match resGet2 with
            | Ok (logs: ErrorLog list) -> logs.Length |> should equal 0
            | Error e -> failwithf "getErrorLogs failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// 嗜好分析用キーワード抽出ロジック（登録済みタグ優先＋文字種境界分割＋ストップワード除外）の検証。
    /// </summary>
    [<Fact>]
    let ``extractKeywordsFromFileName should use hybrid logic`` () =
        // 登録済みタグ辞書を用意
        let tags = [
            { Id = "t1"; Name = "プログラミング"; ColorCode = ""; ParentId = None; VideoCount = 0 }
            { Id = "t2"; Name = "F#"; ColorCode = ""; ParentId = None; VideoCount = 0 }
        ]

        // 1. 登録済みタグが最優先で部分一致抽出されること
        let keywords = Db.extractKeywordsFromFileName tags "F#プログラミング入門.mp4"
        keywords |> should contain "プログラミング"
        keywords |> should contain "F#"

        // 2. 未知の単語が文字種境界で分割されること（ひらがな未知語はノイズ除外され、英数字・漢字・カタカナは抽出される）
        let keywords2 = Db.extractKeywordsFromFileName tags "2026年Familyキャンプ動画"
        keywords2 |> should contain "2026"
        keywords2 |> should contain "Family"
        keywords2 |> should contain "キャンプ"
        keywords2 |> should contain "動画"
        // 「年」やひらがな接続詞（「の」等）はストップワードやノイズとして除外される
        keywords2 |> should not' (contain "の")

    /// <summary>
    /// getTags を実行した際に、子タグに付与されている動画の件数が
    /// 親タグにも再帰的（積算）かつユニーク（重複排除）にカウントされて返ることを検証する。
    /// </summary>
    [<Fact>]
    let ``getTags should count video association cascadingly and uniquely`` () =
        async {
            use conn = createInMemoryDb ()
            
            // 1. 親タグおよび子タグを保存
            let parentTag = { Id = "t_parent"; Name = "ツーリング"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
            let childTag = { Id = "t_child"; Name = "キャンプツーリング"; ColorCode = "#00ff00"; ParentId = Some "t_parent"; VideoCount = 0 }
            let! _ = Db.saveTag conn parentTag
            let! _ = Db.saveTag conn childTag

            // 2. 動画Aを保存し、子タグ「キャンプツーリング」のみを紐付け
            let videoA = {
                Id = "v_a"
                FileName = "trip_camp.mp4"
                FilePath = "C:\\trip_camp.mp4"
                Duration = 100L
                FileSize = 5000000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
            }
            let! _ = Db.saveVideo conn videoA
            let! _ = Db.addTagToVideo conn "v_a" "t_child"

            // 3. 動画Bを保存し、親タグ・子タグ両方に紐付け（重複排除の検証用）
            let videoB = {
                Id = "v_b"
                FileName = "trip_main.mp4"
                FilePath = "C:\\trip_main.mp4"
                Duration = 120L
                FileSize = 6000000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
            }
            let! _ = Db.saveVideo conn videoB
            let! _ = Db.addTagToVideo conn "v_b" "t_parent"
            let! _ = Db.addTagToVideo conn "v_b" "t_child"

            // 4. getTags を実行して件数を検証
            let! resGet = Db.getTags conn
            match resGet with
            | Ok tags ->
                let parent = tags |> List.find (fun t -> t.Id = "t_parent")
                let child = tags |> List.find (fun t -> t.Id = "t_child")

                // 子タグ「キャンプツーリング」には videoA と videoB が紐付くため = 2
                child.VideoCount |> should equal 2

                // 親タグ「ツーリング」には 直接 videoB が、子タグ経由で videoA, videoB が紐付く。
                // 重複排除して計2件 (videoA, videoB) となるべき。
                parent.VideoCount |> should equal 2
            | Error e -> failwithf "getTags failed: %A" e
        } |> Async.RunSynchronously

