namespace TagBasedVideoManager

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.AspNetCore.Http
open Giraffe
open TagBasedVideoManager.Infrastructure
open TagBasedVideoManager.Domain


module Program =
    
    /// サービスの登録設定
    let configureServices (services : IServiceCollection) =
        // 1. 環境変数からデータベースパスを取得 (デフォルト値の設定)
        let dbPath = 
            let envPath = Environment.GetEnvironmentVariable("DATABASE_PATH")
            if String.IsNullOrWhiteSpace(envPath) then "metadata.db" else envPath
        
        // 2. IDbConnection を DI コンテナに登録 (リクエストごとに接続を生成)
        services.AddTransient<System.Data.IDbConnection>(fun _ -> 
            Db.getConnection dbPath
        ) |> ignore

        // 3. IMediaProcessor をシングルトンとして DI コンテナに登録 (停止ボタン連携および全ハンドラー共有)
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
        let thumbnailDir = Environment.GetEnvironmentVariable("THUMBNAIL_DIR")
        if not (String.IsNullOrEmpty(thumbnailDir)) && Directory.Exists(thumbnailDir) then
            let options = StaticFileOptions()
            options.FileProvider <- new PhysicalFileProvider(Path.GetFullPath(thumbnailDir))
            options.RequestPath <- PathString("/thumbnails")
            app.UseStaticFiles(options) |> ignore
        
        // Giraffe エラーハンドラーの適用
        app.UseGiraffeErrorHandler(fun ex logger -> HttpHandlers.giraffeErrorHandler ex logger) |> ignore
        
        // Giraffe ミドルウェアの適用
        app.UseGiraffe(HttpHandlers.webApp)


    /// .env ファイルから環境変数をロードする簡易関数
    let loadDotEnv () =
        let paths = [
            Path.Combine(Directory.GetCurrentDirectory(), ".env")
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".env")
            Path.Combine(AppContext.BaseDirectory, ".env")
            Path.Combine(AppContext.BaseDirectory, "../../../.env")
            Path.Combine(AppContext.BaseDirectory, "../../../../.env")
        ]
        let foundPath = paths |> List.tryFind File.Exists
        match foundPath with
        | Some path ->
            try
                File.ReadAllLines(path)
                |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace(line)) && not (line.StartsWith("#")))
                |> Array.iter (fun line ->
                    let parts = line.Split('=', 2)
                    if parts.Length = 2 then
                        let key = parts.[0].Trim()
                        let value = parts.[1].Trim()
                        Environment.SetEnvironmentVariable(key, value)
                )
            with _ -> ()
        | None -> ()

    [<EntryPoint>]
    let main args =
        loadDotEnv ()
        let builder = WebApplication.CreateBuilder(args)
        
        configureServices builder.Services
        let app = builder.Build()
        
        // 起動時にデータベースの自動初期化マイグレーションを実行
        let dbPath = 
            let envPath = Environment.GetEnvironmentVariable("DATABASE_PATH")
            if String.IsNullOrWhiteSpace(envPath) then "metadata.db" else envPath
        use conn = Db.getConnection dbPath
        match DbInit.initializeDatabase conn with
        | Ok () -> printfn "Database initialized successfully."
        | Error err -> printfn "Database initialization failed: %s" err

        configureApp app
        
        let port = 
            let envPort = Environment.GetEnvironmentVariable("PORT")
            if String.IsNullOrWhiteSpace(envPort) then "5620" else envPort
        let url = $"http://*:{port}"
        
        app.Run(url)
        0
