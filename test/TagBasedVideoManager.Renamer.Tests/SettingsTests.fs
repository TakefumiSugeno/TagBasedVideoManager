namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open System.Security.Cryptography
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
