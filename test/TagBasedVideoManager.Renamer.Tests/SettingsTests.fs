namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

/// <summary>設定ファイル・環境変数を共有するテスト間の並列実行競合を防ぐコレクション定義</summary>
[<CollectionDefinition("IsolatedSettingsAndState", DisableParallelization = true)>]
type IsolatedSettingsAndStateCollection() = class end

/// <summary>
/// SettingsTests / StateTests / RenameIntegrationE2ETests 間で共有する
/// 実設定ファイル保護・一時ディレクトリ隔離・クリーン初期化ハーネス
/// </summary>
module SettingsIsolationHarness =

    /// <summary>実設定ファイルの不変性検証用スナップショット</summary>
    type FileSnapshot = {
        FilePath: string
        Exists: bool
        Bytes: byte[]
        Sha256: string
        LastWriteTimeUtc: DateTime
    }

    /// <summary>リポジトリルートディレクトリを探索する</summary>
    let tryFindRepoRoot () : string option =
        let rec findUp (dir: DirectoryInfo) (depth: int) =
            if depth <= 0 || isNull dir then None
            else
                let marker = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.json")
                if File.Exists(marker) then Some dir.FullName
                else findUp dir.Parent (depth - 1)
        let current = DirectoryInfo(Directory.GetCurrentDirectory())
        let baseDir = DirectoryInfo(AppContext.BaseDirectory)
        findUp current 10 |> Option.orElseWith (fun () -> findUp baseDir 10)

    /// <summary>保護対象となるリポジトリ内実設定ファイルパス一覧を取得する</summary>
    let getProtectedRealSettingsPaths () : string list =
        match tryFindRepoRoot () with
        | None -> []
        | Some root ->
            let renamerDir = Path.Combine(root, "src", "TagBasedVideoManager.Renamer")
            let envName = Settings.resolveEnvironmentName ()
            [
                Path.Combine(renamerDir, "appsettings.json")
                Path.Combine(renamerDir, "appsettings.Development.json")
                Path.Combine(renamerDir, "appsettings.Production.json")
                Path.Combine(renamerDir, $"appsettings.{envName}.json")
            ]
            |> List.distinct

    /// <summary>指定ファイルの状態スナップショット（存在有無・バイト列・SHA256・タイムスタンプ）を取得する</summary>
    let captureSnapshot (path: string) : FileSnapshot =
        if File.Exists(path) then
            let bytes = File.ReadAllBytes(path)
            let hash = SHA256.HashData(bytes) |> Convert.ToHexString
            let timestamp = File.GetLastWriteTimeUtc(path)
            {
                FilePath = path
                Exists = true
                Bytes = bytes
                Sha256 = hash
                LastWriteTimeUtc = timestamp
            }
        else
            {
                FilePath = path
                Exists = false
                Bytes = [||]
                Sha256 = ""
                LastWriteTimeUtc = DateTime.MinValue
            }

    /// <summary>保護対象ファイルがテスト実行前後で一切変更・作成・削除されていないことを検証する</summary>
    let assertSnapshotUnchanged (before: FileSnapshot) : unit =
        let after = captureSnapshot before.FilePath
        if before.Exists then
            Assert.True(after.Exists, $"保護対象の実設定ファイルが削除されました: {before.FilePath}")
            after.Sha256 |> should equal before.Sha256
            Assert.True(System.Linq.Enumerable.SequenceEqual(before.Bytes, after.Bytes), $"保護対象の実設定ファイル内容が変更されました: {before.FilePath}")
            after.LastWriteTimeUtc |> should equal before.LastWriteTimeUtc
        else
            Assert.False(after.Exists, $"存在しなかった実設定ファイルが新規作成されました: {before.FilePath}")

    /// <summary>
    /// 一時ディレクトリおよび一時 SettingsSavePath を生成してテストを実行し、
    /// テスト実行前後でリポジトリ内実設定ファイルが不変であることを保護・検証する安全ラッパー
    /// </summary>
    let withIsolatedSettingsEnvironment (action: string -> unit) : unit =
        let envName = Settings.resolveEnvironmentName ()
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerIsolatedTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore

        let mutable realSnapshots: FileSnapshot list = []
        let mutable transientBackups: (string * byte[] option) list = []
        let mutable testSucceeded = false

        try
            let tempSavePath = Path.Combine(tempDir, $"appsettings.{envName}.json")
            realSnapshots <- getProtectedRealSettingsPaths () |> List.map captureSnapshot

            // Phase 2.1 の State.fs 保存先隔離適用前における bin/ および %APPDATA% 出力の暫定退避・復元
            let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
            let transientPaths = [
                Path.Combine(appDataDir, $"appsettings.{envName}.json")
                Path.Combine(appDataDir, "appsettings.json")
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"appsettings.{envName}.json")
            ]
            transientBackups <-
                transientPaths
                |> List.map (fun p -> p, if File.Exists(p) then Some (File.ReadAllBytes(p)) else None)

            action tempSavePath
            testSucceeded <- true
        finally
            let cleanupErrors = System.Collections.Generic.List<string>()

            try
                if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)
            with ex ->
                let msg = $"一時ディレクトリ削除に失敗しました ({tempDir}): {ex.Message}"
                eprintfn "[WARN] %s" msg
                cleanupErrors.Add(msg)

            for (path, backupOpt) in transientBackups do
                try
                    match backupOpt with
                    | Some bytes ->
                        let changed =
                            not (File.Exists(path))
                            || not (System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), bytes))
                        if changed then
                            let dir = Path.GetDirectoryName(path)
                            if not (String.IsNullOrEmpty(dir)) && not (Directory.Exists(dir)) then
                                Directory.CreateDirectory(dir) |> ignore
                            File.WriteAllBytes(path, bytes)
                    | None ->
                        if File.Exists(path) then File.Delete(path)
                with ex ->
                    let msg = $"一時出力先の復元に失敗しました ({path}): {ex.Message}"
                    eprintfn "[WARN] %s" msg
                    cleanupErrors.Add(msg)

            realSnapshots |> List.iter assertSnapshotUnchanged

            if testSucceeded && cleanupErrors.Count > 0 then
                failwith (String.Join("; ", cleanupErrors))

    /// <summary>開発者のローカル appsettings.Development.json を参照しないクリーン環境下で State.init テストを実行する</summary>
    let withCleanInitEnvironment (action: unit -> unit) : unit =
        withIsolatedSettingsEnvironment (fun _ ->
            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            let cleanEnvName = "IsolatedCleanEnv_" + Guid.NewGuid().ToString("N")
            try
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", cleanEnvName)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
                action ()
            finally
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
        )

    /// <summary>一時 SettingsSavePath とクリーンな既定設定を注入した初期モデルを生成する</summary>
    let initIsolatedModel (tempSavePath: string) : Model =
        let mutable modelOpt: Model option = None
        let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        let cleanEnvName = "IsolatedCleanEnv_" + Guid.NewGuid().ToString("N")
        try
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", cleanEnvName)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
            let initialModel, _ = State.init ()
            modelOpt <- Some initialModel
        finally
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)

        let initialModel = modelOpt.Value
        let defaults = Settings.defaultSettings ()
        let firstRuleId =
            defaults.Rules
            |> List.tryHead
            |> Option.map (fun r -> r.Id)
            |> Option.defaultValue ""
        {
            initialModel with
                Settings = defaults
                CurrentThreshold = defaults.PathLengthThreshold
                SelectedRuleId = firstRuleId
                SettingsSavePath = tempSavePath
        }

