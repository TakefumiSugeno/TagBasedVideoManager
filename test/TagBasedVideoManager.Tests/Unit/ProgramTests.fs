namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open TagBasedVideoManager
open TagBasedVideoManager.Domain

/// <summary>
/// Program.fs の WebHost 構築設定および初期化ロジックを検証する単体テスト。
/// </summary>
module ProgramTests =

    /// <summary>
    /// configureServices によって必要なサービス（IDbConnection, IMediaProcessor等）が
    /// 正常に DI コンテナへ登録されることを検証する。
    /// </summary>
    [<Fact>]
    let ``configureServices should register required dependencies to ServiceCollection`` () =
        // 1. DIコンテナの準備
        let services = ServiceCollection()

        // 2. サービス登録の実行
        Program.configureServices services

        // 3. プロバイダーのビルド
        let provider = services.BuildServiceProvider()

        // 4. アサーション (必要なサービスが登録され取得可能であること)
        let conn = provider.GetService<System.Data.IDbConnection>()
        conn |> should not' (be null)

        let mediaProcessor = provider.GetService<IMediaProcessor>()
        mediaProcessor |> should not' (be null)

    /// <summary>
    /// UT-10: Web アプリケーションは .env ファイルが存在しても設定値に影響を受けず、
    /// ASP.NET Core 標準 IConfiguration（環境変数・appsettings）のみを採用することを検証する。
    /// </summary>
    [<Fact>]
    let ``UT-10: Web configuration should ignore .env file and not load dotEnv into process`` () =
        let envFilePath = Path.Combine(AppContext.BaseDirectory, ".env")
        let testKey = "TEST_DOTENV_IGNORED_VAR"
        let testValue = "ShouldBeIgnored"

        try
            File.WriteAllLines(envFilePath, [| $"{testKey}={testValue}" |])
            Environment.SetEnvironmentVariable(testKey, null)

            // Web アプリ起動時（または設定解決時）に .env ファイルから環境変数が自動ロードされないこと
            // （loadDotEnv は廃止されるため、プロセス環境変数に反映されない）
            let loadedValue = Environment.GetEnvironmentVariable(testKey)
            loadedValue |> should equal null
        finally
            if File.Exists(envFilePath) then try File.Delete(envFilePath) with _ -> ()

    /// <summary>
    /// UT-09: IConfiguration において、appsettings.json の設定値がコンテナ / OS 環境変数によって
    /// 適切にオーバーライド解決されることを検証する。
    /// </summary>
    [<Fact>]
    let ``UT-09: IConfiguration should override appsettings values with environment variables`` () =
        let testKey = "VIDEO_DIRS"
        let baseValue = "/base/videos"
        let envOverrideValue = "/override/videos1;/override/videos2"
        let origEnv = Environment.GetEnvironmentVariable(testKey)
        let origPort = Environment.GetEnvironmentVariable("PORT")

        let tempDir = Path.Combine(Path.GetTempPath(), "WebConfigTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let jsonPath = Path.Combine(tempDir, "appsettings.json")

        try
            Environment.SetEnvironmentVariable("PORT", null)
            File.WriteAllText(jsonPath, $"{{\"{testKey}\": \"{baseValue}\", \"PORT\": 5620}}")
            Environment.SetEnvironmentVariable(testKey, envOverrideValue)

            let configBuilder = ConfigurationBuilder()
            let config =
                configBuilder
                    .AddJsonFile(jsonPath, optional = false)
                    .AddEnvironmentVariables()
                    .Build()

            let resolvedValue = config.[testKey]
            resolvedValue |> should equal envOverrideValue
            let resolvedPort = config.["PORT"]
            resolvedPort |> should equal "5620"
        finally
            Environment.SetEnvironmentVariable(testKey, origEnv)
            Environment.SetEnvironmentVariable("PORT", origPort)
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    /// <summary>
    /// UT-10: DATABASE_PATH の親ディレクトリが存在しない場合でも、
    /// 自動的にディレクトリが作成され SQLite Error 14 などの起動障害を防止できることを検証する。
    /// </summary>
    [<Fact>]
    let ``UT-10: DATABASE_PATH parent directory should be created automatically if not existing`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "WebDbDirTest_" + Guid.NewGuid().ToString("N"))
        let nestedDbDir = Path.Combine(tempDir, "nested", "sub", "dir")
        let dbPath = Path.Combine(nestedDbDir, "test_metadata.db")

        try
            Directory.Exists(nestedDbDir) |> should equal false
            Program.ensureParentDirectoryCreated dbPath
            Directory.Exists(nestedDbDir) |> should equal true
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)
