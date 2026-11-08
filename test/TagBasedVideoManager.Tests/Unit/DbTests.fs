namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open Microsoft.Data.Sqlite
open Dapper
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
                FileModifiedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
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
                FileModifiedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
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
                FileModifiedAt = DateTime.Now
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
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

    /// <summary>
    /// UT-01 / DT-01: 複数動画・複数タグの正常系一括取得検証。
    /// 各動画に紐付くタグが正しく結合されて返却されること。
    /// </summary>
    [<Fact>]
    let ``getVideos should batch load tags correctly for multiple videos (UT-01, DT-01)`` () =
        async {
            use conn = createInMemoryDb ()

            // タグを2件作成
            let tag1 = { Id = "t_1"; Name = "アクション"; ColorCode = "#ff0000"; ParentId = None; VideoCount = 0 }
            let tag2 = { Id = "t_2"; Name = "コメディ"; ColorCode = "#00ff00"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag1
            let! _ = Db.saveTag conn tag2

            // 動画を3件作成
            for i in 1 .. 3 do
                let v = {
                    Id = $"v_{i}"
                    FileName = $"video_{i}.mp4"
                    FilePath = $"C:\\videos\\video_{i}.mp4"
                    Duration = int64 (i * 60)
                    FileSize = int64 (i * 1000000)
                    ThumbnailPath = None
                    IsFavorite = false
                    CreatedAt = DateTime.UtcNow
                    FileModifiedAt = DateTime.UtcNow
                    AccessCount = 0
                    LastAccessedAt = None
                    Tags = []
                    FileUuid = None
                }
                let! _ = Db.saveVideo conn v
                // 全動画に tag1, tag2 を紐付け
                let! _ = Db.addTagToVideo conn $"v_{i}" "t_1"
                let! _ = Db.addTagToVideo conn $"v_{i}" "t_2"
                ()

            // getVideos を実行
            let! res = Db.getVideos conn None
            match res with
            | Ok videos ->
                videos.Length |> should equal 3
                for v in videos do
                    v.Tags.Length |> should equal 2
                    let tagIds = v.Tags |> List.map (fun t -> t.Id) |> Set.ofList
                    tagIds |> should equal (set [ "t_1"; "t_2" ])
            | Error e -> failwithf "getVideos failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// UT-02 / DT-02: タグあり動画とタグなし動画が混在する場合の境界値検証。
    /// タグ未付与の動画は Tags = [] となり、タグ付き動画には正確にタグが付与されること。
    /// </summary>
    [<Fact>]
    let ``getVideos should handle mixed tagged and untagged videos (UT-02, DT-02)`` () =
        async {
            use conn = createInMemoryDb ()

            let tag1 = { Id = "t_single"; Name = "アニメ"; ColorCode = "#0000ff"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag1

            let vTagged = {
                Id = "v_tagged"
                FileName = "tagged.mp4"
                FilePath = "C:\\tagged.mp4"
                Duration = 60L
                FileSize = 1000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            let vUntagged = {
                Id = "v_untagged"
                FileName = "untagged.mp4"
                FilePath = "C:\\untagged.mp4"
                Duration = 60L
                FileSize = 1000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }

            let! _ = Db.saveVideo conn vTagged
            let! _ = Db.saveVideo conn vUntagged
            let! _ = Db.addTagToVideo conn "v_tagged" "t_single"

            let! res = Db.getVideos conn None
            match res with
            | Ok videos ->
                videos.Length |> should equal 2
                let tagged = videos |> List.find (fun v -> v.Id = "v_tagged")
                let untagged = videos |> List.find (fun v -> v.Id = "v_untagged")
                tagged.Tags.Length |> should equal 1
                tagged.Tags.[0].Id |> should equal "t_single"
                untagged.Tags.Length |> should equal 0
            | Error e -> failwithf "getVideos failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// UT-03 / DT-04: 動画0件の空状態境界検証。
    /// DBに動画が存在しない場合、例外やエラーなく即座に空リストが返ること。
    /// </summary>
    [<Fact>]
    let ``getVideos should safely return empty list when no videos exist (UT-03, DT-04)`` () =
        async {
            use conn = createInMemoryDb ()
            let! res = Db.getVideos conn None
            match res with
            | Ok videos -> videos.Length |> should equal 0
            | Error e -> failwithf "getVideos failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// UT-04 / DT-03: 多数の動画が存在する場合の境界値検証（チャンク分割INクエリの動作確認）。
    /// 50件以上の動画に対しても、全動画のタグ情報が欠落なく結合されること。
    /// </summary>
    [<Fact>]
    let ``getVideos should handle large number of videos with tags (UT-04, DT-03)`` () =
        async {
            use conn = createInMemoryDb ()

            let tagA = { Id = "t_a"; Name = "TagA"; ColorCode = "#111"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tagA

            let count = 60
            for i in 1 .. count do
                let v = {
                    Id = $"v_large_{i}"
                    FileName = $"large_{i}.mp4"
                    FilePath = $"C:\\large_{i}.mp4"
                    Duration = 10L
                    FileSize = 100L
                    ThumbnailPath = None
                    IsFavorite = false
                    CreatedAt = DateTime.UtcNow
                    FileModifiedAt = DateTime.UtcNow
                    AccessCount = 0
                    LastAccessedAt = None
                    Tags = []
                    FileUuid = None
                }
                let! _ = Db.saveVideo conn v
                let! _ = Db.addTagToVideo conn $"v_large_{i}" "t_a"
                ()

            let! res = Db.getVideos conn None
            match res with
            | Ok videos ->
                videos.Length |> should equal count
                for v in videos do
                    v.Tags.Length |> should equal 1
                    v.Tags.[0].Id |> should equal "t_a"
            | Error e -> failwithf "getVideos failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// UT-05: searchVideos におけるタグ一括結合検証。
    /// 検索条件に一致した動画群に対して、正しくタグ情報が付与されて返ること。
    /// </summary>
    [<Fact>]
    let ``searchVideos should batch load tags correctly for matched videos (UT-05)`` () =
        async {
            use conn = createInMemoryDb ()

            let tag1 = { Id = "t_search"; Name = "探訪"; ColorCode = "#abcdef"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag1

            let v1 = {
                Id = "v_match"
                FileName = "kyoto_travel.mp4"
                FilePath = "C:\\kyoto_travel.mp4"
                Duration = 100L
                FileSize = 1000L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            let v2 = {
                Id = "v_other"
                FileName = "home.mp4"
                FilePath = "C:\\home.mp4"
                Duration = 50L
                FileSize = 500L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }

            let! _ = Db.saveVideo conn v1
            let! _ = Db.saveVideo conn v2
            let! _ = Db.addTagToVideo conn "v_match" "t_search"

            // "kyoto" で検索
            let tokens = [ Keyword "kyoto" ]
            let! res = Db.searchVideos conn tokens
            match res with
            | Ok videos ->
                videos.Length |> should equal 1
                videos.[0].Id |> should equal "v_match"
                videos.[0].Tags.Length |> should equal 1
                videos.[0].Tags.[0].Name |> should equal "探訪"
            | Error e -> failwithf "searchVideos failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// Videos テーブルの FileUuid カラム永続化検証。
    /// FileUuid = Some "..." および FileUuid = None の動画を保存し、正しく取得できること。
    /// </summary>
    [<Fact>]
    let ``FileUuid persistence should save and load FileUuid correctly`` () =
        async {
            use conn = createInMemoryDb ()

            let uuidVal = "8f9d5a12-4b6c-4e8a-9c1d-2e3f4a5b6c7d"
            let vWithUuid = {
                Id = "v_uuid_1"
                FileName = "has_uuid.mp4"
                FilePath = "C:\\has_uuid.mp4"
                Duration = 60L
                FileSize = 1024L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = Some uuidVal
            }

            let! resSave = Db.saveVideo conn vWithUuid
            match resSave with
            | Ok _ -> ()
            | Error e -> failwithf "saveVideo failed: %A" e

            let! resGet = Db.getVideo conn "v_uuid_1"
            match resGet with
            | Ok v ->
                v.FileUuid |> should equal (Some uuidVal)
            | Error e -> failwithf "getVideo failed: %A" e

            // FileUuid = None の場合
            let vNoUuid = {
                Id = "v_uuid_2"
                FileName = "no_uuid.mp4"
                FilePath = "C:\\no_uuid.mp4"
                Duration = 60L
                FileSize = 1024L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            let! _ = Db.saveVideo conn vNoUuid
            let! resGet2 = Db.getVideo conn "v_uuid_2"
            match resGet2 with
            | Ok v ->
                v.FileUuid |> should equal None
            | Error e -> failwithf "getVideo failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// updateVideoFileUuid の検証。
    /// 既存の動画に対して FileUuid を更新できること。
    /// </summary>
    [<Fact>]
    let ``updateVideoFileUuid should update FileUuid of existing video`` () =
        async {
            use conn = createInMemoryDb ()

            let v = {
                Id = "v_upd_uuid"
                FileName = "to_update.mp4"
                FilePath = "C:\\to_update.mp4"
                Duration = 100L
                FileSize = 2048L
                ThumbnailPath = None
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = None
            }
            let! _ = Db.saveVideo conn v

            let targetUuid = "11111111-2222-3333-4444-555555555555"
            let! resUpd = Db.updateVideoFileUuid conn "v_upd_uuid" targetUuid
            match resUpd with
            | Ok () -> ()
            | Error e -> failwithf "updateVideoFileUuid failed: %A" e

            let! resGet = Db.getVideo conn "v_upd_uuid"
            match resGet with
            | Ok loaded ->
                loaded.FileUuid |> should equal (Some targetUuid)
            | Error e -> failwithf "getVideo failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// updateVideoLocation の検証。
    /// 移動・リネームに伴い、パス、ファイル名、サイズ、更新日時、FileUuid、ThumbnailPath が更新され、
    /// 既存の Id, 付与タグ, IsFavorite, AccessCount, Duration が完全に維持されること。
    /// </summary>
    [<Fact>]
    let ``updateVideoLocation should update path metadata while preserving id tags and stats`` () =
        async {
            use conn = createInMemoryDb ()

            // 1. タグと動画を作成
            let tag1 = { Id = "t_preserve"; Name = "保持タグ"; ColorCode = "#123456"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag1

            let initialModifiedAt = DateTime.UtcNow.AddDays(-10.0)
            let initialVideo = {
                Id = "v_location_1"
                FileName = "old_name.mp4"
                FilePath = "C:\\Videos\\old_name.mp4"
                Duration = 180L
                FileSize = 5000000L
                ThumbnailPath = Some "C:\\Thumbs\\old.jpg"
                IsFavorite = true
                CreatedAt = DateTime.UtcNow.AddDays(-20.0)
                FileModifiedAt = initialModifiedAt
                AccessCount = 42
                LastAccessedAt = Some DateTime.UtcNow
                Tags = []
                FileUuid = None
            }
            let! _ = Db.saveVideo conn initialVideo
            let! _ = Db.addTagToVideo conn "v_location_1" "t_preserve"
            let! _ = Db.updateVideoFavorite conn "v_location_1" true

            // 2. 移動・リネーム情報で updateVideoLocation を実行
            let newFileName = "new_name.mp4"
            let newFilePath = "D:\\MovedVideos\\SubFolder\\new_name.mp4"
            let newFileSize = 5000000L
            let newModifiedAt = DateTime.UtcNow.AddDays(-1.0)
            let newUuid = Some "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            let newThumbPath = Some "D:\\NewThumbs\\v_location_1.jpg"

            let! resUpd = Db.updateVideoLocation conn "v_location_1" newFileName newFilePath newFileSize newModifiedAt newUuid newThumbPath
            match resUpd with
            | Ok () -> ()
            | Error e -> failwithf "updateVideoLocation failed: %A" e

            // 3. 検証
            let! resGet = Db.getVideo conn "v_location_1"
            match resGet with
            | Ok updated ->
                // 更新されたフィールド
                updated.FileName |> should equal newFileName
                updated.FilePath |> should equal newFilePath
                updated.FileSize |> should equal newFileSize
                updated.FileUuid |> should equal newUuid
                updated.ThumbnailPath |> should equal newThumbPath

                // 維持されるべきフィールド
                updated.Id |> should equal "v_location_1"
                updated.Duration |> should equal 180L
                updated.IsFavorite |> should equal true
                updated.AccessCount |> should equal 42
                updated.Tags.Length |> should equal 1
                updated.Tags.[0].Id |> should equal "t_preserve"
            | Error e -> failwithf "getVideo failed: %A" e
        } |> Async.RunSynchronously

    /// <summary>
    /// deleteVideo および CASCADE 連動削除の検証 (IT-05)。
    /// 動画を削除した際、Videos テーブルから削除されると同時に、
    /// 外部キー制約 (ON DELETE CASCADE) により VideoTags テーブルの紐付けも連動して自動削除されること。
    /// </summary>
    [<Fact>]
    let ``deleteVideo should remove video and cascade delete VideoTags (IT-05)`` () =
        async {
            use conn = createInMemoryDb ()

            // 1. タグ2件を登録
            let tag1 = { Id = "t_cascade_1"; Name = "タグ1"; ColorCode = "#111"; ParentId = None; VideoCount = 0 }
            let tag2 = { Id = "t_cascade_2"; Name = "タグ2"; ColorCode = "#222"; ParentId = None; VideoCount = 0 }
            let! _ = Db.saveTag conn tag1
            let! _ = Db.saveTag conn tag2

            // 2. 動画を登録し、タグ2件を紐付け
            let video = {
                Id = "v_cascade_test"
                FileName = "cascade_test.mp4"
                FilePath = "C:\\cascade_test.mp4"
                Duration = 120L
                FileSize = 1000000L
                ThumbnailPath = Some "C:\\Thumbs\\cascade.jpg"
                IsFavorite = false
                CreatedAt = DateTime.UtcNow
                FileModifiedAt = DateTime.UtcNow
                AccessCount = 0
                LastAccessedAt = None
                Tags = []
                FileUuid = Some "33333333-3333-3333-3333-333333333333"
            }
            let! _ = Db.saveVideo conn video
            let! _ = Db.addTagToVideo conn "v_cascade_test" "t_cascade_1"
            let! _ = Db.addTagToVideo conn "v_cascade_test" "t_cascade_2"

            // 事前確認: VideoTags に2件存在すること
            let countSql = "SELECT COUNT(*) FROM VideoTags WHERE VideoId = @VideoId"
            let! countBefore = Async.AwaitTask (conn.ExecuteScalarAsync<int64>(countSql, {| VideoId = "v_cascade_test" |}))
            countBefore |> should equal 2L

            // 3. deleteVideo を実行
            let! resDel = Db.deleteVideo conn "v_cascade_test"
            match resDel with
            | Ok () -> ()
            | Error e -> failwithf "deleteVideo failed: %A" e

            // 4. Videos から削除されていること
            let! resGet = Db.getVideo conn "v_cascade_test"
            match resGet with
            | Error (RecordNotFound _) -> () // 正常に削除された
            | Ok _ -> failwith "Expected RecordNotFound, but video was still found in DB"
            | Error other -> failwithf "Unexpected error from getVideo: %A" other

            // 5. VideoTags から外部キー CASCADE により連動削除されていること (IT-05)
            let! countAfter = Async.AwaitTask (conn.ExecuteScalarAsync<int64>(countSql, {| VideoId = "v_cascade_test" |}))
            countAfter |> should equal 0L

            // 他のタグ自体（Tagsテーブル）は削除されていないこと
            let! tagsRes = Db.getTags conn
            match tagsRes with
            | Ok tags ->
                tags |> List.exists (fun t -> t.Id = "t_cascade_1") |> should be True
                tags |> List.exists (fun t -> t.Id = "t_cascade_2") |> should be True
            | Error e -> failwithf "getTags failed: %A" e
        } |> Async.RunSynchronously