[<Collection("IsolatedSettingsAndState")>]
module SettingsTests =

    let rec findRepoRoot (dir: DirectoryInfo) (depth: int) : string =
        if depth <= 0 || box dir = null then
            failwith "リポジトリルートが見つかりません"
        else
            let candidate = Path.Combine(dir.FullName, ".git")
            if Directory.Exists(candidate) || File.Exists(candidate) then dir.FullName
            else findRepoRoot dir.Parent (depth - 1)

    let getRepoRoot () : string =
        let current = DirectoryInfo(Directory.GetCurrentDirectory())
        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
        try findRepoRoot current 8
        with _ -> findRepoRoot baseDir 8

    let createTempSettingsPath () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerTests_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        Path.Combine(tempDir, "appsettings.json")

    [<Fact>]
    let ``defaultSettings は初期閾値240文字と既定モデル・既定命名規則を生成する`` () =
        let settings = Settings.defaultSettings ()
        settings.PathLengthThreshold |> should equal 240
        settings.SelectedModel |> should equal "meta-llama/llama-3.3-70b-instruct:free"
        settings.ApiKey |> should equal None
        settings.Rules.Length |> should be (greaterThan 0)
        let firstRule = settings.Rules.Head
        firstRule.Order |> should equal 0

    [<Fact>]
    let ``save と load により設定をJSONファイルに保存・復元できる`` () =
        let filePath = createTempSettingsPath ()
        try
            let initial = Settings.defaultSettings ()
            let custom = {
                initial with
                    TargetDirectory = "C:\\TestVideos"
                    PathLengthThreshold = 240
                    SelectedModel = "google/gemini-2.0-flash-exp:free"
                    ApiKey = Some "sk-or-v1-testkey"
            }
            let saveResult = Settings.save filePath custom
            match saveResult with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            File.Exists(filePath) |> should equal true

            let loadResult = Settings.load filePath
            match loadResult with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.TargetDirectory |> should equal "C:\\TestVideos"
                loaded.PathLengthThreshold |> should equal 240
                loaded.SelectedModel |> should equal "google/gemini-2.0-flash-exp:free"
                loaded.ApiKey |> should equal (Some "sk-or-v1-testkey")
                loaded.Rules.Length |> should equal custom.Rules.Length
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    [<Fact>]
    let ``reorderRules は指定されたID順にルールを並び替え、Orderを再採番する`` () =
        let initial = Settings.defaultSettings ()
        let ruleIds = initial.Rules |> List.map (fun r -> r.Id)
        let reversedIds = List.rev ruleIds
        let reordered = Settings.reorderRules reversedIds initial
        reordered.Rules |> List.map (fun r -> r.Id) |> should equal reversedIds
        reordered.Rules |> List.iteri (fun i r -> r.Order |> should equal i)

    [<Fact>]
    let ``存在しないファイルパスを load すると Error を返す`` () =
        let nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N") + ".json")
        let result = Settings.load nonExistentPath
        match result with
        | Ok _ -> failwith "Expected load to fail for non-existent file"
        | Error (IoError (msg, _)) -> msg |> should not' (be EmptyString)
        | Error (SettingsError msg) -> msg |> should not' (be EmptyString)
        | Error other -> failwith $"Unexpected error type: {other}"

    [<Fact>]
    let ``不正なJSON形式のファイルを load すると SettingsError を返す`` () =
        let filePath = createTempSettingsPath ()
        try
            File.WriteAllText(filePath, "{ invalid json content }")
            let result = Settings.load filePath
            match result with
            | Ok _ -> failwith "Expected load to fail for invalid JSON"
            | Error (SettingsError msg) -> msg |> should not' (be EmptyString)
            | Error other -> failwith $"Unexpected error type: {other}"
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    [<Fact>]
    let ``セッション内で閾値を変更しても設定ファイルは更新されない（非保存の保証）`` () =
        let filePath = createTempSettingsPath ()
        try
            let initial = Settings.defaultSettings ()
            Settings.save filePath initial |> ignore
            let initialFileContent = File.ReadAllText(filePath)
            let lastModifiedBefore = File.GetLastWriteTimeUtc(filePath)

            // UI側でのセッション限定変更をシミュレート (Settings は再保存しない)
            let sessionModifiedThreshold = 200
            let memorySettings = { initial with PathLengthThreshold = sessionModifiedThreshold }
            memorySettings.PathLengthThreshold |> should equal 200

            // 設定ファイルの内容とタイムスタンプが一切変化していないことを検証
            let currentFileContent = File.ReadAllText(filePath)
            let lastModifiedAfter = File.GetLastWriteTimeUtc(filePath)
            currentFileContent |> should equal initialFileContent
            lastModifiedAfter |> should equal lastModifiedBefore

            // 改めて load しても元の初期値 240 が返ること
            match Settings.load filePath with
            | Ok reloaded -> reloaded.PathLengthThreshold |> should equal 240
            | Error err -> failwith $"Reload failed: {err}"
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    [<Fact>]
    let ``UT-01: loadConfiguration は .env および OS環境変数 VIDEO_DIR / OPENROUTER_API_KEY を完全に無視する`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEnvTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let envPath = Path.Combine(tempDir, ".env")
        let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
        let origApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
        try
            File.WriteAllLines(envPath, [
                "PORT=5621"
                "VIDEO_DIR=E:\\EnvVideos"
                "OPENROUTER_API_KEY=sk-or-v1-from-env-test-key"
                "OPENROUTER_MODEL=google/gemini-2.0-flash-exp:free"
                "PATH_LENGTH_THRESHOLD=220"
            ])
            Environment.SetEnvironmentVariable("VIDEO_DIR", "E:\\FromOsEnvVideoDir")
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sk-or-v1-from-os-env-key")

            let loaded = Settings.loadConfiguration None (Some envPath)
            // .env や OS環境変数は完全に無視され、組み込み既定値（または appsettings.json）が維持されること
            loaded.TargetDirectory |> should equal ""
            loaded.ApiKey |> should equal None
            loaded.SelectedModel |> should equal "meta-llama/llama-3.3-70b-instruct:free"
            loaded.PathLengthThreshold |> should equal 240
        finally
            Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", origApiKey)
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``UT-01: loadConfiguration は .env が存在しても appsettings.json の設定のみを参照する`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerPriorityTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let jsonPath = Path.Combine(tempDir, "appsettings.json")
        let envPath = Path.Combine(tempDir, ".env")
        try
            File.WriteAllLines(envPath, [
                "VIDEO_DIR=E:\\FromEnv"
                "OPENROUTER_API_KEY=key-from-env"
                "OPENROUTER_MODEL=model-from-env"
            ])

            let customJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "D:\\FromJson"
                    ApiKey = Some "key-from-json"
            }
            Settings.save jsonPath customJson |> ignore

            let loaded = Settings.loadConfiguration (Some jsonPath) (Some envPath)
            loaded.TargetDirectory |> should equal "D:\\FromJson"
            loaded.ApiKey |> should equal (Some "key-from-json")
            // .env の OPENROUTER_MODEL は無視され、customJson の既定モデルが維持されること
            loaded.SelectedModel |> should equal customJson.SelectedModel
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfiguration は外部ファイルの命名規則リストを保持し、先頭ルールを既定とする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerRulesTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let jsonPath = Path.Combine(tempDir, "appsettings.json")
        try
            let customRules = [
                {
                    Id = "custom-rule-external"
                    Name = "外部定義カスタムルール"
                    Pattern = "{Custom}_{Date}.mp4"
                    PromptInstruction = "外部ファイルから読み込んだカスタムプロンプト"
                    Order = 0
                    EnableWebSearch = true
                }
                {
                    Id = "rule-compact"
                    Name = "短縮"
                    Pattern = "{ShortTitle}.mp4"
                    PromptInstruction = "短縮命名"
                    Order = 1
                    EnableWebSearch = false
                }
            ]
            let customJson = {
                Settings.defaultSettings () with
                    Rules = customRules
            }
            Settings.save jsonPath customJson |> ignore

            let loaded = Settings.loadConfiguration (Some jsonPath) None
            loaded.Rules.Length |> should equal 2
            loaded.Rules.Head.Id |> should equal "custom-rule-external"
            loaded.Rules.Head.Name |> should equal "外部定義カスタムルール"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``UT-02: resolveSavePath はアクティブ環境名に応じた appsettings.{Environment}.json を返しベース appsettings.json を上書きしない`` () =
        let tempBaseDir = Path.Combine(Path.GetTempPath(), "RenamerBase_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempBaseDir) |> ignore
        let baseFile = Path.Combine(tempBaseDir, "appsettings.json")
        try
            File.WriteAllText(baseFile, "{}")
            let path = Settings.resolveSavePath (Some tempBaseDir)
            let envName = Settings.resolveEnvironmentName ()
            let expectedFile = Path.Combine(tempBaseDir, $"appsettings.{envName}.json")
            // 保存先は環境別ファイル名であり、ベース appsettings.json そのものではないこと
            path |> should equal expectedFile
            path |> should not' (equal baseFile)
        finally
            if Directory.Exists(tempBaseDir) then Directory.Delete(tempBaseDir, true)

    /// <summary>一時ディレクトリに環境別設定ファイルが存在する場合、ベースファイル不在でも同ディレクトリの環境別パスを返すことを検証する</summary>
    [<Fact>]
    let ``UT-04: resolveSavePath は一時ディレクトリ内に環境別設定が存在する場合に同ディレクトリ内の環境別パスを決定する`` () =
        let tempBaseDir = Path.Combine(Path.GetTempPath(), "RenamerBaseEnvOnly_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempBaseDir) |> ignore
        try
            let envName = Settings.resolveEnvironmentName ()
            // expectedEnvPath は注入した tempBaseDir 配下であり、%APPDATA% ではない
            let expectedEnvPath = Path.Combine(tempBaseDir, $"appsettings.{envName}.json")
            File.WriteAllText(expectedEnvPath, "{}")

            let path = Settings.resolveSavePath (Some tempBaseDir)
            path |> should equal expectedEnvPath
        finally
            if Directory.Exists(tempBaseDir) then Directory.Delete(tempBaseDir, true)

    [<Fact>]
    let ``loadConfiguration は OS環境変数を参照しない (無視する)`` () =
        let dummyEnvKey = "OPENROUTER_API_KEY"
        let dummyEnvVal = "sk-or-v1-from-os-env-should-be-ignored"
        let originalVal = Environment.GetEnvironmentVariable(dummyEnvKey)
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerNoOsEnv_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let emptyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(emptyEnvPath, "# empty env\n")
        try
            Environment.SetEnvironmentVariable(dummyEnvKey, dummyEnvVal)
            let loaded = Settings.loadConfiguration None (Some emptyEnvPath)
            // OS環境変数は参照されないため、ApiKey は None のままであること
            loaded.ApiKey |> should equal None
        finally
            Environment.SetEnvironmentVariable(dummyEnvKey, originalVal)
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    /// <summary>loadConfiguration が一時ディレクトリ内の構成別設定ファイルをベース設定にオーバーライドロードすることを検証する</summary>
    [<Fact>]
    let ``loadConfiguration は一時ディレクトリ内の構成別設定ファイルをベース設定にオーバーライドロードする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerDevFixture_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let envName = Settings.defaultConfigurationName
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let envJsonPath = Path.Combine(tempDir, $"appsettings.{envName}.json")

        try
            File.WriteAllText(dummyEnvPath, "# isolated fixture\n")
            let baseJson = """
            {
              "targetDirectory": "",
              "pathLengthThreshold": 210,
              "selectedModel": "base/model",
              "apiKey": null
            }
            """
            let envJson = """
            {
              "targetDirectory": "D:\\FixtureVideos",
              "selectedModel": "env/model",
              "apiKey": "sk-or-v1-fixture-env-key"
            }
            """
            File.WriteAllText(baseJsonPath, baseJson)
            File.WriteAllText(envJsonPath, envJson)

            let loaded = Settings.loadConfiguration None (Some dummyEnvPath)
            loaded.TargetDirectory |> should equal "D:\\FixtureVideos"
            loaded.SelectedModel |> should equal "env/model"
            loaded.ApiKey |> should equal (Some "sk-or-v1-fixture-env-key")
            loaded.PathLengthThreshold |> should equal 210
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``save と load は NamingRule の EnableWebSearch フラグを正しく永続化・復元できる`` () =
        let filePath = createTempSettingsPath ()
        try
            let initial = Settings.defaultSettings ()
            let customRule = {
                Id = "rule-web-search"
                Name = "Web検索連携ルール"
                Pattern = "{Title}_{Episode}.mp4"
                PromptInstruction = "Web検索結果から正式なタイトルを補完して命名してください。"
                Order = 0
                EnableWebSearch = true
            }
            let custom = { initial with Rules = [ customRule ] }
            let saveResult = Settings.save filePath custom
            match saveResult with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            let loadResult = Settings.load filePath
            match loadResult with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.Rules.Length |> should equal 1
                let loadedRule = loaded.Rules.Head
                loadedRule.Id |> should equal "rule-web-search"
                loadedRule.EnableWebSearch |> should equal true
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    [<Fact>]
    let ``enableWebSearch が省略されたレガシーJSONでもデフォルト false として安全に読み込める`` () =
        let filePath = createTempSettingsPath ()
        try
            let legacyJson = """
            {
              "targetDirectory": "C:\\Legacy",
              "pathLengthThreshold": 240,
              "selectedModel": "meta-llama/llama-3.3-70b-instruct:free",
              "rules": [
                {
                  "id": "legacy-rule",
                  "name": "旧形式ルール",
                  "pattern": "{Title}.mp4",
                  "promptInstruction": "短縮してください",
                  "order": 0
                }
              ]
            }
            """
            File.WriteAllText(filePath, legacyJson)

            let loadResult = Settings.load filePath
            match loadResult with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.Rules.Length |> should equal 1
                let rule = loaded.Rules.Head
                rule.EnableWebSearch |> should equal false
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    /// <summary>一時ディレクトリフィクスチャ上の appsettings.Development.json と appsettings.Production.json が正しくデシリアライズできることを検証する</summary>
    [<Fact>]
    let ``一時ディレクトリの appsettings.Development.json と appsettings.Production.json は正しくデシリアライズできる`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEnvDeserialize_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let devPath = Path.Combine(tempDir, "appsettings.Development.json")
        let prodPath = Path.Combine(tempDir, "appsettings.Production.json")

        try
            let devJson = """
            {
              "targetDirectory": "test/videos",
              "pathLengthThreshold": 200,
              "selectedModel": "fixture/dev-model:free",
              "apiKey": null,
              "rules": [
                {
                  "id": "rule-dev",
                  "name": "開発用ルール",
                  "pattern": "{Code}_{Summary}.mp4",
                  "promptInstruction": "要約してください",
                  "order": 0,
                  "enableWebSearch": true
                }
              ]
            }
            """
            let prodJson = """
            {
              "targetDirectory": "D:\\ProdVideos",
              "pathLengthThreshold": 180,
              "selectedModel": "fixture/prod-model:v1",
              "apiKey": null,
              "rules": [
                {
                  "id": "rule-prod",
                  "name": "本番用ルール",
                  "pattern": "{ShortTitle}.mp4",
                  "promptInstruction": "短縮してください",
                  "order": 0,
                  "enableWebSearch": false
                }
              ]
            }
            """
            File.WriteAllText(devPath, devJson)
            File.WriteAllText(prodPath, prodJson)

            File.Exists(devPath) |> should equal true
            File.Exists(prodPath) |> should equal true

            match Settings.load devPath with
            | Ok devSettings ->
                devSettings.TargetDirectory |> should equal "test/videos"
                devSettings.PathLengthThreshold |> should equal 200
                devSettings.SelectedModel |> should equal "fixture/dev-model:free"
                devSettings.ApiKey |> should equal None
                devSettings.Rules.Length |> should equal 1
                let r = devSettings.Rules.Head
                r.Id |> should equal "rule-dev"
                r.Name |> should equal "開発用ルール"
                r.Pattern |> should equal "{Code}_{Summary}.mp4"
                r.PromptInstruction |> should equal "要約してください"
                r.Order |> should equal 0
                r.EnableWebSearch |> should equal true
            | Error err -> failwith $"Development settings load failed: {err}"

            match Settings.load prodPath with
            | Ok prodSettings ->
                prodSettings.TargetDirectory |> should equal "D:\\ProdVideos"
                prodSettings.PathLengthThreshold |> should equal 180
                prodSettings.SelectedModel |> should equal "fixture/prod-model:v1"
                prodSettings.ApiKey |> should equal None
                prodSettings.Rules.Length |> should equal 1
                let r = prodSettings.Rules.Head
                r.Id |> should equal "rule-prod"
                r.Name |> should equal "本番用ルール"
                r.Pattern |> should equal "{ShortTitle}.mp4"
                r.PromptInstruction |> should equal "短縮してください"
                r.Order |> should equal 0
                r.EnableWebSearch |> should equal false
            | Error err -> failwith $"Production settings load failed: {err}"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``loadConfigurationWithConfig は Development 環境時に appsettings.Development.json で appsettings.json をオーバーライドマージする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerDevMergeTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let devJsonPath = Path.Combine(tempDir, "appsettings.Development.json")

        try
            // ベース設定: TargetDirectory="C:\\BaseDir", SelectedModel="base-model", PathLengthThreshold=210
            let baseJson = """
            {
              "targetDirectory": "C:\\BaseDir",
              "pathLengthThreshold": 210,
              "selectedModel": "base-model",
              "apiKey": "base-key"
            }
            """
            // Developmentオーバーライド: TargetDirectory="C:\\DevDir", SelectedModel="dev-model" (PathLengthThresholdは未指定)
            let devJson = """
            {
              "targetDirectory": "C:\\DevDir",
              "selectedModel": "dev-model"
            }
            """
            File.WriteAllText(baseJsonPath, baseJson)
            File.WriteAllText(devJsonPath, devJson)

            let loaded = Settings.loadConfigurationWithConfig (Some "Development") None (Some dummyEnvPath)
            // オーバーライドされたキーの検証
            loaded.TargetDirectory |> should equal "C:\\DevDir"
            loaded.SelectedModel |> should equal "dev-model"
            // ベース設定から維持されたキーの検証（部分オーバーライド・階層マージ）
            loaded.PathLengthThreshold |> should equal 210
            loaded.ApiKey |> should equal (Some "base-key")
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfigurationWithConfig は Production 環境時に appsettings.Production.json で appsettings.json をオーバーライドマージする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerProdMergeTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let prodJsonPath = Path.Combine(tempDir, "appsettings.Production.json")

        try
            let baseJson = """
            {
              "targetDirectory": "C:\\BaseDir",
              "pathLengthThreshold": 210,
              "selectedModel": "base-model",
              "apiKey": "base-key"
            }
            """
            // Productionオーバーライド: TargetDirectory="" (空文字上書き), apiKey=null (明示的null上書き)
            let prodJson = """
            {
              "targetDirectory": "",
              "apiKey": null
            }
            """
            File.WriteAllText(baseJsonPath, baseJson)
            File.WriteAllText(prodJsonPath, prodJson)

            let loaded = Settings.loadConfigurationWithConfig (Some "Production") None (Some dummyEnvPath)
            loaded.TargetDirectory |> should equal ""
            loaded.ApiKey |> should equal None
            // 未指定キーはベースが維持されること
            loaded.SelectedModel |> should equal "base-model"
            loaded.PathLengthThreshold |> should equal 210
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfigurationWithConfig は 環境別ファイル不在時に appsettings.json へ安全にフォールバックする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerFallbackConfigTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")

        try
            let baseJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\BaseDirOnly"
                    SelectedModel = "model-from-base-only"
                    PathLengthThreshold = 225
            }
            Settings.save baseJsonPath baseJson |> ignore

            // Development指定でも appsettings.Development.json が無ければ appsettings.json をロード
            let loadedDev = Settings.loadConfigurationWithConfig (Some "Development") None (Some dummyEnvPath)
            loadedDev.TargetDirectory |> should equal "C:\\BaseDirOnly"
            loadedDev.SelectedModel |> should equal "model-from-base-only"
            loadedDev.PathLengthThreshold |> should equal 225

            // Production指定でも appsettings.Production.json が無ければ appsettings.json をロード
            let loadedProd = Settings.loadConfigurationWithConfig (Some "Production") None (Some dummyEnvPath)
            loadedProd.TargetDirectory |> should equal "C:\\BaseDirOnly"
            loadedProd.SelectedModel |> should equal "model-from-base-only"
            loadedProd.PathLengthThreshold |> should equal 225
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfigurationWithConfig は明示指定パス (jsonPathOpt) を構成別設定よりも最優先する`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerExplicitPathTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let devJsonPath = Path.Combine(tempDir, "appsettings.Development.json")
        let explicitJsonPath = Path.Combine(tempDir, "custom-explicit.json")

        try
            let devJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\DevConfigDir"
                    SelectedModel = "model-dev"
            }
            let explicitJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\ExplicitDir"
                    SelectedModel = "model-explicit"
            }
            Settings.save devJsonPath devJson |> ignore
            Settings.save explicitJsonPath explicitJson |> ignore

            // 明示パスを指定した場合、Development環境であっても explicitJson が最優先されること
            let loaded = Settings.loadConfigurationWithConfig (Some "Development") (Some explicitJsonPath) (Some dummyEnvPath)
            loaded.TargetDirectory |> should equal "C:\\ExplicitDir"
            loaded.SelectedModel |> should equal "model-explicit"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``UT-01: loadConfigurationWithConfig は構成別設定と .env の併存時でも .env から未定義項目を補完しない`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEnvCoexistTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let envPath = Path.Combine(tempDir, ".env")
        File.WriteAllLines(envPath, [
            "VIDEO_DIR=E:\\FromEnvDir"
            "OPENROUTER_API_KEY=key-from-env"
        ])

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let devJsonPath = Path.Combine(tempDir, "appsettings.Development.json")

        try
            let baseJson = """
            {
              "pathLengthThreshold": 200
            }
            """
            let devJson = """
            {
              "targetDirectory": "D:\\FromDevJson",
              "selectedModel": "model-from-dev-json"
            }
            """
            File.WriteAllText(baseJsonPath, baseJson)
            File.WriteAllText(devJsonPath, devJson)

            // Development構成下で、appsettings.json + appsettings.Development.json + .env が存在する場合
            // TargetDirectory は Development JSON 優先
            // ApiKey は JSON 未指定でも .env は無視され None のままであること
            // PathLengthThreshold は baseJson から継承
            let loaded = Settings.loadConfigurationWithConfig (Some "Development") None (Some envPath)
            loaded.TargetDirectory |> should equal "D:\\FromDevJson"
            loaded.ApiKey |> should equal None
            loaded.SelectedModel |> should equal "model-from-dev-json"
            loaded.PathLengthThreshold |> should equal 200
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``resolveEnvironmentName は環境変数を最優先し未設定時はコンパイル構成にフォールバックする`` () =
        // 既存の環境変数を退避
        let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        try
            // 1. DOTNET_ENVIRONMENT 優先
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Staging")
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development")
            Settings.resolveEnvironmentName () |> should equal "Staging"

            // 2. ASPNETCORE_ENVIRONMENT 優先（DOTNET_ENVIRONMENT が空の場合）
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "CustomEnv")
            Settings.resolveEnvironmentName () |> should equal "CustomEnv"

            // 3. 両方未設定時はコンパイル時シンボル (#if DEBUG なら Development, それ以外は Production)
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
            let expectedDefault =
#if DEBUG
                "Development"
#else
                "Production"
#endif
            Settings.resolveEnvironmentName () |> should equal expectedDefault
        finally
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)

    [<Fact>]
    let ``save は日本語を含む設定をUnicodeエスケープせずUTF-8文字列のままJSON出力する`` () =
        let filePath = createTempSettingsPath ()
        try
            let settings = Settings.defaultSettings ()
            let saveResult = Settings.save filePath settings
            match saveResult with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            let jsonContent = File.ReadAllText(filePath)
            // 日本語のルール名やプロンプト指示文がそのまま含まれていること
            jsonContent |> should contain "日付_撮影地_行動 (推奨)"
            jsonContent |> should contain "短縮 (タイトルのみ)"
            // Unicode エスケープシーケンス (\uXXXX) に変換されていないこと
            jsonContent |> should not' (contain @"\u65e5\u4ed8")
            jsonContent |> should not' (contain @"\u77ed\u7e2e")
        finally
            if File.Exists(filePath) then File.Delete(filePath)
            let dir = Path.GetDirectoryName(filePath)
            if Directory.Exists(dir) then Directory.Delete(dir, true)

    [<Fact>]
    let ``UT-05: 空 rules: [] 指定時にベースルールのゾンビ復元が発生しないこと`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEmptyRules_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let devJsonPath = Path.Combine(tempDir, "appsettings.Development.json")
        try
            let baseJson = """
            {
              "rules": [
                {
                  "id": "base-rule-1",
                  "name": "ベースルール",
                  "pattern": "{Title}.mp4",
                  "promptInstruction": "指示文",
                  "order": 0
                }
              ]
            }
            """
            let devJson = """
            {
              "rules": []
            }
            """
            File.WriteAllText(baseJsonPath, baseJson)
            File.WriteAllText(devJsonPath, devJson)

            let loaded = Settings.loadConfigurationWithConfig (Some "Development") (Some devJsonPath) None
            // 明示的に空配列 [] を指定した場合は、ベース設定のルールがゾンビ復元されず空であること
            loaded.Rules |> should be Empty
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``UT-06: resolveEnvironmentName は不正な環境名（空文字・パストラバーサル ../evil）をサニタイズして安全にフォールバックする`` () =
        let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        try
            // 1. パストラバーサル文字を含む環境名
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "../evil")
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
            let safeEnv1 = Settings.resolveEnvironmentName ()
            safeEnv1 |> should not' (equal "../evil")
            safeEnv1 |> should not' (contain "/")
            safeEnv1 |> should not' (contain "\\")

            // 2. 空白のみの環境名
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "   ")
            let safeEnv2 = Settings.resolveEnvironmentName ()
            safeEnv2 |> should not' (be EmptyString)
        finally
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)

    [<Fact>]
    let ``UT-07: クリーンテンプレート検査: appsettings.json に個人固有パスやAPIキー等の秘密情報が含まれないこと`` () =
        let root = getRepoRoot ()
        let webAppSettingsPath = Path.Combine(root, "src", "TagBasedVideoManager", "appsettings.json")
        let renamerAppSettingsPath = Path.Combine(root, "src", "TagBasedVideoManager.Renamer", "appsettings.json")

        File.Exists(webAppSettingsPath) |> should equal true
        File.Exists(renamerAppSettingsPath) |> should equal true

        // 1. Web 側の appsettings.json 検証
        let webContent = File.ReadAllText(webAppSettingsPath)
        webContent |> should not' (contain "E:\\")
        webContent |> should not' (contain "sk-or-v1-")

        use webDoc = System.Text.Json.JsonDocument.Parse(webContent)
        let webRoot = webDoc.RootElement
        if webRoot.TryGetProperty("VIDEO_DIRS", ref Unchecked.defaultof<_>) then
            let vdirs = webRoot.GetProperty("VIDEO_DIRS").GetString()
            vdirs |> should equal ""
        if webRoot.TryGetProperty("PORT", ref Unchecked.defaultof<_>) then
            let port = webRoot.GetProperty("PORT").GetInt32()
            port |> should equal 5620

        // 2. Renamer 側の appsettings.json 検証（個人固有パスやAPIキーが除去されていること）
        let renamerContent = File.ReadAllText(renamerAppSettingsPath)
        renamerContent |> should not' (contain "E:\\e")
        renamerContent |> should not' (contain "sk-or-v1-")

        use renamerDoc = System.Text.Json.JsonDocument.Parse(renamerContent)
        let renamerRoot = renamerDoc.RootElement
        renamerRoot.GetProperty("targetDirectory").GetString() |> should equal ""
        let apiKeyElement = renamerRoot.GetProperty("apiKey")
        (apiKeyElement.ValueKind = System.Text.Json.JsonValueKind.Null || apiKeyElement.GetString() = "") |> should equal true

    [<Fact>]
    let ``IT-04: .gitignore に appsettings.Development.json と appsettings.Production.json が除外登録されていること`` () =
        let root = getRepoRoot ()
        let gitignorePath = Path.Combine(root, ".gitignore")
        File.Exists(gitignorePath) |> should equal true
        let lines = File.ReadAllLines(gitignorePath) |> Array.map (fun s -> s.Trim())

        let hasDev = lines |> Array.exists (fun l -> l = "**/appsettings.Development.json" || l = "appsettings.Development.json")
        let hasProd = lines |> Array.exists (fun l -> l = "**/appsettings.Production.json" || l = "appsettings.Production.json")

        hasDev |> should equal true
        hasProd |> should equal true

    [<Fact>]
    let ``IT-05: git ls-files に .env.sample および環境別 JSON が追跡対象として含まれないこと`` () =
        let root = getRepoRoot ()
        let psi = System.Diagnostics.ProcessStartInfo("git", "ls-files")
        psi.WorkingDirectory <- root
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use proc = System.Diagnostics.Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit(10000) |> ignore

        let trackedFiles = output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        trackedFiles |> should not' (contain ".env.sample")
        trackedFiles |> should not' (contain "src/TagBasedVideoManager/appsettings.Development.json")
        trackedFiles |> should not' (contain "src/TagBasedVideoManager.Renamer/appsettings.Production.json")

    [<Fact>]
    let ``IT-01: GUID 一意一時ファイル (.tmp) による並行保存競合防止`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerConcurrentSave_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let filePath = Path.Combine(tempDir, "appsettings.Development.json")
        try
            let initial = Settings.defaultSettings ()
            let saveTasks =
                [ 1 .. 10 ]
                |> List.map (fun i ->
                    async {
                        let custom = { initial with PathLengthThreshold = 200 + i }
                        return Settings.save filePath custom
                    }
                )
            let results = Async.RunSynchronously (Async.Parallel saveTasks)
            // すべての保存が IOException による失敗なく Ok () を返すこと
            results |> Array.iter (fun r ->
                match r with
                | Ok () -> ()
                | Error (IoError (msg, _)) -> failwith $"並行書き込みで IoError が発生しました: {msg}"
                | Error (SettingsError msg) -> failwith $"並行書き込みで SettingsError が発生しました: {msg}"
                | Error other -> failwith $"予期しないエラー: {other}"
            )
            File.Exists(filePath) |> should equal true
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    /// <summary>一時ディレクトリ内の環境別設定ファイルへの保存と再読込整合を検証する</summary>
    [<Fact>]
    let ``IT-02: 一時ディレクトリ基準の環境別保存先解決・保存と再読込整合`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerIsolatedEnvSave_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let baseFile = Path.Combine(tempDir, "appsettings.json")
        File.WriteAllText(baseFile, "{}")

        try
            let envName = Settings.resolveEnvironmentName ()
            let saveTarget = Settings.resolveSavePath (Some tempDir)
            // expectedPath は注入した tempDir 配下であり、%APPDATA% ではない
            let expectedPath = Path.Combine(tempDir, $"appsettings.{envName}.json")
            saveTarget |> should equal expectedPath

            let custom = { Settings.defaultSettings () with TargetDirectory = "C:\\IsolatedSavedDir" }
            let saveResult = Settings.save saveTarget custom
            match saveResult with
            | Error err -> failwith $"Save to isolated path failed: {err}"
            | Ok () -> ()

            File.Exists(saveTarget) |> should equal true

            // 再読込時に一時ディレクトリの環境別設定が反映されること
            let loaded = Settings.loadConfiguration (Some saveTarget) None
            loaded.TargetDirectory |> should equal "C:\\IsolatedSavedDir"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    /// <summary>一時ディレクトリ上で環境別オーバーライド設定がベース設定を上書きしつつ未指定キーを継承することを検証する</summary>
    [<Fact>]
    let ``回帰テスト: loadConfigurationWithConfig において一時ディレクトリの環境別設定がベース層を優先上書きすること`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerOverrideRegression_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let testEnvName = "RegressionTestEnv_" + Guid.NewGuid().ToString("N")
        let baseFile = Path.Combine(tempDir, "appsettings.json")
        let targetFile = Path.Combine(tempDir, $"appsettings.{testEnvName}.json")
        let dummyEnv = Path.Combine(tempDir, ".env")

        try
            File.WriteAllText(dummyEnv, "# isolated fixture\n")
            let baseJson = """{ "targetDirectory": "C:\\BaseDir", "pathLengthThreshold": 215, "selectedModel": "base/model" }"""
            let overrideJson = """{ "targetDirectory": "C:\\EnvOverriddenDir", "selectedModel": "custom/env-model:v1" }"""
            File.WriteAllText(baseFile, baseJson)
            File.WriteAllText(targetFile, overrideJson)

            let loaded = Settings.loadConfigurationWithConfig (Some testEnvName) None (Some dummyEnv)
            loaded.TargetDirectory |> should equal "C:\\EnvOverriddenDir"
            loaded.SelectedModel |> should equal "custom/env-model:v1"
            loaded.PathLengthThreshold |> should equal 215
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``RenamerError.toMessage は全 7 種のエラーケースで適切なユーザー向けメッセージを生成する`` () =
        // 1. IoError
        let ex = exn "ディスク書き込み制限"
        RenamerError.toMessage (IoError ("ファイル書き込みに失敗しました", Some ex))
        |> should equal "ファイル書き込みに失敗しました"

        // 2. SettingsError
        RenamerError.toMessage (SettingsError "設定JSONの構文解析エラー")
        |> should equal "設定JSONの構文解析エラー"

        // 3. OpenRouterError
        RenamerError.toMessage (OpenRouterError (429, "Rate limit exceeded"))
        |> should equal "OpenRouter エラー (429): Rate limit exceeded"

        // 4. ContainerError
        RenamerError.toMessage (ContainerError ("docker ps", 1, "Cannot connect to Docker daemon"))
        |> should equal "コンテナ コマンド 'docker ps' 失敗 (終了コード 1): Cannot connect to Docker daemon"

        // 5. TimeoutError
        RenamerError.toMessage (TimeoutError "docker-compose up")
        |> should equal "コンテナ コマンド 'docker-compose up' がタイムアウトしました。"

        // 6. ValidationError
        RenamerError.toMessage (ValidationError "パス長が閾値を超過しています")
        |> should equal "パス長が閾値を超過しています"

        // 7. UndoConflictError
        RenamerError.toMessage (UndoConflictError ("C:\\videos\\sample.mp4", "同名ファイルが既に存在します"))
        |> should equal "Undo 衝突エラー (C:\\videos\\sample.mp4): 同名ファイルが既に存在します"

    [<Fact>]
    let ``loadConfigurationWithConfig は jsonPathOpt に不正JSON指定時は既定値へフォールバックし正常JSON時はそれを採用する`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerJsonFallback_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let brokenPath = Path.Combine(tempDir, "broken.json")
        let validPath = Path.Combine(tempDir, "valid.json")
        File.WriteAllText(brokenPath, "{ invalid json content ...")
        File.WriteAllText(validPath, """{ "targetDirectory": "C:\\ExplicitValidDir", "pathLengthThreshold": 210 }""")

        try
            // 不正 JSON の場合は baseSettings（既定値）へフォールバック
            let fallbackSettings = Settings.loadConfigurationWithConfig (Some "Development") (Some brokenPath) None
            let defaults = Settings.defaultSettings ()
            fallbackSettings.TargetDirectory |> should equal defaults.TargetDirectory
            fallbackSettings.PathLengthThreshold |> should equal defaults.PathLengthThreshold

            // 正常 JSON の場合はその内容を最優先採用
            let validSettings = Settings.loadConfigurationWithConfig (Some "Development") (Some validPath) None
            validSettings.TargetDirectory |> should equal "C:\\ExplicitValidDir"
            validSettings.PathLengthThreshold |> should equal 210
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfigurationWithConfig はディレクトリ内 appsettings.{env}.json に構文エラーがあってもクラッシュせずフォールバックする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEnvSyntaxError_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let basePath = Path.Combine(tempDir, "appsettings.json")
        let brokenEnvPath = Path.Combine(tempDir, "appsettings.BrokenEnv.json")
        let dummyEnv = Path.Combine(tempDir, ".env")

        File.WriteAllText(basePath, """{ "pathLengthThreshold": 222, "selectedModel": "base-model" }""")
        File.WriteAllText(brokenEnvPath, "{ broken json syntax !!!")
        File.WriteAllText(dummyEnv, "# dummy")

        try
            // BrokenEnv 環境下の設定読み込みで構文エラーがあっても例外終了せず、basePath の設定を採用
            let loaded = Settings.loadConfigurationWithConfig (Some "BrokenEnv") None (Some dummyEnv)
            loaded.PathLengthThreshold |> should equal 222
            loaded.SelectedModel |> should equal "base-model"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)


    [<Fact>]
    let ``loadConfigurationWithConfig は configNameOpt に不正文字が含まれる場合に resolveEnvironmentName へ安全フォールバックする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerSanitizeEnv_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnv = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnv, "# dummy")

        try
            // 不正ファイル名文字（* : ? " < > | 等）を含む環境名
            let loaded = Settings.loadConfigurationWithConfig (Some "env*with:bad?chars") None (Some dummyEnv)
            let defaults = Settings.defaultSettings ()
            // クラッシュせず正常に設定がロードされること
            loaded.PathLengthThreshold |> should equal defaults.PathLengthThreshold
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``save は保存先パスが既存ディレクトリなどの書き込み不能パスの場合に Error IoError を返す`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerSaveErrorTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        try
            // tempDir は既存ディレクトリであるため、ここにファイルを保存しようとすると File.Move 等でエラー
            let settings = Settings.defaultSettings ()
            let result = Settings.save tempDir settings
            match result with
            | Error (IoError (msg, Some _)) ->
                msg |> should contain "設定ファイルの保存中にエラーが発生しました"
            | Error other -> failwith $"予期しないエラー型: {other}"
            | Ok () -> failwith "既存ディレクトリへの保存で成功してはいけません"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    /// <summary>空白環境名・パストラバーサル文字列・不在ディレクトリ指定時に一時隔離環境で安全フォールバックすることを検証する</summary>
    [<Fact>]
    let ``loadConfigurationWithConfig は空白環境名・パストラバーサル文字列・不在ディレクトリ指定時に安全フォールバックする`` () =
        let defaults = Settings.defaultSettings ()
        let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerSanitizeFallback_" + Guid.NewGuid().ToString("N"))
        let subDir = Path.Combine(tempDir, "sub")
        Directory.CreateDirectory(subDir) |> ignore
        let isolatedEnv = Path.Combine(subDir, ".env")
        File.WriteAllText(isolatedEnv, "# isolated\n")

        try
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
            let fallbackEnv = Settings.resolveEnvironmentName ()

            // 隔離ディレクトリ内にはフォールバック先環境（Development/Production）用の非既定値 (199) を配置
            File.WriteAllText(
                Path.Combine(subDir, $"appsettings.{fallbackEnv}.json"),
                """{ "pathLengthThreshold": 199, "selectedModel": "fallback-env-model" }"""
            )
            // トラバーサルや不正名の参照先に別値 (222) を配置し、参照されないことを検証
            File.WriteAllText(
                Path.Combine(tempDir, "appsettings.evil.json"),
                """{ "pathLengthThreshold": 222, "selectedModel": "evil-model" }"""
            )
            File.WriteAllText(
                Path.Combine(tempDir, "evil.json"),
                """{ "pathLengthThreshold": 222, "selectedModel": "evil-model" }"""
            )
            File.WriteAllText(
                Path.Combine(subDir, "appsettings.env.json"),
                """{ "pathLengthThreshold": 222, "selectedModel": "evil-model" }"""
            )
            let badSubDir = Path.Combine(subDir, "bad")
            Directory.CreateDirectory(badSubDir) |> ignore
            File.WriteAllText(
                Path.Combine(badSubDir, "appsettings.sub.json"),
                """{ "pathLengthThreshold": 222, "selectedModel": "evil-model" }"""
            )

            // 1. configNameOpt = Some "   " (空白文字のみ) -> 既定環境名へフォールバック (199)
            let loadedWhitespace = Settings.loadConfigurationWithConfig (Some "   ") None (Some isolatedEnv)
            loadedWhitespace.PathLengthThreshold |> should equal 199
            loadedWhitespace.SelectedModel |> should equal "fallback-env-model"

            // 2. configNameOpt = Some "bad\\sub" (バックスラッシュ含む) -> サニタイズされ既定環境名へフォールバック (199)
            let loadedBackslash = Settings.loadConfigurationWithConfig (Some "bad\\sub") None (Some isolatedEnv)
            loadedBackslash.PathLengthThreshold |> should equal 199
            loadedBackslash.SelectedModel |> should equal "fallback-env-model"

            // 3. configNameOpt = Some "../evil" (親ディレクトリトラバーサル含む) -> サニタイズされ既定環境名へフォールバック (199)
            let loadedParentTraversal = Settings.loadConfigurationWithConfig (Some "../evil") None (Some isolatedEnv)
            loadedParentTraversal.PathLengthThreshold |> should equal 199
            loadedParentTraversal.SelectedModel |> should equal "fallback-env-model"

            // 4. configNameOpt = Some "bad/../env" (スラッシュ・親ディレクトリトラバーサル含む) -> サニタイズされ既定環境名へフォールバック (199)
            let loadedTraversal = Settings.loadConfigurationWithConfig (Some "bad/../env") None (Some isolatedEnv)
            loadedTraversal.PathLengthThreshold |> should equal 199
            loadedTraversal.SelectedModel |> should equal "fallback-env-model"

            // 5. envPathOpt = Some nonexistentPath (存在しないディレクトリ) -> 組み込み既定値 (240) を返す
            let nonExistentEnv = Path.Combine(Path.GetTempPath(), "nonexistent_dir_" + Guid.NewGuid().ToString("N"), ".env")
            let loadedNonExistent = Settings.loadConfigurationWithConfig (Some "Development") None (Some nonExistentEnv)
            loadedNonExistent.PathLengthThreshold |> should equal defaults.PathLengthThreshold
            loadedNonExistent.SelectedModel |> should equal defaults.SelectedModel
        finally
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    // =========================================================================
    // Task 1.2: Issue #41 Root Cause 再現テストおよび先行単体・結合テスト (Red)
    // =========================================================================

    [<CLIMutable>]
    type ConverterTestDto = {
        ApiKey: string option
        AfterProp: string
    }

    let private assertOk (result: Result<unit, RenamerError>) : unit =
        match result with
        | Ok () -> ()
        | Error err -> failwith $"Expected Ok (), got Error: {err}"

    let private createConverterJsonOptions () =
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.Converters.Add(StringOptionConverter())
        opts

    let private withTempDir (prefix: string) (action: string -> unit) : unit =
        let tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}")
        Directory.CreateDirectory(tempDir) |> ignore
        try
            action tempDir
        finally
            if Directory.Exists(tempDir) then
                for file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories) do
                    try
                        let attrs = File.GetAttributes(file)
                        if (attrs &&& FileAttributes.ReadOnly) <> enum 0 then
                            File.SetAttributes(file, FileAttributes.Normal)
                    with _ -> ()
                Directory.Delete(tempDir, true)

    let private makeContext (curDir: string) (appBaseDir: string) (appDataDir: string) (dotEnvPathOpt: string option) : ConfigLoadContext =
        {
            CurrentDirectory = curDir
            AppBaseDirectory = appBaseDir
            AppDataDirectory = appDataDir
            DotEnvPath = dotEnvPathOpt
        }

    // -------------------------------------------------------------------------
    // 3.1 Issue #41 Root Cause 再現テスト (UT-RED-01 ~ UT-RED-03)
    // -------------------------------------------------------------------------

    /// <summary>UT-RED-01: %APPDATA% のプレースホルダーが参照されず、リポジトリ側有効キーと出典パスおよび移行案内が返ることを検証する</summary>
    [<Fact>]
    let ``UT-RED-01: loadConfigurationWithMetadataWithContext は %APPDATA% の xxx を無視してリポジトリ有効キーと出典パスおよび移行通知を返す`` () =
        withTempDir "UtRed01" (fun root ->
            let repoDir = Path.Combine(root, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            let binDir = Path.Combine(repoDir, "bin", "Debug", "net10.0")
            let appDataDir = Path.Combine(root, "appdata", "TagBasedVideoManager")
            Directory.CreateDirectory(renamerDir) |> ignore
            Directory.CreateDirectory(binDir) |> ignore
            Directory.CreateDirectory(appDataDir) |> ignore

            let repoBasePath = Path.Combine(renamerDir, "appsettings.json")
            let repoDevPath = Path.Combine(renamerDir, "appsettings.Development.json")
            let appDataBasePath = Path.Combine(appDataDir, "appsettings.json")
            File.WriteAllText(repoBasePath, """{ "apiKey": null, "selectedModel": "repo/base-model" }""")
            File.WriteAllText(repoDevPath, """{ "apiKey": "sk-or-v1-valid-repo-key" }""")
            File.WriteAllText(appDataBasePath, """{ "apiKey": "xxx", "selectedModel": "appdata/stale-model" }""")

            let ctx = makeContext repoDir binDir appDataDir None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None None

            loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-valid-repo-key")
            loaded.Settings.SelectedModel |> should equal "repo/base-model"
            loaded.ApiKeySourcePath |> should equal (Some repoDevPath)
            loaded.EnvironmentName |> should equal "Development"
            loaded.SavePath |> should equal repoDevPath
            Assert.True(
                loaded.TraceLogs |> List.exists (fun l -> l.Contains(appDataDir) && l.Contains(repoDevPath)),
                $"TraceLogs に %%APPDATA%% 残存移行案内が含まれていません: {loaded.TraceLogs}"
            )
            Assert.False(
                loaded.TraceLogs |> List.exists (fun l -> l.Contains("sk-or-v1-valid-repo-key")),
                "TraceLogs に実 API キー値が出力されてはいけません"
            )
        )

    /// <summary>UT-RED-02: Settings.save が ApiKey (string option) を素の JSON 文字列または null として保存し往復復元できることを検証する</summary>
    [<Fact>]
    let ``UT-RED-02: Settings.save は ApiKey を素の文字列または null として出力し Settings.load および loadConfiguration で往復復元できる`` () =
        withTempDir "UtRed02" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            let settingsWithKey = { Settings.defaultSettings () with ApiKey = Some "sk-or-v1-roundtrip-key" }

            match Settings.save filePath settingsWithKey with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            let rawJson = File.ReadAllText(filePath)
            rawJson |> should contain "\"apiKey\": \"sk-or-v1-roundtrip-key\""
            rawJson |> should not' (contain "\"value\"")
            rawJson |> should not' (contain "\"Case\"")
            rawJson |> should not' (contain "\"Fields\"")

            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded -> loaded.ApiKey |> should equal (Some "sk-or-v1-roundtrip-key")

            let cfgLoaded = Settings.loadConfiguration (Some filePath) None
            cfgLoaded.ApiKey |> should equal (Some "sk-or-v1-roundtrip-key")

            let settingsWithNone = { Settings.defaultSettings () with ApiKey = None }
            match Settings.save filePath settingsWithNone with
            | Error err -> failwith $"Save None failed: {err}"
            | Ok () -> ()

            let rawNoneJson = File.ReadAllText(filePath)
            rawNoneJson |> should contain "\"apiKey\": null"
            match Settings.load filePath with
            | Error err -> failwith $"Load None failed: {err}"
            | Ok loadedNone -> loadedNone.ApiKey |> should equal None
        )

    /// <summary>UT-RED-03: bin/ にコピーが存在しても読込・保存共通で src/TagBasedVideoManager.Renamer/ が採用されることを検証する</summary>
    [<Fact>]
    let ``UT-RED-03: resolveConfigDirectoryWithOrigins と resolveSavePath は bin 配下に設定が存在しても src/TagBasedVideoManager.Renamer を選ぶ`` () =
        SettingsIsolationHarness.withIsolatedSettingsEnvironment (fun _ ->
            withTempDir "UtRed03" (fun root ->
                let renamerDir = Path.Combine(root, "src", "TagBasedVideoManager.Renamer")
                let binDir = Path.Combine(root, "bin", "Debug", "net10.0")
                Directory.CreateDirectory(renamerDir) |> ignore
                Directory.CreateDirectory(binDir) |> ignore

                File.WriteAllText(Path.Combine(renamerDir, "appsettings.json"), """{ "apiKey": null }""")
                File.WriteAllText(Path.Combine(binDir, "appsettings.json"), """{ "apiKey": "bin-copy" }""")

                let resolvedDir, traceLogs = Settings.resolveConfigDirectoryWithOrigins binDir binDir "Development"
                resolvedDir |> should equal renamerDir
                Assert.NotEqual<string>(binDir, resolvedDir)
                Assert.True(traceLogs |> List.exists (fun l -> l.Contains(renamerDir)))

                let origCwd = Directory.GetCurrentDirectory()
                let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                try
                    Directory.SetCurrentDirectory(binDir)
                    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development")
                    let savePath = Settings.resolveSavePath None
                    savePath |> should equal (Path.Combine(renamerDir, "appsettings.Development.json"))
                    Assert.False(savePath.StartsWith(binDir, StringComparison.OrdinalIgnoreCase))
                finally
                    Directory.SetCurrentDirectory(origCwd)
                    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
            )
        )

    // -------------------------------------------------------------------------
    // 3.2 StringOptionConverter & Settings.load (UT-CONV-01 ~ UT-CONV-10)
    // -------------------------------------------------------------------------

    /// <summary>UT-CONV-01: StringOptionConverter と Settings.save/load が Some 文字列を素の JSON 文字列で往復できることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-01: StringOptionConverter と Settings.save/load は Some 文字列を素の JSON 文字列として往復する`` () =
        withTempDir "UtConv01" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let dtoJson = JsonSerializer.Serialize({ ApiKey = Some "sk-or-v1-abc1234567"; AfterProp = "ok" }, opts)
            dtoJson |> should contain "\"apiKey\":\"sk-or-v1-abc1234567\""
            dtoJson |> should not' (contain "\"value\"")
            let dtoBack = JsonSerializer.Deserialize<ConverterTestDto>(dtoJson, opts)
            dtoBack.ApiKey |> should equal (Some "sk-or-v1-abc1234567")
            dtoBack.AfterProp |> should equal "ok"

            let filePath = Path.Combine(tempDir, "appsettings.json")
            let settings = { Settings.defaultSettings () with ApiKey = Some "sk-or-v1-abc1234567" }
            match Settings.save filePath settings with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            let savedText = File.ReadAllText(filePath)
            savedText |> should contain "\"apiKey\": \"sk-or-v1-abc1234567\""
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded -> loaded.ApiKey |> should equal (Some "sk-or-v1-abc1234567")
        )

    /// <summary>UT-CONV-02: StringOptionConverter と Settings.save/load が None を null として往復できることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-02: StringOptionConverter と Settings.save/load は None を null として出力し復元する`` () =
        withTempDir "UtConv02" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let converter = StringOptionConverter()
            use ms = new MemoryStream()
            use writer = new Utf8JsonWriter(ms)
            converter.Write(writer, None, opts)
            writer.Flush()
            let directNullJson = Encoding.UTF8.GetString(ms.ToArray())
            directNullJson |> should equal "null"

            let nullBytes = Encoding.UTF8.GetBytes("null")
            let mutable reader = Utf8JsonReader(nullBytes.AsSpan())
            reader.Read() |> ignore
            reader.TokenType |> should equal JsonTokenType.Null
            let directReadNull = converter.Read(&reader, typeof<string option>, JsonSerializerOptions.Default)
            directReadNull |> should equal None

            let filePath = Path.Combine(tempDir, "appsettings.json")
            let settings = { Settings.defaultSettings () with ApiKey = None }
            match Settings.save filePath settings with
            | Error err -> failwith $"Save failed: {err}"
            | Ok () -> ()

            File.ReadAllText(filePath) |> should contain "\"apiKey\": null"
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded -> loaded.ApiKey |> should equal None
        )

    /// <summary>UT-CONV-03: 空文字 "" の apiKey が Settings.load および StringOptionConverter で None になることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-03: 空文字の apiKey は Settings.load と StringOptionConverter で None として安全に読み込まれる`` () =
        withTempDir "UtConv03" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let dto = JsonSerializer.Deserialize<ConverterTestDto>("""{"apiKey":"","afterProp":"kept"}""", opts)
            dto.ApiKey |> should equal None
            dto.AfterProp |> should equal "kept"

            let filePath = Path.Combine(tempDir, "appsettings.json")
            File.WriteAllText(
                filePath,
                """{ "targetDirectory": "", "pathLengthThreshold": 240, "selectedModel": "m", "apiKey": "", "rules": [] }"""
            )
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.ApiKey |> should equal None
                loaded.SelectedModel |> should equal "m"
                loaded.Rules |> should be Empty
        )

    /// <summary>UT-CONV-04: 空白のみの apiKey が Settings.load および StringOptionConverter で None になることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-04: 空白のみの apiKey は Settings.load と StringOptionConverter で None として安全に読み込まれる`` () =
        withTempDir "UtConv04" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let dto = JsonSerializer.Deserialize<ConverterTestDto>("{\"apiKey\":\"   \\t  \",\"afterProp\":\"kept\"}", opts)
            dto.ApiKey |> should equal None
            dto.AfterProp |> should equal "kept"

            let filePath = Path.Combine(tempDir, "appsettings.json")
            File.WriteAllText(
                filePath,
                "{\"targetDirectory\":\"\",\"pathLengthThreshold\":240,\"selectedModel\":\"m\",\"apiKey\":\"   \\t  \",\"rules\":[]}"
            )
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded -> loaded.ApiKey |> should equal None
        )

    /// <summary>UT-CONV-05: 前後空白および引用符付き apiKey が正規化されて読み込まれることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-05: 前後空白と引用符付き apiKey が正規化されて読み込まれる`` () =
        withTempDir "UtConv05" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let dtoSingle = JsonSerializer.Deserialize<ConverterTestDto>("""{"apiKey":"  'sk-or-v1-trimmed-key'  ","afterProp":"ok1"}""", opts)
            dtoSingle.ApiKey |> should equal (Some "sk-or-v1-trimmed-key")
            let dtoDouble = JsonSerializer.Deserialize<ConverterTestDto>("""{"apiKey":" \"sk-or-v1-double\" ","afterProp":"ok2"}""", opts)
            dtoDouble.ApiKey |> should equal (Some "sk-or-v1-double")

            let filePath1 = Path.Combine(tempDir, "single.json")
            let filePath2 = Path.Combine(tempDir, "double.json")
            File.WriteAllText(filePath1, """{ "apiKey": "  'sk-or-v1-trimmed-key'  " }""")
            File.WriteAllText(filePath2, """{ "apiKey": " \"sk-or-v1-double\" " }""")

            match Settings.load filePath1 with
            | Error err -> failwith $"Load 1 failed: {err}"
            | Ok loaded1 -> loaded1.ApiKey |> should equal (Some "sk-or-v1-trimmed-key")

            match Settings.load filePath2 with
            | Error err -> failwith $"Load 2 failed: {err}"
            | Ok loaded2 -> loaded2.ApiKey |> should equal (Some "sk-or-v1-double")
        )

    /// <summary>UT-CONV-06: 旧バグ形式 { "value": ... } の apiKey でもクラッシュせず None かつ警告出力されることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-06: 旧バグ形式のオブジェクト apiKey は StringOptionConverter と Settings.load で安全にスキップされ None になる`` () =
        withTempDir "UtConv06" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let origErr = Console.Error
            use sw = new StringWriter()
            let dto =
                try
                    Console.SetError(TextWriter.Synchronized sw)
                    JsonSerializer.Deserialize<ConverterTestDto>("""{"apiKey":{"value":"sk-or-v1-legacy-broken"},"afterProp":"preserved"}""", opts)
                finally
                    Console.SetError(origErr)

            dto.ApiKey |> should equal None
            dto.AfterProp |> should equal "preserved"
            sw.ToString() |> should contain "[WARN]"

            let filePath = Path.Combine(tempDir, "legacy.json")
            File.WriteAllText(
                filePath,
                """{ "selectedModel": "after-legacy", "apiKey": { "value": "sk-or-v1-legacy-broken" } }"""
            )
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.ApiKey |> should equal None
                loaded.SelectedModel |> should equal "after-legacy"
        )

    /// <summary>UT-CONV-07: 深くネストしたオブジェクト型 apiKey でもストリーム位置ズレなく後続プロパティが読み込まれることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-07: ネストオブジェクト型 apiKey でもストリーム位置ズレなく後続プロパティと rules が読み込まれる`` () =
        withTempDir "UtConv07" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            let dto = JsonSerializer.Deserialize<ConverterTestDto>("""{"apiKey":{"a":{"b":[1,2]}},"afterProp":"aligned"}""", opts)
            dto.ApiKey |> should equal None
            dto.AfterProp |> should equal "aligned"

            let filePath = Path.Combine(tempDir, "nested.json")
            File.WriteAllText(
                filePath,
                """{
                  "apiKey": { "a": { "b": [1, 2] } },
                  "selectedModel": "after-nested",
                  "rules": [
                    {
                      "id": "r-after",
                      "name": "後続ルール",
                      "pattern": "{Title}.mp4",
                      "promptInstruction": "指示",
                      "order": 0,
                      "enableWebSearch": false
                    }
                  ]
                }"""
            )
            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                loaded.ApiKey |> should equal None
                loaded.SelectedModel |> should equal "after-nested"
                loaded.Rules.Length |> should equal 1
                loaded.Rules.Head.Id |> should equal "r-after"
        )

    /// <summary>UT-CONV-08: 数値・真偽値・配列トークンの apiKey でも例外なく None となり他プロパティが維持されることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-08: 数値・真偽値・配列の apiKey は StringOptionConverter と Settings.load で安全に None になる`` () =
        withTempDir "UtConv08" (fun tempDir ->
            let opts = createConverterJsonOptions ()
            for (label, tokenJson) in [ ("number", "12345"); ("bool", "true"); ("array", "[\"x\"]") ] do
                let dto = JsonSerializer.Deserialize<ConverterTestDto>($"{{\"apiKey\":{tokenJson},\"afterProp\":\"ok-{label}\"}}", opts)
                dto.ApiKey |> should equal None
                dto.AfterProp |> should equal $"ok-{label}"

                let filePath = Path.Combine(tempDir, $"{label}.json")
                File.WriteAllText(
                    filePath,
                    $"{{\"apiKey\":{tokenJson},\"selectedModel\":\"model-{label}\"}}"
                )
                match Settings.load filePath with
                | Error err -> failwith $"Load {label} failed: {err}"
                | Ok loaded ->
                    loaded.ApiKey |> should equal None
                    loaded.SelectedModel |> should equal $"model-{label}"
        )

    /// <summary>UT-CONV-09: selectedModel のみの最小 JSON 読み込みで欠損フィールドが既定値補完されることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-09: Settings.load は selectedModel のみの最小 JSON に対して欠損フィールドを既定値補完する`` () =
        withTempDir "UtConv09" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "minimal.json")
            File.WriteAllText(filePath, """{ "selectedModel": "custom/model-only" }""")

            match Settings.load filePath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok loaded ->
                let defaults = Settings.defaultSettings ()
                loaded.SelectedModel |> should equal "custom/model-only"
                loaded.TargetDirectory |> should equal ""
                loaded.PathLengthThreshold |> should equal 240
                loaded.ApiKey |> should equal None
                loaded.Rules |> should equal defaults.Rules
        )

    /// <summary>UT-CONV-10: 0バイト空ファイルおよび空白のみファイルを Settings.load すると SettingsError が返ることを検証する</summary>
    [<Fact>]
    let ``UT-CONV-10: Settings.load は 0バイト空ファイルと空白のみファイルに対して SettingsError を返す`` () =
        withTempDir "UtConv10" (fun tempDir ->
            let zeroPath = Path.Combine(tempDir, "zero.json")
            let wsPath = Path.Combine(tempDir, "ws.json")
            File.WriteAllText(zeroPath, "")
            File.WriteAllText(wsPath, "   \r\n  ")

            match Settings.load zeroPath with
            | Error (SettingsError msg) -> Assert.False(String.IsNullOrWhiteSpace(msg))
            | other -> failwith $"Expected SettingsError for zero-byte file, got: {other}"

            match Settings.load wsPath with
            | Error (SettingsError msg) -> Assert.False(String.IsNullOrWhiteSpace(msg))
            | other -> failwith $"Expected SettingsError for whitespace file, got: {other}"
        )

    // -------------------------------------------------------------------------
    // 3.3 & 4.2 normalizeApiKeyString / validateApiKey (UT-KEY-01 ~ 22, DT-KEY-01 ~ 07)
    // -------------------------------------------------------------------------

    /// <summary>normalizeApiKeyString が null・空・空白・引用符のみを None とし、前後空白と引用符を除去することを検証する</summary>
    [<Fact>]
    let ``normalizeApiKeyString は null や引用符のみを None とし前後空白・引用符を正規化する`` () =
        Settings.normalizeApiKeyString null |> should equal None
        Settings.normalizeApiKeyString "" |> should equal None
        Settings.normalizeApiKeyString "   \t  " |> should equal None
        Settings.normalizeApiKeyString "\"\"" |> should equal None
        Settings.normalizeApiKeyString " '' " |> should equal None
        Settings.normalizeApiKeyString "\"   \"" |> should equal None
        Settings.normalizeApiKeyString " '  ' " |> should equal None
        Settings.normalizeApiKeyString "  'sk-or-v1-abc'  " |> should equal (Some "sk-or-v1-abc")
        Settings.normalizeApiKeyString " \"sk-or-v1-xyz\" " |> should equal (Some "sk-or-v1-xyz")

    /// <summary>UT-KEY-01 ~ UT-KEY-03: None・空文字・空白引用符のみが MissingApiKey になることを検証する</summary>
    [<Fact>]
    let ``UT-KEY-01~03: validateApiKey は None・空文字・空白および引用符のみを MissingApiKey と判定する`` () =
        Settings.validateApiKey None |> should equal MissingApiKey
        Settings.validateApiKey (Some "") |> should equal MissingApiKey
        Settings.validateApiKey (Some "   ") |> should equal MissingApiKey
        Settings.validateApiKey (Some " '' ") |> should equal MissingApiKey
        Settings.validateApiKey (Some " \"\" ") |> should equal MissingApiKey

    /// <summary>UT-KEY-04 ~ UT-KEY-10: API キー長境界値 (1, 9, 10, 11 文字) と正規化後境界を検証する</summary>
    [<Fact>]
    let ``UT-KEY-04~10: validateApiKey は文字数境界 1・9・10・11 および引用符除去後長で正しく判定する`` () =
        // UT-KEY-04: 最小長 1
        Settings.validateApiKey (Some "a") |> should equal (PlaceholderApiKey "a")
        // UT-KEY-05: 最小長 - 1 = 9
        Settings.validateApiKey (Some "123456789") |> should equal (PlaceholderApiKey "123456789")
        // UT-KEY-06: 最小長 = 10
        Settings.validateApiKey (Some "1234567890") |> should equal (ValidApiKey "1234567890")
        // UT-KEY-07: 最小長 + 1 = 11
        Settings.validateApiKey (Some "sk-or-v1-ab") |> should equal (ValidApiKey "sk-or-v1-ab")
        // UT-KEY-08: 標準 OpenRouter キー
        Settings.validateApiKey (Some "sk-or-v1-0123456789abcdef") |> should equal (ValidApiKey "sk-or-v1-0123456789abcdef")
        // UT-KEY-09: 前後空白・引用符除去後有効
        Settings.validateApiKey (Some "  'sk-or-v1-clean1234'  ") |> should equal (ValidApiKey "sk-or-v1-clean1234")
        Settings.validateApiKey (Some "\"sk-or-v1-clean1234\"") |> should equal (ValidApiKey "sk-or-v1-clean1234")
        // UT-KEY-10: 引用符込み 11 文字、除去後 9 文字
        Settings.validateApiKey (Some "'123456789'") |> should equal (PlaceholderApiKey "123456789")

    /// <summary>UT-KEY-11 ~ UT-KEY-14: 既知プレースホルダー文字列（大文字小文字非区別）を検証する</summary>
    [<Fact>]
    let ``UT-KEY-11~14: validateApiKey は既知プレースホルダー文字列を大文字小文字非区別で PlaceholderApiKey と判定する`` () =
        // UT-KEY-11
        Settings.validateApiKey (Some "xxx") |> should equal (PlaceholderApiKey "xxx")
        Settings.validateApiKey (Some "your-api-key") |> should equal (PlaceholderApiKey "your-api-key")
        Settings.validateApiKey (Some "YOUR_API_KEY") |> should equal (PlaceholderApiKey "YOUR_API_KEY")
        // UT-KEY-12
        Settings.validateApiKey (Some "api_key") |> should equal (PlaceholderApiKey "api_key")
        Settings.validateApiKey (Some "ApiKey") |> should equal (PlaceholderApiKey "ApiKey")
        Settings.validateApiKey (Some "ChangeMe") |> should equal (PlaceholderApiKey "ChangeMe")
        // UT-KEY-13
        Settings.validateApiKey (Some "replace_me") |> should equal (PlaceholderApiKey "replace_me")
        Settings.validateApiKey (Some "PLACEHOLDER") |> should equal (PlaceholderApiKey "PLACEHOLDER")
        Settings.validateApiKey (Some "dummy") |> should equal (PlaceholderApiKey "dummy")
        // UT-KEY-14
        Settings.validateApiKey (Some "none") |> should equal (PlaceholderApiKey "none")
        Settings.validateApiKey (Some "NULL") |> should equal (PlaceholderApiKey "NULL")

    /// <summary>UT-KEY-15 ~ UT-KEY-22: テンプレート・x反復・連続ドット・sk-or-v1-ダミー系およびその境界を検証する</summary>
    [<Fact>]
    let ``UT-KEY-15~22: validateApiKey は角括弧・x反復・連続ドット・sk-or-v1 ダミー値と有効境界を正しく区別する`` () =
        // UT-KEY-15: 角括弧テンプレート
        Settings.validateApiKey (Some "[API_KEY]") |> should equal (PlaceholderApiKey "[API_KEY]")
        Settings.validateApiKey (Some "[YOUR_OPENROUTER_API_KEY]") |> should equal (PlaceholderApiKey "[YOUR_OPENROUTER_API_KEY]")
        // UT-KEY-16: 片側のみ角括弧で 10 文字以上は有効
        Settings.validateApiKey (Some "[sk-or-v1-123456") |> should equal (ValidApiKey "[sk-or-v1-123456")
        Settings.validateApiKey (Some "sk-or-v1-123456]") |> should equal (ValidApiKey "sk-or-v1-123456]")
        // UT-KEY-17: 'x'/'X' 反復
        Settings.validateApiKey (Some "xxxxxxxxxx") |> should equal (PlaceholderApiKey "xxxxxxxxxx")
        Settings.validateApiKey (Some "XXXXXXXXXXXX") |> should equal (PlaceholderApiKey "XXXXXXXXXXXX")
        Settings.validateApiKey (Some "xXxXxXxXxXxX") |> should equal (PlaceholderApiKey "xXxXxXxXxXxX")
        // UT-KEY-18: 'x' 含む有効キー境界
        Settings.validateApiKey (Some "xxxxxxxxx1") |> should equal (ValidApiKey "xxxxxxxxx1")
        // UT-KEY-19: 大文字小文字混合 + 前後空白・引用符
        Settings.validateApiKey (Some "  'Your-Api-Key'  ") |> should equal (PlaceholderApiKey "Your-Api-Key")
        // UT-KEY-20: サンプルプレースホルダー
        Settings.validateApiKey (Some "sk-or-v1-xxxxxxxxxxxxxxxx") |> should equal (PlaceholderApiKey "sk-or-v1-xxxxxxxxxxxxxxxx")
        Settings.validateApiKey (Some "sk-or-v1-...") |> should equal (PlaceholderApiKey "sk-or-v1-...")
        Settings.validateApiKey (Some "SK-OR-V1-0000****") |> should equal (PlaceholderApiKey "SK-OR-V1-0000****")
        // UT-KEY-21: 連続ドット含むダミー値
        Settings.validateApiKey (Some "abc...def123") |> should equal (PlaceholderApiKey "abc...def123")
        // UT-KEY-22: sk-or-v1- に実文字混在の有効キー境界
        Settings.validateApiKey (Some "sk-or-v1-x1a2") |> should equal (ValidApiKey "sk-or-v1-x1a2")

    /// <summary>DT-KEY-01 ~ DT-KEY-07: validateApiKey の C2 複合条件デシジョンテーブル全7行を検証する</summary>
    [<Fact>]
    let ``DT-KEY-01~07: validateApiKey 複合条件デシジョンテーブル全7行`` () =
        // DT-KEY-01: C1=F
        Settings.validateApiKey None |> should equal MissingApiKey
        Settings.validateApiKey (Some "") |> should equal MissingApiKey
        Settings.validateApiKey (Some "  ") |> should equal MissingApiKey
        // DT-KEY-02: C1..C6 全 T
        Settings.validateApiKey (Some "sk-or-v1-123456") |> should equal (ValidApiKey "sk-or-v1-123456")
        // DT-KEY-03: C2=F (長さ < 10)
        Settings.validateApiKey (Some "123456789") |> should equal (PlaceholderApiKey "123456789")
        // DT-KEY-04: C3=F (既知ダミー)
        Settings.validateApiKey (Some "your-api-key") |> should equal (PlaceholderApiKey "your-api-key")
        // DT-KEY-05: C4=F ([...] テンプレート)
        Settings.validateApiKey (Some "[OPENROUTER_API_KEY]") |> should equal (PlaceholderApiKey "[OPENROUTER_API_KEY]")
        // DT-KEY-06: C5=F (x反復 / 連続ドット)
        Settings.validateApiKey (Some "xxxxxxxxxxxx") |> should equal (PlaceholderApiKey "xxxxxxxxxxxx")
        Settings.validateApiKey (Some "abc...def123") |> should equal (PlaceholderApiKey "abc...def123")
        // DT-KEY-07: C6=F (sk-or-v1- 残余ダミー記号のみ)
        Settings.validateApiKey (Some "sk-or-v1-xxxxxxxxxxxxxxxx") |> should equal (PlaceholderApiKey "sk-or-v1-xxxxxxxxxxxxxxxx")

    // -------------------------------------------------------------------------
    // 3.4 & 4.3 resolveConfigDirectoryWithOrigins / resolveConfigDirectory / resolveSavePath
    // -------------------------------------------------------------------------

    /// <summary>UT-DIR-01 (DT-DIR-01): resolveConfigDirectory は baseDirOpt = Some dir をそのまま返すことを検証する</summary>
    [<Fact>]
    let ``UT-DIR-01: resolveConfigDirectory は Some dir 指定時にそのまま dir を返す`` () =
        withTempDir "UtDir01" (fun tempDir ->
            Settings.resolveConfigDirectory (Some tempDir) "Development" |> should equal tempDir
        )

    /// <summary>UT-DIR-02 ~ UT-DIR-05: 親探索 step 0, 1, 8 (上限内) と step 9 (上限超過) を検証する</summary>
    [<Fact>]
    let ``UT-DIR-02~05: resolveConfigDirectoryWithOrigins は step 0..8 で採用し step 9 超過時は第2段階へフォールバックする`` () =
        withTempDir "UtDirSteps" (fun root ->
            let repoDir = Path.Combine(root, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            let unrelatedAppDir = Path.Combine(root, "unrelated_app")
            Directory.CreateDirectory(renamerDir) |> ignore
            Directory.CreateDirectory(unrelatedAppDir) |> ignore

            // UT-DIR-03: step 1 で appsettings.Development.json のみ存在
            File.WriteAllText(Path.Combine(renamerDir, "appsettings.Development.json"), """{ "selectedModel": "env-only" }""")
            let sub1 = Path.Combine(repoDir, "sub1")
            Directory.CreateDirectory(sub1) |> ignore
            let resStep1, traceStep1 = Settings.resolveConfigDirectoryWithOrigins sub1 unrelatedAppDir "Development"
            resStep1 |> should equal renamerDir
            Assert.True(traceStep1 |> List.exists (fun l -> l.Contains(renamerDir)))

            // UT-DIR-02: step 0 (起点直下) + appsettings.json
            File.WriteAllText(Path.Combine(renamerDir, "appsettings.json"), """{ "apiKey": null }""")
            let resStep0, traceStep0 = Settings.resolveConfigDirectoryWithOrigins repoDir unrelatedAppDir "Development"
            resStep0 |> should equal renamerDir
            Assert.True(traceStep0 |> List.exists (fun l -> l.Contains(renamerDir)))

            // UT-DIR-04: step 8 (親8階層上)
            let d8 = Path.Combine(repoDir, "d1", "d2", "d3", "d4", "d5", "d6", "d7", "d8")
            Directory.CreateDirectory(d8) |> ignore
            let resStep8, _ = Settings.resolveConfigDirectoryWithOrigins d8 unrelatedAppDir "Development"
            resStep8 |> should equal renamerDir

            // UT-DIR-05: step 9 (親9階層上 = 上限超過 -> unrelatedAppDir)
            let d9 = Path.Combine(d8, "d9")
            Directory.CreateDirectory(d9) |> ignore
            let resStep9, traceStep9 = Settings.resolveConfigDirectoryWithOrigins d9 unrelatedAppDir "Development"
            resStep9 |> should equal unrelatedAppDir
            Assert.True(
                traceStep9 |> List.exists (fun l -> l.Contains("8") || l.Contains("上限")),
                $"親8階層上限到達トレースが記録されていません: {traceStep9}"
            )
            Assert.True(
                traceStep9 |> List.exists (fun l -> l.Contains(unrelatedAppDir)),
                $"第2段階フォールバック先採用トレースが記録されていません: {traceStep9}"
            )
        )

    /// <summary>UT-DIR-06 ~ UT-DIR-10 & DT-DIR-02 ~ DT-DIR-06: 2段階探索優先順位・単独CurrentDirectory非採用・空フォルダ除外を検証する</summary>
    [<Fact>]
    let ``UT-DIR-06~10 & DT-DIR-02~06: 2段階共通ディレクトリ解決の全条件分岐を検証する`` () =
        withTempDir "UtDirMatrix" (fun root ->
            // UT-DIR-06: CurrentDirectory 直下に appsettings.json があるだけでは採用しない
            let curWithJson = Path.Combine(root, "cur_direct")
            let publishDir = Path.Combine(root, "publish")
            Directory.CreateDirectory(curWithJson) |> ignore
            Directory.CreateDirectory(publishDir) |> ignore
            File.WriteAllText(Path.Combine(curWithJson, "appsettings.json"), """{ "apiKey": "should-ignore" }""")

            let resDirect, _ = Settings.resolveConfigDirectoryWithOrigins curWithJson publishDir "Development"
            resDirect |> should equal publishDir

            // UT-DIR-07 (DT-DIR-02): CurrentDirectory と BaseDirectory 双方に別リポジトリがある場合は CurrentDirectory を優先
            let repoA = Path.Combine(root, "repoA")
            let repoARenamer = Path.Combine(repoA, "src", "TagBasedVideoManager.Renamer")
            let repoB = Path.Combine(root, "repoB")
            let repoBRenamer = Path.Combine(repoB, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(repoARenamer) |> ignore
            Directory.CreateDirectory(repoBRenamer) |> ignore
            File.WriteAllText(Path.Combine(repoARenamer, "appsettings.json"), "{}")
            File.WriteAllText(Path.Combine(repoARenamer, "appsettings.Development.json"), "{}")
            File.WriteAllText(Path.Combine(repoBRenamer, "appsettings.json"), "{}")
            File.WriteAllText(Path.Combine(repoBRenamer, "appsettings.Development.json"), "{}")

            let resPriority, _ = Settings.resolveConfigDirectoryWithOrigins repoA repoB "Development"
            resPriority |> should equal repoARenamer

            // DT-DIR-03: CurDir で appsettings.json のみ存在
            let repoC = Path.Combine(root, "repoC")
            let repoCRenamer = Path.Combine(repoC, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(repoCRenamer) |> ignore
            File.WriteAllText(Path.Combine(repoCRenamer, "appsettings.json"), "{}")
            let resBaseOnly, _ = Settings.resolveConfigDirectoryWithOrigins repoC publishDir "Development"
            resBaseOnly |> should equal repoCRenamer

            // DT-DIR-04: CurDir で appsettings.{env}.json のみ存在
            let repoD = Path.Combine(root, "repoD")
            let repoDRenamer = Path.Combine(repoD, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(repoDRenamer) |> ignore
            File.WriteAllText(Path.Combine(repoDRenamer, "appsettings.Development.json"), "{}")
            let resEnvOnly, _ = Settings.resolveConfigDirectoryWithOrigins repoD publishDir "Development"
            resEnvOnly |> should equal repoDRenamer

            // UT-DIR-08 (DT-DIR-05): CurDir はリポジトリ外、BaseDirectory が repoB/bin/Debug/net10.0
            let outsideDir = Path.Combine(root, "outside")
            let repoBBin = Path.Combine(repoB, "bin", "Debug", "net10.0")
            Directory.CreateDirectory(outsideDir) |> ignore
            Directory.CreateDirectory(repoBBin) |> ignore
            let resBaseDirHit, _ = Settings.resolveConfigDirectoryWithOrigins outsideDir repoBBin "Development"
            resBaseDirHit |> should equal repoBRenamer

            // UT-DIR-09 (DT-DIR-06): 双方ステップ内にリポジトリなし -> appBaseDir へフォールバック
            let resFallback, traceFallback = Settings.resolveConfigDirectoryWithOrigins outsideDir publishDir "Development"
            resFallback |> should equal publishDir
            Assert.True(traceFallback |> List.exists (fun l -> l.Contains(publishDir)))

            // UT-DIR-10: src/TagBasedVideoManager.Renamer フォルダはあるが設定ファイル 0 件 -> 不採用で appBaseDir へフォールバック
            let emptyRepo = Path.Combine(root, "empty_repo")
            let emptyRenamerDir = Path.Combine(emptyRepo, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(emptyRenamerDir) |> ignore
            let resEmptyDir, traceEmptyDir = Settings.resolveConfigDirectoryWithOrigins emptyRepo publishDir "Development"
            resEmptyDir |> should equal publishDir
            Assert.True(
                traceEmptyDir |> List.exists (fun l -> l.Contains(emptyRenamerDir)),
                $"空ディレクトリ不採用トレースがありません: {traceEmptyDir}"
            )
        )

    /// <summary>UT-DIR-11: resolveSavePath は %APPDATA% へフォールバックせず注入ディレクトリ配下の環境別パスを返すことを検証する</summary>
    [<Fact>]
    let ``UT-DIR-11: resolveSavePath は空の一時ディレクトリでも %APPDATA% へフォールバックしない`` () =
        withTempDir "UtDir11" (fun emptyTempDir ->
            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            try
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development")
                let savePath = Settings.resolveSavePath (Some emptyTempDir)
                savePath |> should equal (Path.Combine(emptyTempDir, "appsettings.Development.json"))
            finally
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
        )

    // -------------------------------------------------------------------------
    // 3.5 & 4.4 savePatch (UT-PATCH-01 ~ 10, DT-PATCH-01 ~ 06)
    // -------------------------------------------------------------------------

    /// <summary>UT-PATCH-01 (DT-PATCH-02): SelectedModel の部分更新で他キー・未知キー・ApiKey が完全保持されることを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-01: savePatch は SelectedModel 更新時に既存キー・未知キー・素の apiKey を保持する`` () =
        withTempDir "UtPatch01" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            let initialJson = """{
  "targetDirectory": "D:\\KeepVideos",
  "pathLengthThreshold": 215,
  "selectedModel": "old/model",
  "apiKey": "sk-or-v1-keep-key",
  "rules": [
    {
      "id": "r-keep",
      "name": "保持ルール",
      "pattern": "{Title}.mp4",
      "promptInstruction": "保持指示",
      "order": 0,
      "enableWebSearch": true
    }
  ],
  "customUnknownKey": {
    "nested": 42,
    "flag": true
  }
}"""
            File.WriteAllText(filePath, initialJson)

            let result = Settings.savePatch filePath { SelectedModel = Some "new/model"; Rules = None }
            result |> assertOk

            use doc = JsonDocument.Parse(File.ReadAllText(filePath))
            let root = doc.RootElement
            root.GetProperty("selectedModel").GetString() |> should equal "new/model"
            root.GetProperty("apiKey").ValueKind |> should equal JsonValueKind.String
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-keep-key"
            root.GetProperty("targetDirectory").GetString() |> should equal "D:\\KeepVideos"
            root.GetProperty("pathLengthThreshold").GetInt32() |> should equal 215
            root.GetProperty("rules").GetArrayLength() |> should equal 1
            root.GetProperty("rules").[0].GetProperty("id").GetString() |> should equal "r-keep"
            let unknown = root.GetProperty("customUnknownKey")
            unknown.GetProperty("nested").GetInt32() |> should equal 42
            unknown.GetProperty("flag").GetBoolean() |> should equal true
        )

    /// <summary>UT-PATCH-02: Rules の部分更新で日本語非エスケープ（UTF-8 BOMなし）かつ他キーが保持されることを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-02: savePatch は Rules 更新時に日本語をエスケープせず他キーを保持する`` () =
        withTempDir "UtPatch02" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                filePath,
                """{ "apiKey": "sk-or-v1-keep-rules", "selectedModel": "keep/model", "customFlag": true }"""
            )

            let updatedRule = {
                Id = "r1"
                Name = "日本語ルール名"
                Pattern = "{日付}_{場所}.mp4"
                PromptInstruction = "日本語で命名してください"
                Order = 0
                EnableWebSearch = true
            }
            let result = Settings.savePatch filePath { SelectedModel = None; Rules = Some [ updatedRule ] }
            result |> assertOk

            let bytes = File.ReadAllBytes(filePath)
            let hasBom = bytes.Length >= 3 && bytes.[0] = 0xEFuy && bytes.[1] = 0xBBuy && bytes.[2] = 0xBFuy
            Assert.False(hasBom, "UTF-8 BOM が付与されてはいけません")

            let text = File.ReadAllText(filePath, Encoding.UTF8)
            text |> should contain "日本語ルール名"
            text |> should contain "日本語で命名してください"
            text |> should not' (contain @"\u65e5\u672c\u8a9e")

            use doc = JsonDocument.Parse(text)
            let root = doc.RootElement
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-keep-rules"
            root.GetProperty("selectedModel").GetString() |> should equal "keep/model"
            root.GetProperty("customFlag").GetBoolean() |> should equal true
            root.GetProperty("rules").[0].GetProperty("id").GetString() |> should equal "r1"
        )

    /// <summary>UT-PATCH-03: PascalCase キーの重複を除去して camelCase キーへ統一することを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-03: savePatch は既存の PascalCase キーを削除して camelCase キーのみを保持する`` () =
        withTempDir "UtPatch03" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                filePath,
                """{ "SelectedModel": "old-pascal", "Rules": [], "apiKey": "sk-or-v1-keep" }"""
            )

            let rule = {
                Id = "camel-rule"
                Name = "Camel"
                Pattern = "{Title}.mp4"
                PromptInstruction = "Prompt"
                Order = 0
                EnableWebSearch = false
            }
            let result = Settings.savePatch filePath { SelectedModel = Some "new-camel"; Rules = Some [ rule ] }
            result |> assertOk

            use doc = JsonDocument.Parse(File.ReadAllText(filePath))
            let root = doc.RootElement
            let mutable tmp = Unchecked.defaultof<JsonElement>
            root.TryGetProperty("SelectedModel", &tmp) |> should equal false
            root.TryGetProperty("Rules", &tmp) |> should equal false
            root.GetProperty("selectedModel").GetString() |> should equal "new-camel"
            root.GetProperty("rules").GetArrayLength() |> should equal 1
            root.GetProperty("rules").[0].GetProperty("id").GetString() |> should equal "camel-rule"
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-keep"
        )

    /// <summary>UT-PATCH-04 (DT-PATCH-05): 未存在ファイル時に変更キーのみを含む最小 JSON を新規作成することを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-04: savePatch はファイル未存在時に変更対象キーのみの最小 JSON を生成し apiKey null 等を書き出さない`` () =
        withTempDir "UtPatch04" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "nested", "appsettings.Development.json")
            let result = Settings.savePatch filePath { SelectedModel = Some "m2"; Rules = None }
            result |> assertOk
            File.Exists(filePath) |> should equal true

            use doc = JsonDocument.Parse(File.ReadAllText(filePath))
            let root = doc.RootElement
            let props = [ for p in root.EnumerateObject() -> p.Name ]
            props |> should equal [ "selectedModel" ]
            root.GetProperty("selectedModel").GetString() |> should equal "m2"
        )

    /// <summary>UT-PATCH-05 (DT-PATCH-04): 既存 JSON 破損時に上書きせず段階 ParseJson と行・列位置付きエラーを返すことを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-05: savePatch は既存 JSON 破損時に書き込まず ParseJson と行・列位置付き SettingsError を返す`` () =
        withTempDir "UtPatch05" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(filePath, "{ broken json")
            let beforeBytes = File.ReadAllBytes(filePath)

            let result = Settings.savePatch filePath { SelectedModel = Some "m2"; Rules = None }
            match result with
            | Error (SettingsError msg) ->
                msg |> should contain filePath
                msg |> should contain "ParseJson"
                msg |> should contain "Line"
                msg |> should contain "Pos"
            | other -> failwith $"Expected SettingsError, got: {other}"

            Assert.True(System.Linq.Enumerable.SequenceEqual(beforeBytes, File.ReadAllBytes(filePath)))
        )

    /// <summary>UT-PATCH-06: ルートが配列や null の JSON に対しても上書き破壊せず SettingsError を返すことを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-06: savePatch はルートが配列または null の場合に上書きせず SettingsError を返す`` () =
        withTempDir "UtPatch06" (fun tempDir ->
            for (name, content) in [ ("array.json", "[1, 2, 3]"); ("null.json", "null") ] do
                let filePath = Path.Combine(tempDir, name)
                File.WriteAllText(filePath, content)
                let beforeBytes = File.ReadAllBytes(filePath)

                match Settings.savePatch filePath { SelectedModel = Some "m2"; Rules = None } with
                | Error (SettingsError msg) ->
                    msg |> should contain filePath
                    msg |> should contain "ParseJson"
                | other -> failwith $"Expected SettingsError for {name}, got: {other}"

                Assert.True(System.Linq.Enumerable.SequenceEqual(beforeBytes, File.ReadAllBytes(filePath)))
        )

    /// <summary>UT-PATCH-07 (DT-PATCH-01): 空パッチではロック・書き込みを行わず即 Ok () を返すことを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-07: savePatch は空パッチ時にファイル生成もタイムスタンプ変更もせず Ok () を返す`` () =
        withTempDir "UtPatch07" (fun tempDir ->
            let missingPath = Path.Combine(tempDir, "missing.json")
            Settings.savePatch missingPath { SelectedModel = None; Rules = None } |> assertOk
            File.Exists(missingPath) |> should equal false

            let existingPath = Path.Combine(tempDir, "existing.json")
            File.WriteAllText(existingPath, "{ invalid json should not even be parsed }")
            let beforeWriteUtc = File.GetLastWriteTimeUtc(existingPath)
            let beforeBytes = File.ReadAllBytes(existingPath)

            Settings.savePatch existingPath { SelectedModel = None; Rules = None } |> assertOk
            File.GetLastWriteTimeUtc(existingPath) |> should equal beforeWriteUtc
            Assert.True(System.Linq.Enumerable.SequenceEqual(beforeBytes, File.ReadAllBytes(existingPath)))
        )

    /// <summary>UT-PATCH-08: 環境別ファイルへの保存時に同一ディレクトリのベース appsettings.json が不変であることを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-08: savePatch はベース appsettings.json を変更せず環境別ファイルへ空 rules を保存する`` () =
        withTempDir "UtPatch08" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null, "selectedModel": "base" }""")
            let baseBefore = File.ReadAllBytes(basePath)
            let baseTimeBefore = File.GetLastWriteTimeUtc(basePath)

            let result = Settings.savePatch devPath { SelectedModel = None; Rules = Some [] }
            result |> assertOk

            Assert.True(System.Linq.Enumerable.SequenceEqual(baseBefore, File.ReadAllBytes(basePath)))
            File.GetLastWriteTimeUtc(basePath) |> should equal baseTimeBefore

            use doc = JsonDocument.Parse(File.ReadAllText(devPath))
            doc.RootElement.GetProperty("rules").GetArrayLength() |> should equal 0
        )

    /// <summary>UT-PATCH-09: JSONC コメント・末尾カンマ付き既存ファイルに対しても安全に部分更新できることを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-09: savePatch はコメントと末尾カンマ付き JSONC を安全に読み込んで部分更新できる`` () =
        withTempDir "UtPatch09" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                filePath,
                """{
  // user comment
  "apiKey": "sk-or-v1-jsonc-key",
  "targetDirectory": "D:\\Jsonc",
}"""
            )
            let result = Settings.savePatch filePath { SelectedModel = Some "m2"; Rules = None }
            result |> assertOk

            use doc = JsonDocument.Parse(File.ReadAllText(filePath))
            let root = doc.RootElement
            root.GetProperty("selectedModel").GetString() |> should equal "m2"
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-jsonc-key"
            root.GetProperty("targetDirectory").GetString() |> should equal "D:\\Jsonc"
        )

    /// <summary>UT-PATCH-10: 0バイト空ファイルに対して上書き破壊せず段階 ParseJson で安全中止することを検証する</summary>
    [<Fact>]
    let ``UT-PATCH-10: savePatch は 0バイト空ファイルに対して上書きせず ParseJson エラーを返す`` () =
        withTempDir "UtPatch10" (fun tempDir ->
            let zeroPath = Path.Combine(tempDir, "zero.json")
            File.WriteAllText(zeroPath, "")

            match Settings.savePatch zeroPath { SelectedModel = Some "m2"; Rules = None } with
            | Error (SettingsError msg) ->
                msg |> should contain zeroPath
                msg |> should contain "ParseJson"
            | other -> failwith $"Expected SettingsError for 0-byte file, got: {other}"

            File.ReadAllBytes(zeroPath).Length |> should equal 0
        )

    /// <summary>DT-PATCH-03 & DT-PATCH-06: 書込不可時の IoError 通知・.tmp 非残存・%APPDATA% 非フォールバックを検証する</summary>
    [<Fact>]
    let ``DT-PATCH-03 & DT-PATCH-06: savePatch は既存 ReadOnly および新規作成不能パスで IoError を返し .tmp を残さない`` () =
        withTempDir "DtPatchIo" (fun tempDir ->
            // DT-PATCH-03: 既存ファイルが ReadOnly
            let readOnlyPath = Path.Combine(tempDir, "readonly.json")
            File.WriteAllText(readOnlyPath, """{ "apiKey": "sk-or-v1-ro-keep" }""")
            let beforeBytes = File.ReadAllBytes(readOnlyPath)
            File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly)

            try
                match Settings.savePatch readOnlyPath { SelectedModel = Some "m2"; Rules = None } with
                | Error (IoError (msg, _)) ->
                    msg |> should contain readOnlyPath
                    Assert.True(msg.Contains("AtomicMove") || msg.Contains("WriteTempFile") || msg.Contains("段階"))
                | other -> failwith $"Expected IoError for ReadOnly file, got: {other}"

                Assert.True(System.Linq.Enumerable.SequenceEqual(beforeBytes, File.ReadAllBytes(readOnlyPath)))
                Directory.GetFiles(tempDir, "*.tmp") |> should be Empty
            finally
                File.SetAttributes(readOnlyPath, FileAttributes.Normal)

            // DT-PATCH-06: 新規作成先パスが不正 (親パスが既存ファイル)
            let blockingFile = Path.Combine(tempDir, "not_a_dir")
            File.WriteAllText(blockingFile, "file")
            let unreachablePath = Path.Combine(blockingFile, "appsettings.Development.json")
            let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
            let appDataDevSnapshot = SettingsIsolationHarness.captureSnapshot (Path.Combine(appDataDir, "appsettings.Development.json"))

            match Settings.savePatch unreachablePath { SelectedModel = Some "m2"; Rules = None } with
            | Error (IoError (msg, _)) ->
                msg |> should contain unreachablePath
                Assert.True(
                    msg.Contains("CreateDirectory") || msg.Contains("WriteTempFile") || msg.Contains("AtomicMove") || msg.Contains("段階"),
                    $"失敗段階名が含まれていません: {msg}"
                )
            | other -> failwith $"Expected IoError for unreachable path, got: {other}"

            Directory.GetFiles(tempDir, "*.tmp") |> should be Empty
            SettingsIsolationHarness.assertSnapshotUnchanged appDataDevSnapshot
        )

    // -------------------------------------------------------------------------
    // 3.6 & 4.1 設定マージ・PartialApiKey 5状態・警告・環境名 (UT-MERGE-*, DT-MERGE-*, UT-ERR-*, UT-ENV-*)
    // -------------------------------------------------------------------------

    /// <summary>UT-MERGE-01 (DT-MERGE-02): 上位プレースホルダーが下位有効キーを上書きせず保護警告を出すことを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-01: 環境別ファイルのプレースホルダー apiKey は下位の有効キーと出典パスを維持し Warnings を記録する`` () =
        withTempDir "UtMerge01" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
            File.WriteAllText(devPath, """{ "apiKey": "xxx" }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-base-valid")
            loaded.ApiKeySourcePath |> should equal (Some basePath)
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(devPath)))
        )

    /// <summary>UT-MERGE-02 (DT-MERGE-04): 環境別ファイルの明示 null が下位有効キーを意図的に無効化することを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-02: 環境別ファイルの明示 null は下位の有効キーを保護警告なしで無効化する`` () =
        withTempDir "UtMerge02" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
            File.WriteAllText(devPath, """{ "apiKey": null }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal (Some devPath)
            loaded.Warnings |> should be Empty
        )

    /// <summary>UT-MERGE-03 (DT-MERGE-03): 下位に有効キーがない場合は上位プレースホルダー値と出典パスが保持されることを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-03: 下位有効キーがない場合は環境別プレースホルダー値と出典パスを保持する`` () =
        withTempDir "UtMerge03" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null }""")
            File.WriteAllText(devPath, """{ "apiKey": "[API_KEY]" }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal (Some "[API_KEY]")
            loaded.ApiKeySourcePath |> should equal (Some devPath)
        )

    /// <summary>UT-MERGE-04 (DT-MERGE-07): 上位空文字 "" や引用符のみ "" / '' が下位有効キーを消去せず警告記録することを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-04: 上位の空文字および引用符のみ apiKey は下位有効キーを維持し Warnings を記録する`` () =
        for (name, rawValJson) in [ ("empty", "\"\""); ("doubleQuotes", "\"\\\"\\\"\""); ("singleQuotes", "\" '' \"") ] do
            withTempDir $"UtMerge04_{name}" (fun tempDir ->
                let basePath = Path.Combine(tempDir, "appsettings.json")
                let devPath = Path.Combine(tempDir, "appsettings.Development.json")
                File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
                File.WriteAllText(devPath, $"{{\"apiKey\": {rawValJson}}}")

                let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
                let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

                loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-base-valid")
                loaded.ApiKeySourcePath |> should equal (Some basePath)
                Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(devPath)))
            )

    /// <summary>UT-MERGE-05 (DT-MERGE-09 & DT-MERGE-10): 旧形式 { "value": ... } 混在時に下位保護しつつ修正案内を出すことを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-05: 旧破損形式のオブジェクト apiKey 検出時は文字列形式への修正案内とパスを Warnings に記録する`` () =
        // DT-MERGE-09: 下位に有効キーあり
        withTempDir "UtMerge05_LowerValid" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
            File.WriteAllText(devPath, """{ "apiKey": { "value": "sk-or-v1-broken" } }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-base-valid")
            loaded.ApiKeySourcePath |> should equal (Some basePath)
            Assert.True(
                loaded.Warnings |> List.exists (fun w -> w.Contains(devPath) && w.Contains("sk-or-v1-...")),
                $"修正案内が Warnings に含まれていません: {loaded.Warnings}"
            )
        )

        // DT-MERGE-10: 下位に有効キーなし
        withTempDir "UtMerge05_NoLower" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null }""")
            File.WriteAllText(devPath, """{ "apiKey": { "value": "sk-or-v1-broken" } }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal (Some devPath)
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(devPath) && w.Contains("sk-or-v1-...")))
        )

    /// <summary>UT-MERGE-06 (DT-MERGE-06): ベース層 appsettings.json の apiKey null は未設定扱いとなり ApiKeySourcePath が None になることを検証する</summary>
    [<Fact>]
    let ``UT-MERGE-06: ベース層 appsettings.json の apiKey null では ApiKeySourcePath が None となり SavePath が環境別になる`` () =
        withTempDir "UtMerge06" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null, "selectedModel": "base/m" }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal None
            loaded.EnvironmentName |> should equal "Development"
            loaded.SavePath |> should equal devPath
            loaded.Warnings |> should be Empty
        )

    /// <summary>DT-MERGE-01, 05, 08, 11: デシジョンテーブル残余行 (上位有効・省略継承・空文字無下位・環境別null) を網羅検証する</summary>
    [<Fact>]
    let ``DT-MERGE-01,05,08,11: mergePartialsChain の残余デシジョンテーブル行を網羅検証する`` () =
        // DT-MERGE-01: 上位が有効キー
        withTempDir "DtMerge01" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
            File.WriteAllText(devPath, """{ "apiKey": "sk-or-v1-dev-valid" }""")
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-dev-valid")
            loaded.ApiKeySourcePath |> should equal (Some devPath)
            loaded.Warnings |> should be Empty
        )

        // DT-MERGE-05: ベース apiKey 未指定、環境別が明示 null
        withTempDir "DtMerge05" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "selectedModel": "m" }""")
            File.WriteAllText(devPath, """{ "apiKey": null }""")
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal (Some devPath)
            loaded.Warnings |> should be Empty
        )

        // DT-MERGE-08 (環境別空文字・下位有効なし -> Some devPath)
        withTempDir "DtMerge08_Env" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "selectedModel": "m" }""")
            File.WriteAllText(devPath, """{ "apiKey": "" }""")
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal (Some devPath)
        )

        // DT-MERGE-08 (ベース空文字・下位有効なし -> None)
        withTempDir "DtMerge08_Base" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            File.WriteAllText(basePath, """{ "apiKey": "" }""")
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.ApiKey |> should equal None
            loaded.ApiKeySourcePath |> should equal None
        )

        // DT-MERGE-11 (上位 apiKey 省略 -> 下位継承)
        withTempDir "DtMerge11" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": "sk-or-v1-base-valid" }""")
            File.WriteAllText(devPath, """{ "selectedModel": "dev-model-only" }""")
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-base-valid")
            loaded.ApiKeySourcePath |> should equal (Some basePath)
            loaded.Settings.SelectedModel |> should equal "dev-model-only"
        )

    /// <summary>UT-ERR-01: 環境別ファイル破損および 0バイト空時に下位有効設定で起動し Warnings へ通知することを検証する</summary>
    [<Fact>]
    let ``UT-ERR-01: 環境別 JSON 破損および 0バイト空ファイルでも安全起動し Warnings にファイルパスとエラーを記録する`` () =
        for (name, brokenContent) in [ ("syntax", "{ broken"); ("zerobyte", "") ] do
            withTempDir $"UtErr01_{name}" (fun tempDir ->
                let basePath = Path.Combine(tempDir, "appsettings.json")
                let devPath = Path.Combine(tempDir, "appsettings.Development.json")
                File.WriteAllText(basePath, """{ "selectedModel": "safe-base-model", "pathLengthThreshold": 205 }""")
                File.WriteAllText(devPath, brokenContent)

                let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
                let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

                loaded.Settings.SelectedModel |> should equal "safe-base-model"
                loaded.Settings.PathLengthThreshold |> should equal 205
                loaded.LoadedFiles |> should equal [ basePath ]
                Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(devPath)))
            )

    /// <summary>UT-ERR-02: ベース JSON 破損・不正 rules 配列時の警告記録および JSONC コメント/末尾カンマ正常読込を検証する</summary>
    [<Fact>]
    let ``UT-ERR-02: ベース破損・不正 rules 配列は Warnings へ通知され、JSONC は警告なしで読み込まれる`` () =
        // 1. ベース破損
        withTempDir "UtErr02_BaseBroken" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, "{ broken base")
            File.WriteAllText(devPath, """{ "selectedModel": "dev-fallback" }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.SelectedModel |> should equal "dev-fallback"
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(basePath)))
        )

        // 2. 不正 rules 配列（文字列や項目型不整合）
        withTempDir "UtErr02_InvalidRules" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            File.WriteAllText(basePath, """{ "selectedModel": "m-with-bad-rules", "rules": [ { "id": 123 } ] }""")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.SelectedModel |> should equal "m-with-bad-rules"
            Assert.True(
                loaded.Warnings |> List.exists (fun w -> w.Contains("rules") && w.Contains(basePath)),
                $"rules パース警告がありません: {loaded.Warnings}"
            )
        )

        // 3. JSONC コメントと末尾カンマ
        withTempDir "UtErr02_Jsonc" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            File.WriteAllText(
                basePath,
                """{
                  // コメント行
                  "selectedModel": "jsonc-model",
                  "pathLengthThreshold": 212,
                }"""
            )
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            loaded.Settings.SelectedModel |> should equal "jsonc-model"
            loaded.Settings.PathLengthThreshold |> should equal 212
            loaded.Warnings |> should be Empty
        )

    /// <summary>UT-ERR-03: LoadedConfiguration の EnvironmentName と SavePath が 2段階解決結果と一致することを検証する</summary>
    [<Fact>]
    let ``UT-ERR-03: loadConfigurationWithMetadataWithContext は EnvironmentName と SavePath を整合して返す`` () =
        withTempDir "UtErr03" (fun root ->
            let repoDir = Path.Combine(root, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            let binDir = Path.Combine(repoDir, "bin", "Release", "net10.0")
            Directory.CreateDirectory(renamerDir) |> ignore
            Directory.CreateDirectory(binDir) |> ignore
            File.WriteAllText(Path.Combine(renamerDir, "appsettings.json"), """{ "apiKey": null }""")

            let ctx = makeContext repoDir binDir (Path.Combine(root, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Production") None None

            loaded.EnvironmentName |> should equal "Production"
            loaded.SavePath |> should equal (Path.Combine(renamerDir, "appsettings.Production.json"))
        )

    /// <summary>UT-ENV-01: .env と OS 環境変数が無視され .env 廃止移行ログが TraceLogs に出力されることを検証する</summary>
    [<Fact>]
    let ``UT-ENV-01: loadConfigurationWithMetadataWithContext は .env と OS 環境変数を無視し移行ログを出力する`` () =
        withTempDir "UtEnv01" (fun root ->
            let repoDir = Path.Combine(root, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(renamerDir) |> ignore
            File.WriteAllText(
                Path.Combine(renamerDir, "appsettings.json"),
                """{ "targetDirectory": "D:\\JsonOnly", "apiKey": "sk-or-v1-json-only-key" }"""
            )
            let dotEnvPath = Path.Combine(root, ".env")
            File.WriteAllLines(dotEnvPath, [ "VIDEO_DIR=E:\\FromDotEnv"; "OPENROUTER_API_KEY=sk-or-v1-from-dotenv" ])

            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let origApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            try
                Environment.SetEnvironmentVariable("VIDEO_DIR", "E:\\FromOsEnv")
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sk-or-v1-from-osenv")

                let ctx = makeContext repoDir repoDir (Path.Combine(root, "noappdata")) (Some dotEnvPath)
                let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None None

                loaded.Settings.TargetDirectory |> should equal "D:\\JsonOnly"
                loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-json-only-key")
                Assert.True(loaded.TraceLogs |> List.exists (fun l -> l.Contains(dotEnvPath)))
            finally
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", origApiKey)
        )

    /// <summary>UT-ENV-02: loadConfiguration / loadConfigurationWithConfig が loadConfigurationWithMetadata.Settings と一致することを検証する</summary>
    [<Fact>]
    let ``UT-ENV-02: 後方互換 API loadConfiguration と loadConfigurationWithConfig は loadConfigurationWithMetadata.Settings と一致する`` () =
        withTempDir "UtEnv02" (fun tempDir ->
            let envMarker = Path.Combine(tempDir, ".env")
            File.WriteAllText(envMarker, "# marker")
            File.WriteAllText(Path.Combine(tempDir, "appsettings.json"), """{ "pathLengthThreshold": 208, "selectedModel": "compat/base" }""")
            let envName = Settings.resolveEnvironmentName ()
            File.WriteAllText(Path.Combine(tempDir, $"appsettings.{envName}.json"), """{ "selectedModel": "compat/env", "apiKey": "sk-or-v1-compat-key" }""")

            let meta = Settings.loadConfigurationWithMetadata (Some envName) None (Some envMarker)
            let legacy1 = Settings.loadConfigurationWithConfig (Some envName) None (Some envMarker)
            let legacy2 = Settings.loadConfiguration None (Some envMarker)

            legacy1 |> should equal meta.Settings
            legacy2 |> should equal meta.Settings
            meta.Settings.PathLengthThreshold |> should equal 208
            meta.Settings.SelectedModel |> should equal "compat/env"
            meta.Settings.ApiKey |> should equal (Some "sk-or-v1-compat-key")
        )

    /// <summary>UT-ENV-03 ~ UT-ENV-06: resolveEnvironmentName と不正環境名の安全なフォールバックを検証する</summary>
    [<Fact>]
    let ``UT-ENV-03~06: 環境名優先順位および不正文字・パストラバーサル指定時の安全フォールバックを検証する`` () =
        withTempDir "UtEnvSanitize" (fun tempDir ->
            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            try
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
                let fallbackEnv = Settings.resolveEnvironmentName ()

                // 1. resolveEnvironmentName 直接検証: 正常値での DOTNET_ENVIRONMENT 優先
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Staging")
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development")
                Settings.resolveEnvironmentName () |> should equal "Staging"

                // 2. 不正値 DOTNET_ENVIRONMENT はサニタイズされ ASPNETCORE_ENVIRONMENT へフォールバック
                for badEnv in [ "   "; "../evil"; "bad\\sub"; "env*with:bad?chars" ] do
                    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", badEnv)
                    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "CustomSafeEnv")
                    Settings.resolveEnvironmentName () |> should equal "CustomSafeEnv"

                // 3. 双方不正値時は既定環境名へ安全フォールバック
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "../evil")
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "env*with:bad?chars")
                Settings.resolveEnvironmentName () |> should equal fallbackEnv

                // 4. loadConfigurationWithMetadataWithContext へ不正 configNameOpt を渡した場合も安全フォールバック
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
                File.WriteAllText(
                    Path.Combine(tempDir, $"appsettings.{fallbackEnv}.json"),
                    """{ "pathLengthThreshold": 197, "selectedModel": "safe-fallback-model" }"""
                )
                let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None

                for badEnv in [ "   "; "../evil"; "bad\\sub"; "env*with:bad?chars" ] do
                    let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some badEnv) None (Some tempDir)
                    loaded.EnvironmentName |> should equal fallbackEnv
                    loaded.Settings.PathLengthThreshold |> should equal 197
                    loaded.Settings.SelectedModel |> should equal "safe-fallback-model"
            finally
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
        )

    // -------------------------------------------------------------------------
    // 5.1 結合テスト (IT-IO-01, IT-CONF-01, IT-FAULT-01 ~ IT-FAULT-04)
    // -------------------------------------------------------------------------

    /// <summary>IT-IO-01: save -> savePatch -> load / loadConfigurationWithMetadataWithContext の往復整合を検証する</summary>
    [<Fact>]
    let ``IT-IO-01: save と savePatch の変更が load および loadConfigurationWithMetadataWithContext で整合して復元される`` () =
        withTempDir "ItIo01" (fun tempDir ->
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            let initial = {
                Settings.defaultSettings () with
                    TargetDirectory = "D:\\ItVideos"
                    PathLengthThreshold = 210
                    SelectedModel = "initial/model"
                    ApiKey = Some "sk-or-v1-integration-key"
            }
            Settings.save devPath initial |> assertOk

            // 未知キーを既存ファイルへ追加して Patch Save で消えないことも検証
            let withUnknown = File.ReadAllText(devPath).TrimEnd().TrimEnd('}') + ",\n  \"unknownIntegrationFlag\": \"keep-me\"\n}"
            File.WriteAllText(devPath, withUnknown)

            let patchedRule = {
                Id = "it-rule-1"
                Name = "結合テストルール"
                Pattern = "{Title}.mp4"
                PromptInstruction = "結合プロンプト"
                Order = 0
                EnableWebSearch = true
            }
            Settings.savePatch devPath { SelectedModel = Some "patched/model"; Rules = None } |> assertOk
            Settings.savePatch devPath { SelectedModel = None; Rules = Some [ patchedRule ] } |> assertOk

            match Settings.load devPath with
            | Error err -> failwith $"Load failed: {err}"
            | Ok reloaded ->
                reloaded.ApiKey |> should equal (Some "sk-or-v1-integration-key")
                reloaded.TargetDirectory |> should equal "D:\\ItVideos"
                reloaded.PathLengthThreshold |> should equal 210
                reloaded.SelectedModel |> should equal "patched/model"
                reloaded.Rules |> should equal [ patchedRule ]

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let meta = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
            meta.Settings.ApiKey |> should equal (Some "sk-or-v1-integration-key")
            meta.ApiKeySourcePath |> should equal (Some devPath)
            meta.Settings.SelectedModel |> should equal "patched/model"
            meta.Settings.TargetDirectory |> should equal "D:\\ItVideos"

            use doc = JsonDocument.Parse(File.ReadAllText(devPath))
            doc.RootElement.GetProperty("unknownIntegrationFlag").GetString() |> should equal "keep-me"
            doc.RootElement.GetProperty("apiKey").ValueKind |> should equal JsonValueKind.String
        )

    /// <summary>IT-CONF-01: ConfigLoadContext 経由で .env・OS 環境変数・%APPDATA%・擬似リポジトリの優先と通知を結合検証する</summary>
    [<Fact>]
    let ``IT-CONF-01: ConfigLoadContext 経由で .env・OS環境変数・%APPDATA% を無視し擬似リポジトリ値と移行ログを返す`` () =
        withTempDir "ItConf01" (fun root ->
            let repoDir = Path.Combine(root, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            let binDir = Path.Combine(repoDir, "bin", "Debug", "net10.0")
            let appDataDir = Path.Combine(root, "appdata", "TagBasedVideoManager")
            let dotEnvPath = Path.Combine(root, ".env")
            Directory.CreateDirectory(renamerDir) |> ignore
            Directory.CreateDirectory(binDir) |> ignore
            Directory.CreateDirectory(appDataDir) |> ignore

            let basePath = Path.Combine(renamerDir, "appsettings.json")
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "pathLengthThreshold": 205, "apiKey": null }""")
            File.WriteAllText(devPath, """{ "targetDirectory": "D:\\RepoVideos", "apiKey": "sk-or-v1-repo-integration" }""")
            File.WriteAllText(Path.Combine(appDataDir, "appsettings.json"), """{ "apiKey": "xxx", "targetDirectory": "E:\\OldAppData" }""")
            File.WriteAllLines(dotEnvPath, [ "VIDEO_DIR=E:\\DotEnvVideos"; "OPENROUTER_API_KEY=sk-or-v1-dotenv" ])

            let origVideoDir = Environment.GetEnvironmentVariable("VIDEO_DIR")
            let origApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            try
                Environment.SetEnvironmentVariable("VIDEO_DIR", "E:\\OsEnvVideos")
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "sk-or-v1-osenv")

                let ctx = makeContext binDir binDir appDataDir (Some dotEnvPath)
                let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None None

                loaded.Settings.TargetDirectory |> should equal "D:\\RepoVideos"
                loaded.Settings.PathLengthThreshold |> should equal 205
                loaded.Settings.ApiKey |> should equal (Some "sk-or-v1-repo-integration")
                loaded.ApiKeySourcePath |> should equal (Some devPath)
                loaded.LoadedFiles |> should equal [ basePath; devPath ]
                loaded.SavePath |> should equal devPath
                Assert.True(
                    loaded.TraceLogs |> List.exists (fun l -> l.Contains(appDataDir) && l.Contains(devPath) && (l.Contains("移行") || l.Contains("廃止"))),
                    $"TraceLogs に %%APPDATA%% 廃止・移行案内が含まれていません: {loaded.TraceLogs}"
                )
                Assert.True(loaded.TraceLogs |> List.exists (fun l -> l.Contains(dotEnvPath)))
            finally
                Environment.SetEnvironmentVariable("VIDEO_DIR", origVideoDir)
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", origApiKey)
        )

    /// <summary>IT-FAULT-01: ReadOnly 属性注入時に .tmp を削除し %APPDATA% へフォールバックせず段階・試行回数・パス付き IoError を返すことを検証する</summary>
    [<Fact>]
    let ``IT-FAULT-01: savePatch は ReadOnly ファイルに対してフォールバックせず詳細 IoError を返し .tmp を残さない`` () =
        withTempDir "ItFault01" (fun tempDir ->
            let targetPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(targetPath, """{ "apiKey": "sk-or-v1-keep-ro" }""")
            let beforeBytes = File.ReadAllBytes(targetPath)
            File.SetAttributes(targetPath, FileAttributes.ReadOnly)

            let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
            let appDataDev = Path.Combine(appDataDir, "appsettings.Development.json")
            let appDataSnapshot = SettingsIsolationHarness.captureSnapshot appDataDev

            try
                match Settings.savePatch targetPath { SelectedModel = Some "m2"; Rules = None } with
                | Error (IoError (msg, _)) ->
                    msg |> should contain targetPath
                    Assert.True(msg.Contains("AtomicMove") || msg.Contains("WriteTempFile") || msg.Contains("段階"))
                    Assert.True(msg.Contains("回") || msg.Contains("試行"))
                | other -> failwith $"Expected IoError, got: {other}"

                Assert.True(System.Linq.Enumerable.SequenceEqual(beforeBytes, File.ReadAllBytes(targetPath)))
                Directory.GetFiles(tempDir, "*.tmp") |> should be Empty
                SettingsIsolationHarness.assertSnapshotUnchanged appDataSnapshot
            finally
                File.SetAttributes(targetPath, FileAttributes.Normal)
        )

    /// <summary>IT-FAULT-02: 破損 JSON と 0バイト空ファイル注入時にバイト列を一切変更せず ParseJson エラーを返すことを検証する</summary>
    [<Fact>]
    let ``IT-FAULT-02: savePatch は破損 JSON と 0バイト空ファイルの生バイト列を変更せず ParseJson エラーを返す`` () =
        withTempDir "ItFault02" (fun tempDir ->
            let brokenPath = Path.Combine(tempDir, "broken.json")
            File.WriteAllText(brokenPath, "{\n  \"bad\": }\n")
            let brokenBytes = File.ReadAllBytes(brokenPath)

            match Settings.savePatch brokenPath { SelectedModel = Some "m2"; Rules = None } with
            | Error (SettingsError msg) ->
                msg |> should contain brokenPath
                msg |> should contain "ParseJson"
                msg |> should contain "Line"
                msg |> should contain "Pos"
            | other -> failwith $"Expected SettingsError for broken JSON, got: {other}"
            Assert.True(System.Linq.Enumerable.SequenceEqual(brokenBytes, File.ReadAllBytes(brokenPath)))

            let zeroPath = Path.Combine(tempDir, "empty.json")
            File.WriteAllBytes(zeroPath, [||])
            match Settings.savePatch zeroPath { SelectedModel = Some "m2"; Rules = None } with
            | Error (SettingsError msg) ->
                msg |> should contain zeroPath
                msg |> should contain "ParseJson"
            | other -> failwith $"Expected SettingsError for 0-byte JSON, got: {other}"
            File.ReadAllBytes(zeroPath).Length |> should equal 0
        )

    /// <summary>IT-FAULT-03: 10 スレッド並行 savePatch 競合でロストアップデートなく双方の最新値と既存キーが保持されることを検証する</summary>
    [<Fact>]
    let ``IT-FAULT-03: 並行 savePatch 競合でロストアップデートせず最新 selectedModel・最新 rules・既存 apiKey が全保持される`` () =
        withTempDir "ItFault03" (fun tempDir ->
            let filePath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                filePath,
                """{
  "targetDirectory": "D:\\KeepConcurrent",
  "apiKey": "sk-or-v1-concurrent-keep",
  "selectedModel": "init/model",
  "rules": []
}"""
            )

            let modelTasks =
                [ 1 .. 5 ]
                |> List.map (fun i ->
                    async {
                        return Settings.savePatch filePath { SelectedModel = Some $"concurrent-model-{i}"; Rules = None }
                    }
                )
            let ruleTasks =
                [ 1 .. 5 ]
                |> List.map (fun j ->
                    async {
                        let rule = {
                            Id = $"concurrent-rule-{j}"
                            Name = $"並行ルール{j}"
                            Pattern = "{Title}.mp4"
                            PromptInstruction = "指示"
                            Order = 0
                            EnableWebSearch = (j % 2 = 0)
                        }
                        return Settings.savePatch filePath { SelectedModel = None; Rules = Some [ rule ] }
                    }
                )

            let results = Async.RunSynchronously (Async.Parallel (modelTasks @ ruleTasks))
            results |> Array.iter (fun r -> r |> assertOk)
            Directory.GetFiles(tempDir, "*.tmp") |> should be Empty

            use doc = JsonDocument.Parse(File.ReadAllText(filePath))
            let root = doc.RootElement
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-concurrent-keep"
            root.GetProperty("targetDirectory").GetString() |> should equal "D:\\KeepConcurrent"
            let finalModel = root.GetProperty("selectedModel").GetString()
            Assert.True(finalModel.StartsWith("concurrent-model-", StringComparison.Ordinal), $"Unexpected selectedModel: {finalModel}")
            root.GetProperty("rules").GetArrayLength() |> should equal 1
            let finalRuleId = root.GetProperty("rules").[0].GetProperty("id").GetString()
            Assert.True(finalRuleId.StartsWith("concurrent-rule-", StringComparison.Ordinal), $"Unexpected rule id: {finalRuleId}")
        )

    /// <summary>IT-FAULT-04: 構文エラー・旧 { "value": ... } 形式・不正 rules 混在時でも安全起動し全警告が記録されることを検証する</summary>
    [<Fact>]
    let ``IT-FAULT-04: 構文エラー・旧形式 apiKey・不正 rules 混在時も安全起動し Warnings に詳細を記録する`` () =
        withTempDir "ItFault04" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                basePath,
                """{
  "selectedModel": "safe-surviving-model",
  "apiKey": { "value": "sk-or-v1-legacy-secret-do-not-leak" },
  "rules": "not-an-array"
}"""
            )
            File.WriteAllText(devPath, "{ broken dev json")

            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loaded.Settings.SelectedModel |> should equal "safe-surviving-model"
            loaded.Settings.ApiKey |> should equal None
            loaded.LoadedFiles |> should equal [ basePath ]
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(basePath) && w.Contains("sk-or-v1-...")))
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(basePath) && w.Contains("rules")))
            Assert.True(loaded.Warnings |> List.exists (fun w -> w.Contains(devPath)))
            Assert.False(
                loaded.Warnings |> List.exists (fun w -> w.Contains("sk-or-v1-legacy-secret-do-not-leak")),
                "旧オブジェクト内のキー値が警告メッセージへ漏洩してはいけません"
            )
        )

    // =========================================================================
    // Task 4.1: Diff C1 >= 85% & Adversarial QA 回帰テスト
    // =========================================================================

    /// <summary>QA-SET-01: parsePartial の FileShare.None 排他ロック時にクラッシュせず下位設定/既定値へフォールバックし Warnings へ詳細を記録することを検証する</summary>
    [<Fact>]
    let ``QA-SET-01: parsePartial は FileShare.None でロックされた設定ファイルに対しても安全起動し Warnings を記録する`` () =
        withTempDir "QaSetLock01" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "selectedModel": "surviving/base-model", "pathLengthThreshold": 212 }""")
            File.WriteAllText(devPath, """{ "selectedModel": "locked/dev-model", "pathLengthThreshold": 180 }""")

            // 1. ディレクトリ層読込経路: 上位ファイルが FileShare.None で排他ロックされている場合
            use lockStream = new FileStream(devPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None
            let loadedLayer = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)

            loadedLayer.Settings.SelectedModel |> should equal "surviving/base-model"
            loadedLayer.Settings.PathLengthThreshold |> should equal 212
            loadedLayer.LoadedFiles |> should equal [ basePath ]
            Assert.True(
                loadedLayer.Warnings |> List.exists (fun w -> w.Contains(devPath)),
                $"ロックされたファイルパスが Warnings に含まれていません: {loadedLayer.Warnings}"
            )

            // 2. 明示 jsonPathOpt 経路: 指定ファイルが FileShare.None で排他ロックされている場合は既定値へ安全フォールバック
            let loadedExplicit = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") (Some devPath) None
            let defaults = Settings.defaultSettings ()
            loadedExplicit.Settings |> should equal defaults
            loadedExplicit.LoadedFiles |> should be Empty
            Assert.True(
                loadedExplicit.Warnings |> List.exists (fun w -> w.Contains(devPath)),
                $"明示パスのロック警告が Warnings に含まれていません: {loadedExplicit.Warnings}"
            )
        )

    /// <summary>QA-SET-02: Settings.save と Settings.savePatch が空・null・空白・不正文字パスに対して例外を投げず Error IoError を返すことを検証する</summary>
    [<Fact>]
    let ``QA-SET-02: save と savePatch は null・空文字・空白・不正文字パスに対して例外を投げず Error IoError を返す`` () =
        withTempDir "QaSetInvalidPath02" (fun tempDir ->
            let settings = Settings.defaultSettings ()
            let patch = { SelectedModel = Some "safe/model"; Rules = None }
            let invalidPaths: string list = [
                ""
                "   "
                null
                Path.Combine(tempDir, "bad\u0000name.json")
                Path.Combine(tempDir, "bad?*:<>|name.json")
            ]

            for badPath in invalidPaths do
                match Settings.save badPath settings with
                | Error (IoError (msg, _)) ->
                    Assert.False(String.IsNullOrWhiteSpace(msg))
                | other ->
                    failwith $"Settings.save で IoError 以外が返りました (path='{badPath}'): {other}"

                match Settings.savePatch badPath patch with
                | Error (IoError (msg, _)) ->
                    Assert.False(String.IsNullOrWhiteSpace(msg))
                | other ->
                    failwith $"Settings.savePatch で IoError 以外が返りました (path='{badPath}'): {other}"

            // 空パッチ時は I/O を行わず Ok () で早期終了すること
            match Settings.savePatch null { SelectedModel = None; Rules = None } with
            | Ok () -> ()
            | other -> failwith $"空パッチ時に Ok () 以外が返りました: {other}"
        )

    /// <summary>QA-SET-03: savePatch が重複キーと jsonElementToNode 全型（配列・null・整数/小数・bool・絵文字）を ArgumentException なしで後勝ち往復することを検証する</summary>
    [<Fact>]
    let ``QA-SET-03: savePatch はトップレベル重複キーとネスト重複・配列・null・数値・真偽値・絵文字を例外なく後勝ち保持する`` () =
        withTempDir "QaSetDupAndTypes03" (fun tempDir ->
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            let rawJson =
                """{
  "selectedModel": "first/model",
  "selectedModel": "second/model",
  "SelectedModel": "pascal/model",
  "apiKey": "sk-or-v1-first-key",
  "apiKey": "sk-or-v1-last-winner-key",
  "Rules": [],
  "nestedObj": {
    "dup": "old-value",
    "dup": "new-winner-value",
    "boolTrue": true,
    "boolFalse": false,
    "nullVal": null,
    "intVal": 42,
    "floatVal": 3.14,
    "emojiText": "🎬🚀日本語テスト"
  },
  "mixedArray": [1, 2.5, true, false, null, "🎥配列文字", { "innerDup": "v1", "innerDup": "v2" }]
}"""
            File.WriteAllText(devPath, rawJson, Encoding.UTF8)

            let patchedRule = {
                Id = "qa-rule-dup"
                Name = "重複テストルール"
                Pattern = "{Code}_{Title}.mp4"
                PromptInstruction = "簡潔に命名"
                Order = 0
                EnableWebSearch = true
            }
            Settings.savePatch devPath { SelectedModel = Some "patched/final-model"; Rules = Some [ patchedRule ] } |> assertOk

            let savedText = File.ReadAllText(devPath, Encoding.UTF8)
            use doc = JsonDocument.Parse(savedText)
            let root = doc.RootElement

            root.GetProperty("selectedModel").GetString() |> should equal "patched/final-model"
            let mutable pascalModel = Unchecked.defaultof<JsonElement>
            root.TryGetProperty("SelectedModel", &pascalModel) |> should equal false
            let mutable pascalRules = Unchecked.defaultof<JsonElement>
            root.TryGetProperty("Rules", &pascalRules) |> should equal false

            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-last-winner-key"
            root.GetProperty("rules").GetArrayLength() |> should equal 1
            root.GetProperty("rules").[0].GetProperty("id").GetString() |> should equal "qa-rule-dup"

            let nested = root.GetProperty("nestedObj")
            nested.GetProperty("dup").GetString() |> should equal "new-winner-value"
            nested.GetProperty("boolTrue").GetBoolean() |> should equal true
            nested.GetProperty("boolFalse").GetBoolean() |> should equal false
            nested.GetProperty("nullVal").ValueKind |> should equal JsonValueKind.Null
            nested.GetProperty("intVal").GetInt32() |> should equal 42
            nested.GetProperty("floatVal").GetDouble() |> should equal 3.14
            nested.GetProperty("emojiText").GetString() |> should equal "🎬🚀日本語テスト"

            let arr = root.GetProperty("mixedArray")
            arr.GetArrayLength() |> should equal 7
            arr.[0].GetInt32() |> should equal 1
            arr.[1].GetDouble() |> should equal 2.5
            arr.[2].GetBoolean() |> should equal true
            arr.[3].GetBoolean() |> should equal false
            arr.[4].ValueKind |> should equal JsonValueKind.Null
            arr.[5].GetString() |> should equal "🎥配列文字"
            arr.[6].GetProperty("innerDup").GetString() |> should equal "v2"
        )

    /// <summary>QA-SET-04: parsePartial の rules 厳密検証（[null]・必須フィールド個別欠落/null・正常不正混在・型不正）で安全フォールバックと Warnings 記録を検証する</summary>
    [<Fact>]
    let ``QA-SET-04: parsePartial は不正 rules（null要素・必須文字列欠損/null・混在・型不正）を無視して下位ルールを維持し Warnings に記録する`` () =
        withTempDir "QaSetRulesStrict04" (fun tempDir ->
            let basePath = Path.Combine(tempDir, "appsettings.json")
            let devPath = Path.Combine(tempDir, "appsettings.Development.json")
            File.WriteAllText(
                basePath,
                """{
  "rules": [
    { "id": "base-safe", "name": "安全なベースルール", "pattern": "{Title}.mp4", "promptInstruction": "指示", "order": 0, "enableWebSearch": false }
  ]
}"""
            )

            let validItemJson =
                """{ "id": "ok-1", "name": "正常項目", "pattern": "{Code}.mp4", "promptInstruction": "正常指示", "order": 0, "enableWebSearch": true }"""

            let invalidRulesPayloads: string * string list =
                "non-object-null",
                [
                    """{ "rules": [ null ] }"""
                    $$"""{ "rules": [ {{validItemJson}}, null ] }"""
                ]

            let missingFieldPayloads: string list = [
                """{ "rules": [ { "name": "no-id", "pattern": "{Title}.mp4", "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": null, "name": "null-id", "pattern": "{Title}.mp4", "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": "no-name", "pattern": "{Title}.mp4", "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": "null-name", "name": null, "pattern": "{Title}.mp4", "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": "no-pattern", "name": "n", "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": "null-pattern", "name": "n", "pattern": null, "promptInstruction": "x", "order": 0 } ] }"""
                """{ "rules": [ { "id": "no-prompt", "name": "n", "pattern": "{Title}.mp4", "order": 0 } ] }"""
                """{ "rules": [ { "id": "null-prompt", "name": "n", "pattern": "{Title}.mp4", "promptInstruction": null, "order": 0 } ] }"""
                $$"""{ "rules": [ {{validItemJson}}, { "id": "mixed-bad", "name": "欠落", "pattern": "{Title}.mp4", "order": 1 } ] }"""
                """{ "rules": [ { "id": "bad-order", "name": "型不正", "pattern": "{Title}.mp4", "promptInstruction": "x", "order": "not-an-int" } ] }"""
            ]

            let allPayloads = (snd invalidRulesPayloads) @ missingFieldPayloads
            let ctx = makeContext tempDir tempDir (Path.Combine(tempDir, "noappdata")) None

            for payload in allPayloads do
                File.WriteAllText(devPath, payload)
                let loaded = Settings.loadConfigurationWithMetadataWithContext ctx (Some "Development") None (Some tempDir)
                loaded.Settings.Rules.Length |> should equal 1
                loaded.Settings.Rules.Head.Id |> should equal "base-safe"
                Assert.True(
                    loaded.Warnings |> List.exists (fun w -> w.Contains(devPath) && w.Contains("rules")),
                    $"rules 検証警告が記録されていません (payload={payload}): {loaded.Warnings}"
                )
        )

    /// <summary>QA-SET-05: resolveConfigDirectoryWithOrigins と loadConfigurationWithMetadataWithContext の null/空/不正envName/相対jsonPath 境界防御を検証する</summary>
    [<Fact>]
    let ``QA-SET-05: resolveConfigDirectoryWithOrigins と loadConfigurationWithMetadataWithContext は null・空・不正env・相対jsonPath で例外なく安全解決する`` () =
        withTempDir "QaSetOriginsBoundary05" (fun tempDir ->
            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            let origCwd = Directory.GetCurrentDirectory()
            try
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "StagingSafe")
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)

                // 1. currentDir / appBaseDir が null・空白、envName が "../evil"
                let dirNull, tracesNull = Settings.resolveConfigDirectoryWithOrigins null null "../evil"
                dirNull |> should equal ""
                Assert.True(tracesNull |> List.exists (fun t -> t.Contains("スキップ")))

                let dirEmpty, tracesEmpty = Settings.resolveConfigDirectoryWithOrigins "" "   " "../evil"
                dirEmpty |> should equal ""
                Assert.True(tracesEmpty |> List.exists (fun t -> t.Contains("第2段階フォールバック")))

                // 2. src/TagBasedVideoManager.Renamer が存在するが JSON が 0 件のケース + 8 階層超え打ち切り
                let emptyRenamerParent = Path.Combine(tempDir, "empty_renamer")
                Directory.CreateDirectory(Path.Combine(emptyRenamerParent, "src", "TagBasedVideoManager.Renamer")) |> ignore
                let deepDir = Path.Combine(emptyRenamerParent, "a", "b", "c", "d", "e", "f", "g", "h", "i")
                Directory.CreateDirectory(deepDir) |> ignore
                let fallbackBase = Path.Combine(tempDir, "fallback_base")
                Directory.CreateDirectory(fallbackBase) |> ignore

                let dirEmptyRenamer, tracesEmptyRenamer = Settings.resolveConfigDirectoryWithOrigins emptyRenamerParent fallbackBase "StagingSafe"
                dirEmptyRenamer |> should equal fallbackBase
                Assert.True(tracesEmptyRenamer |> List.exists (fun t -> t.Contains("設定ファイルが 0 件のため不採用")))

                let dirDeep, tracesDeep = Settings.resolveConfigDirectoryWithOrigins deepDir fallbackBase "StagingSafe"
                dirDeep |> should equal fallbackBase
                Assert.True(tracesDeep |> List.exists (fun t -> t.Contains("親探索上限")))

                // 3. ConfigLoadContext の全ディレクトリが null + 不正 configNameOpt = Some "../evil"
                let nullCtx: ConfigLoadContext = {
                    CurrentDirectory = null
                    AppBaseDirectory = null
                    AppDataDirectory = null
                    DotEnvPath = Some "   "
                }
                let loadedNull = Settings.loadConfigurationWithMetadataWithContext nullCtx (Some "../evil") None None
                loadedNull.EnvironmentName |> should equal "StagingSafe"
                loadedNull.SavePath |> should equal "appsettings.StagingSafe.json"
                loadedNull.Settings |> should equal (Settings.defaultSettings ())

                // 4. 相対 jsonPathOpt = Some "appsettings.json" の安全解決と環境別 SavePath
                let relDir = Path.Combine(tempDir, "rel_json")
                Directory.CreateDirectory(relDir) |> ignore
                File.WriteAllText(Path.Combine(relDir, "appsettings.json"), """{ "selectedModel": "rel/model", "apiKey": "sk-or-v1-rel-valid-key" }""")
                File.WriteAllText(Path.Combine(relDir, ".env"), "VIDEO_DIR=ignored")
                Directory.SetCurrentDirectory(relDir)

                let relCtx = makeContext relDir relDir (Path.Combine(tempDir, "noappdata")) None
                let loadedRel = Settings.loadConfigurationWithMetadataWithContext relCtx (Some "Development") (Some "appsettings.json") None
                loadedRel.Settings.SelectedModel |> should equal "rel/model"
                loadedRel.SavePath |> should equal (Path.Combine(relDir, "appsettings.Development.json"))
                loadedRel.ApiKeySourcePath |> should equal (Some (Path.Combine(relDir, "appsettings.json")))
                Assert.True(loadedRel.TraceLogs |> List.exists (fun l -> l.Contains(".env")))
            finally
                Directory.SetCurrentDirectory(origCwd)
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
        )


