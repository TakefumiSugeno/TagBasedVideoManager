namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open System.IO
open Microsoft.Extensions.DependencyInjection
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
    /// loadDotEnv が実行ファイルの配下やカレントディレクトリ等の .env ファイルを検知し、
    /// 環境変数を適切にプロセス内にロードできるかを検証する。
    /// </summary>
    [<Fact>]
    let ``loadDotEnv should load environment variables from env file`` () =
        // 1. テスト用の一時的な .env ファイルを AppContext.BaseDirectory 配下に作成
        let envFilePath = Path.Combine(AppContext.BaseDirectory, ".env")
        let testKey = "TEST_ENV_VAR_FOR_PROGRAM_TESTS"
        let testValue = "HelloWorld_ProgramTests"

        try
            // もし既存のファイルがあれば退避
            let originalContent = if File.Exists(envFilePath) then Some (File.ReadAllText(envFilePath)) else None
            File.WriteAllLines(envFilePath, [| $"{testKey}={testValue}" |])

            // 2. 環境変数ロード関数の呼び出し
            Program.loadDotEnv ()

            // 3. アサーション
            let loadedValue = Environment.GetEnvironmentVariable(testKey)
            loadedValue |> should equal testValue

            // 4. クリーニングと復元
            Environment.SetEnvironmentVariable(testKey, null)
            match originalContent with
            | Some content -> File.WriteAllText(envFilePath, content)
            | None -> if File.Exists(envFilePath) then File.Delete(envFilePath)
        with
        | ex -> 
            // 失敗時でも確実にクリーンアップ
            if File.Exists(envFilePath) then try File.Delete(envFilePath) with _ -> ()
            reraise ()
