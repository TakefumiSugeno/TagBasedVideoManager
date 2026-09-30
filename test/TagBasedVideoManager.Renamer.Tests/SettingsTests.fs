namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module SettingsTests =

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
    let ``loadConfiguration は .env から OPENROUTER_API_KEY と VIDEO_DIR を取得できる`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerEnvTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let envPath = Path.Combine(tempDir, ".env")
        try
            File.WriteAllLines(envPath, [
                "PORT=5621"
                "VIDEO_DIR=E:\\EnvVideos"
                "OPENROUTER_API_KEY=sk-or-v1-from-env-test-key"
                "OPENROUTER_MODEL=google/gemini-2.0-flash-exp:free"
                "PATH_LENGTH_THRESHOLD=220"
            ])

            let loaded = Settings.loadConfiguration None (Some envPath)
            loaded.TargetDirectory |> should equal "E:\\EnvVideos"
            loaded.ApiKey |> should equal (Some "sk-or-v1-from-env-test-key")
            loaded.SelectedModel |> should equal "google/gemini-2.0-flash-exp:free"
            loaded.PathLengthThreshold |> should equal 220
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``loadConfiguration は appsettings.json の設定を .env より優先する`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerPriorityTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let jsonPath = Path.Combine(tempDir, "appsettings.json")
        let envPath = Path.Combine(tempDir, ".env")
        try
            File.WriteAllLines(envPath, [
                "VIDEO_DIR=E:\\FromEnv"
                "OPENROUTER_API_KEY=key-from-env"
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
    let ``resolveSavePath は BaseDirectory に appsettings.json があれば BaseDirectory を優先する`` () =
        let tempBaseDir = Path.Combine(Path.GetTempPath(), "RenamerBase_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempBaseDir) |> ignore
        let baseFile = Path.Combine(tempBaseDir, "appsettings.json")
        try
            File.WriteAllText(baseFile, "{}")
            let path = Settings.resolveSavePath (Some tempBaseDir)
            path |> should equal baseFile
        finally
            if Directory.Exists(tempBaseDir) then Directory.Delete(tempBaseDir, true)

    [<Fact>]
    let ``resolveSavePath は BaseDirectory に appsettings.json がなければ AppData パスを決定する`` () =
        let tempBaseDir = Path.Combine(Path.GetTempPath(), "RenamerBaseEmpty_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempBaseDir) |> ignore
        try
            let path = Settings.resolveSavePath (Some tempBaseDir)
            let expectedAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager", "appsettings.json")
            path |> should equal expectedAppData
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

    [<Fact>]
    let ``loadConfiguration は AppData にファイルがあってもプロジェクト直下の appsettings.json を優先ロードする`` () =
        // 引数なし loadConfiguration None None の実行時に、プロジェクトの appsettings.json がロードされることを検証
        let loaded = Settings.loadConfiguration None None
        // ユーザーが更新した設定（またはプロジェクト直下の設定）がロードされていること
        let rec findRoot (dir: DirectoryInfo) (depth: int) =
            if depth <= 0 || box dir = null then None
            else
                let candidate = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.json")
                if File.Exists(candidate) then Some candidate
                else findRoot dir.Parent (depth - 1)
        let current = DirectoryInfo(Directory.GetCurrentDirectory())
        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
        let projJsonPath =
            findRoot current 8
            |> Option.orElseWith (fun () -> findRoot baseDir 8)
            |> Option.defaultWith (fun () -> failwith "src/TagBasedVideoManager.Renamer/appsettings.json が見つかりません")

        let projJson = Settings.load projJsonPath
        match projJson with
        | Ok expected ->
            loaded.TargetDirectory |> should equal expected.TargetDirectory
            loaded.SelectedModel |> should equal expected.SelectedModel
            loaded.ApiKey |> should equal expected.ApiKey
        | Error err -> failwith $"Project json load failed: {err}"

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

    [<Fact>]
    let ``プロジェクト直下の appsettings.Debug.json と appsettings.Release.json は正しくデシリアライズできる`` () =
        let rec findRoot (dir: DirectoryInfo) (depth: int) =
            if depth <= 0 || box dir = null then None
            else
                let candidateDebug = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.Debug.json")
                if File.Exists(candidateDebug) then Some dir.FullName
                else findRoot dir.Parent (depth - 1)
        let current = DirectoryInfo(Directory.GetCurrentDirectory())
        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
        let rootDir =
            findRoot current 8
            |> Option.orElseWith (fun () -> findRoot baseDir 8)
            |> Option.defaultWith (fun () -> failwith "プロジェクトルートが見つかりません")

        let debugPath = Path.Combine(rootDir, "src", "TagBasedVideoManager.Renamer", "appsettings.Debug.json")
        let releasePath = Path.Combine(rootDir, "src", "TagBasedVideoManager.Renamer", "appsettings.Release.json")

        File.Exists(debugPath) |> should equal true
        File.Exists(releasePath) |> should equal true

        match Settings.load debugPath with
        | Ok debugSettings ->
            debugSettings.TargetDirectory |> should equal "test/videos"
            debugSettings.PathLengthThreshold |> should equal 240
            debugSettings.SelectedModel |> should not' (be EmptyString)
            debugSettings.ApiKey |> should equal (Some "[API_KEY]")
            debugSettings.Rules.Length |> should be (greaterThan 0)
        | Error err -> failwith $"Debug settings load failed: {err}"

        match Settings.load releasePath with
        | Ok releaseSettings ->
            releaseSettings.TargetDirectory |> should equal ""
            releaseSettings.PathLengthThreshold |> should equal 240
            releaseSettings.SelectedModel |> should not' (be EmptyString)
            releaseSettings.ApiKey |> should equal None
            releaseSettings.Rules.Length |> should be (greaterThan 0)
        | Error err -> failwith $"Release settings load failed: {err}"

    [<Fact>]
    let ``loadConfigurationWithConfig は Debug 構成時に appsettings.Debug.json を appsettings.json より優先ロードする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerDebugConfigTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let debugJsonPath = Path.Combine(tempDir, "appsettings.Debug.json")

        try
            let baseJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\BaseDir"
                    SelectedModel = "model-from-base"
            }
            let debugJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\DebugDir"
                    SelectedModel = "model-from-debug"
            }
            Settings.save baseJsonPath baseJson |> ignore
            Settings.save debugJsonPath debugJson |> ignore

            let loaded = Settings.loadConfigurationWithConfig (Some "Debug") None (Some dummyEnvPath)
            loaded.TargetDirectory |> should equal "C:\\DebugDir"
            loaded.SelectedModel |> should equal "model-from-debug"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``loadConfigurationWithConfig は Release 構成時に appsettings.Release.json を appsettings.json より優先ロードする`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerReleaseConfigTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let dummyEnvPath = Path.Combine(tempDir, ".env")
        File.WriteAllText(dummyEnvPath, "# dummy env\n")

        let baseJsonPath = Path.Combine(tempDir, "appsettings.json")
        let releaseJsonPath = Path.Combine(tempDir, "appsettings.Release.json")

        try
            let baseJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\BaseDir"
                    SelectedModel = "model-from-base"
            }
            let releaseJson = {
                Settings.defaultSettings () with
                    TargetDirectory = "C:\\ReleaseDir"
                    SelectedModel = "model-from-release"
            }
            Settings.save baseJsonPath baseJson |> ignore
            Settings.save releaseJsonPath releaseJson |> ignore

            let loaded = Settings.loadConfigurationWithConfig (Some "Release") None (Some dummyEnvPath)
            loaded.TargetDirectory |> should equal "C:\\ReleaseDir"
            loaded.SelectedModel |> should equal "model-from-release"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``loadConfigurationWithConfig は 構成別ファイル不在時に appsettings.json へ安全にフォールバックする`` () =
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
            }
            Settings.save baseJsonPath baseJson |> ignore

            // Debug構成を指定しても appsettings.Debug.json が無ければ appsettings.json をロード
            let loadedDebug = Settings.loadConfigurationWithConfig (Some "Debug") None (Some dummyEnvPath)
            loadedDebug.TargetDirectory |> should equal "C:\\BaseDirOnly"
            loadedDebug.SelectedModel |> should equal "model-from-base-only"

            // Release構成を指定しても appsettings.Release.json が無ければ appsettings.json をロード
            let loadedRelease = Settings.loadConfigurationWithConfig (Some "Release") None (Some dummyEnvPath)
            loadedRelease.TargetDirectory |> should equal "C:\\BaseDirOnly"
            loadedRelease.SelectedModel |> should equal "model-from-base-only"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)
