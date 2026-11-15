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

    /// IConfiguration または OS 環境変数から設定値を取得し、未設定の場合は defaultValue を返す
    let resolveConfigValue (config: IConfiguration) (key: string) (defaultValue: string) : string =
        let fromConfig = if not (isNull config) then config.[key] else null
        if not (String.IsNullOrWhiteSpace(fromConfig)) then fromConfig
        else
            let env = Environment.GetEnvironmentVariable(key)
            if not (String.IsNullOrWhiteSpace(env)) then env
            else defaultValue

    /// アプリケーション起動時の解決済み設定情報
    type StartupSettings = {
        DatabasePath: string
        ThumbnailDir: string
        Port: string
        RawVideoDirs: string
        VideoDirs: string list
    }

    /// IConfiguration から StartupSettings を解決する
    let resolveStartupSettings (config: IConfiguration) : StartupSettings =
        let dbPath = resolveConfigValue config "DATABASE_PATH" "metadata.db"
        let thumbDir = resolveConfigValue config "THUMBNAIL_DIR" "thumbnails"
        let port = resolveConfigValue config "PORT" "5620"
        let rawVideoDirs = resolveConfigValue config "VIDEO_DIRS" ""
        let videoDirList =
            match VideoDirs.parse rawVideoDirs with
            | Ok dirs -> dirs
            | Error _ -> []
        {
            DatabasePath = dbPath
            ThumbnailDir = thumbDir
            Port = port
            RawVideoDirs = rawVideoDirs
            VideoDirs = videoDirList
        }
    
    /// サービスの登録設定
    let configureServices (services : IServiceCollection) =
        // 1. IConfiguration / 環境変数からデータベースパスを取得
        services.AddTransient<System.Data.IDbConnection>(fun sp -> 
            let config = sp.GetService<IConfiguration>()
            let dbPath = resolveConfigValue config "DATABASE_PATH" "metadata.db"
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
        let thumbnailDir = resolveConfigValue config "THUMBNAIL_DIR" "thumbnails"

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

        let settings = resolveStartupSettings config

        // DATABASE_PATH の親ディレクトリ自動作成 (SQLite Error 14 起動失敗防止)
        ensureParentDirectoryCreated settings.DatabasePath

        // THUMBNAIL_DIR 自動作成
        let fullThumbDir = Path.GetFullPath(settings.ThumbnailDir)
        if not (Directory.Exists(fullThumbDir)) then
            try Directory.CreateDirectory(fullThumbDir) |> ignore with _ -> ()

        // 起動時設定・VIDEO_DIRS 診断ログ出力
        printfn "[Config] Environment=%s, Port=%s, ThumbnailDir=%s, DatabasePath=%s, VideoDirs(count=%d)=[%s]"
            envName settings.Port settings.ThumbnailDir settings.DatabasePath settings.VideoDirs.Length (String.Join("; ", settings.VideoDirs))

        configureServices builder.Services
        let app = builder.Build()
        
        // 起動時にデータベースの自動初期化マイグレーションを実行
        use conn = Db.getConnection settings.DatabasePath
        match DbInit.initializeDatabase conn with
        | Ok () -> printfn "Database initialized successfully."
        | Error err -> printfn "Database initialization failed: %s" err

        configureApp app
        
        let url = $"http://*:{settings.Port}"
        let version = HttpHandlers.getAppVersion ()
        printfn "TagBasedVideoManager v%s started on %s" version url

        app.Run(url)
        0
