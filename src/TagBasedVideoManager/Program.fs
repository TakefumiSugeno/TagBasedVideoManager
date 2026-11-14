namespace TagBasedVideoManager

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.FileProviders
open Microsoft.AspNetCore.Http
open Giraffe
open TagBasedVideoManager.Infrastructure
open TagBasedVideoManager.Domain

module Program =

    /// 指定されたファイルパスの親ディレクトリが存在しない場合は自動作成する (SQLite Error 14 起動失敗防止)
    let ensureParentDirectoryCreated (filePath: string) : unit =
        if not (String.IsNullOrWhiteSpace(filePath)) then
            let fullPath = Path.GetFullPath(filePath)
            let parentDir = Path.GetDirectoryName(fullPath)
            if not (String.IsNullOrEmpty(parentDir)) && not (Directory.Exists(parentDir)) then
                try Directory.CreateDirectory(parentDir) |> ignore with _ -> ()
    
    /// サービスの登録設定
    let configureServices (services : IServiceCollection) =
        // 1. IConfiguration / 環境変数からデータベースパスを取得
        services.AddTransient<System.Data.IDbConnection>(fun sp -> 
            let config = sp.GetService<IConfiguration>()
            let dbPath =
                let fromConfig = if not (isNull config) then config.["DATABASE_PATH"] else null
                if not (String.IsNullOrWhiteSpace(fromConfig)) then fromConfig
                else
                    let envPath = Environment.GetEnvironmentVariable("DATABASE_PATH")
                    if String.IsNullOrWhiteSpace(envPath) then "metadata.db" else envPath
            Db.getConnection dbPath
        ) |> ignore

        // 2. IMediaProcessor をシングルトンとして DI コンテナに登録 (停止ボタン連携および全ハンドラー共有)
        services.AddSingleton<IMediaProcessor, MediaProcessor>() |> ignore

        services.AddControllers() |> ignore
        services.AddGiraffe() |> ignore

    /// アプリケーションパイプラインの設定
    let configureApp (app : IApplicationBuilder) =
        // デフォルトファイル (index.html 等) の有効化
        app.UseDefaultFiles() |> ignore
        
        // 静的ファイル配信 (wwwroot) の有効化
        app.UseStaticFiles() |> ignore

        // サムネイル画像用の静的ファイルマウント配信
        let config = app.ApplicationServices.GetService<IConfiguration>()
        let thumbnailDir =
            let fromConfig = if not (isNull config) then config.["THUMBNAIL_DIR"] else null
            if not (String.IsNullOrWhiteSpace(fromConfig)) then fromConfig
            else
                let env = Environment.GetEnvironmentVariable("THUMBNAIL_DIR")
                if String.IsNullOrWhiteSpace(env) then "thumbnails" else env

        if not (String.IsNullOrEmpty(thumbnailDir)) then
            let fullThumbnailDir = Path.GetFullPath(thumbnailDir)
            if not (Directory.Exists(fullThumbnailDir)) then
                try Directory.CreateDirectory(fullThumbnailDir) |> ignore with _ -> ()
            if Directory.Exists(fullThumbnailDir) then
                let options = StaticFileOptions()
                options.FileProvider <- new PhysicalFileProvider(fullThumbnailDir)
                options.RequestPath <- PathString("/thumbnails")
                app.UseStaticFiles(options) |> ignore
        
        // Giraffe エラーハンドラーの適用
        app.UseGiraffeErrorHandler(fun ex logger -> HttpHandlers.giraffeErrorHandler ex logger) |> ignore
        
        // Giraffe ミドルウェアの適用
        app.UseGiraffe(HttpHandlers.webApp)

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        let config = builder.Configuration
        let envName = builder.Environment.EnvironmentName

        let dbPath =
            if not (String.IsNullOrWhiteSpace(config.["DATABASE_PATH"])) then config.["DATABASE_PATH"]
            else
                let env = Environment.GetEnvironmentVariable("DATABASE_PATH")
                if not (String.IsNullOrWhiteSpace(env)) then env else "metadata.db"

        let thumbnailDir =
            if not (String.IsNullOrWhiteSpace(config.["THUMBNAIL_DIR"])) then config.["THUMBNAIL_DIR"]
            else
                let env = Environment.GetEnvironmentVariable("THUMBNAIL_DIR")
                if not (String.IsNullOrWhiteSpace(env)) then env else "thumbnails"

        let port =
            if not (String.IsNullOrWhiteSpace(config.["PORT"])) then config.["PORT"]
            else
                let env = Environment.GetEnvironmentVariable("PORT")
                if not (String.IsNullOrWhiteSpace(env)) then env else "5620"

        // DATABASE_PATH の親ディレクトリ自動作成 (SQLite Error 14 起動失敗防止)
        ensureParentDirectoryCreated dbPath

        // THUMBNAIL_DIR 自動作成
        let fullThumbDir = Path.GetFullPath(thumbnailDir)
        if not (Directory.Exists(fullThumbDir)) then
            try Directory.CreateDirectory(fullThumbDir) |> ignore with _ -> ()

        // 起動時設定・VIDEO_DIRS 診断ログ出力
        let rawVideoDirs =
            if not (String.IsNullOrWhiteSpace(config.["VIDEO_DIRS"])) then config.["VIDEO_DIRS"]
            else
                let env = Environment.GetEnvironmentVariable("VIDEO_DIRS")
                if not (String.IsNullOrWhiteSpace(env)) then env else ""
        let videoDirList =
            match VideoDirs.parse rawVideoDirs with
            | Ok dirs -> dirs
            | Error _ -> []
        printfn "[Config] Environment=%s, Port=%s, ThumbnailDir=%s, DatabasePath=%s, VideoDirs(count=%d)=[%s]"
            envName port thumbnailDir dbPath videoDirList.Length (String.Join("; ", videoDirList))

        configureServices builder.Services
        let app = builder.Build()
        
        // 起動時にデータベースの自動初期化マイグレーションを実行
        use conn = Db.getConnection dbPath
        match DbInit.initializeDatabase conn with
        | Ok () -> printfn "Database initialized successfully."
        | Error err -> printfn "Database initialization failed: %s" err

        configureApp app
        
        let url = $"http://*:{port}"
        let version = HttpHandlers.getAppVersion ()
        printfn "TagBasedVideoManager v%s started on %s" version url

        app.Run(url)
        0
