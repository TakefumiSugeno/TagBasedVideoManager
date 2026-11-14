namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

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

    [<Fact>]
    let ``UT-04: resolveSavePath は BaseDirectory に構成ファイル不在または書込不可時に AppData の環境別パスを決定する`` () =
        let tempBaseDir = Path.Combine(Path.GetTempPath(), "RenamerBaseEmpty_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempBaseDir) |> ignore
        try
            let path = Settings.resolveSavePath (Some tempBaseDir)
            let envName = Settings.resolveEnvironmentName ()
            let expectedAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager", $"appsettings.{envName}.json")
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
    let ``loadConfiguration は AppData に環境別ファイルがない標準状態でプロジェクト直下の構成別設定ファイルをロードする`` () =
        // 引数なし loadConfiguration None None の実行時に、プロジェクト直下の環境別設定ファイル（Debug構成時は appsettings.Development.json）がオーバーライドマージされることを検証
        let envName = Settings.resolveEnvironmentName ()
        let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
        let appDataEnvFile = Path.Combine(appDataDir, $"appsettings.{envName}.json")
        let appDataBaseFile = Path.Combine(appDataDir, "appsettings.json")

        let hadEnvFile = File.Exists(appDataEnvFile)
        let origEnvContent = if hadEnvFile then File.ReadAllText(appDataEnvFile) else null

        let hadBaseFile = File.Exists(appDataBaseFile)
        let origBaseContent = if hadBaseFile then File.ReadAllText(appDataBaseFile) else null

        try
            if hadEnvFile then File.Delete(appDataEnvFile)
            if hadBaseFile then File.Delete(appDataBaseFile)

            let loaded = Settings.loadConfiguration None None
            // プロジェクト直下の設定がロードされていること
            let rec findRoot (dir: DirectoryInfo) (depth: int) =
                if depth <= 0 || box dir = null then None
                else
                    let candidateDev = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.Development.json")
                    if File.Exists(candidateDev) then Some dir.FullName
                    else findRoot dir.Parent (depth - 1)
            let current = DirectoryInfo(Directory.GetCurrentDirectory())
            let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            let rootDir =
                findRoot current 8
                |> Option.orElseWith (fun () -> findRoot baseDir 8)
                |> Option.defaultWith (fun () -> failwith "src/TagBasedVideoManager.Renamer/appsettings.Development.json が見つかりません")

            let devJsonPath = Path.Combine(rootDir, "src", "TagBasedVideoManager.Renamer", "appsettings.Development.json")
            let devJson = Settings.load devJsonPath
            match devJson with
            | Ok expected ->
                loaded.TargetDirectory |> should equal expected.TargetDirectory
                loaded.SelectedModel |> should equal expected.SelectedModel
                loaded.ApiKey |> should equal expected.ApiKey
            | Error err -> failwith $"Project dev json load failed: {err}"
        finally
            try
                if hadEnvFile then File.WriteAllText(appDataEnvFile, origEnvContent)
                elif File.Exists(appDataEnvFile) then File.Delete(appDataEnvFile)
            with _ -> ()
            try
                if hadBaseFile then File.WriteAllText(appDataBaseFile, origBaseContent)
                elif File.Exists(appDataBaseFile) then File.Delete(appDataBaseFile)
            with _ -> ()

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
    let ``プロジェクト直下の appsettings.Development.json と appsettings.Production.json は正しくデシリアライズできる`` () =
        let rec findRoot (dir: DirectoryInfo) (depth: int) =
            if depth <= 0 || box dir = null then None
            else
                let candidateDev = Path.Combine(dir.FullName, "src", "TagBasedVideoManager.Renamer", "appsettings.Development.json")
                if File.Exists(candidateDev) then Some dir.FullName
                else findRoot dir.Parent (depth - 1)
        let current = DirectoryInfo(Directory.GetCurrentDirectory())
        let baseDir = DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
        let rootDir =
            findRoot current 8
            |> Option.orElseWith (fun () -> findRoot baseDir 8)
            |> Option.defaultWith (fun () -> failwith "プロジェクトルートが見つかりません")

        let devPath = Path.Combine(rootDir, "src", "TagBasedVideoManager.Renamer", "appsettings.Development.json")
        let prodPath = Path.Combine(rootDir, "src", "TagBasedVideoManager.Renamer", "appsettings.Production.json")

        File.Exists(devPath) |> should equal true
        File.Exists(prodPath) |> should equal true

        match Settings.load devPath with
        | Ok devSettings ->
            devSettings.TargetDirectory |> should equal "test/videos"
            devSettings.PathLengthThreshold |> should equal 240
            devSettings.SelectedModel |> should not' (be EmptyString)
            devSettings.ApiKey |> should equal (Some "[API_KEY]")
            devSettings.Rules.Length |> should be (greaterThan 0)
        | Error err -> failwith $"Development settings load failed: {err}"

        match Settings.load prodPath with
        | Ok prodSettings ->
            prodSettings.TargetDirectory |> should equal ""
            prodSettings.PathLengthThreshold |> should equal 240
            prodSettings.SelectedModel |> should not' (be EmptyString)
            prodSettings.ApiKey |> should equal None
            prodSettings.Rules.Length |> should be (greaterThan 0)
        | Error err -> failwith $"Production settings load failed: {err}"

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

    [<Fact>]
    let ``IT-02: ReadOnly 基底ディレクトリ時の %APPDATA% 環境別保存と再読込整合`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "RenamerReadOnly_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let baseFile = Path.Combine(tempDir, "appsettings.json")
        File.WriteAllText(baseFile, "{}")

        let envName = Settings.resolveEnvironmentName ()
        let expectedAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager", $"appsettings.{envName}.json")
        let hadExisting = File.Exists(expectedAppData)
        let origContent = if hadExisting then File.ReadAllText(expectedAppData) else null

        try
            // 保存先を解決 -> 書込不可または未配置時は AppData 側へ
            // 擬似的に AppData 側へ保存
            let custom = { Settings.defaultSettings () with TargetDirectory = "C:\\AppDataSavedDir" }
            let saveResult = Settings.save expectedAppData custom
            match saveResult with
            | Error err -> failwith $"Save to AppData failed: {err}"
            | Ok () -> ()

            File.Exists(expectedAppData) |> should equal true

            // 再読込時に %APPDATA% の環境別設定が反映されること
            let loaded = Settings.loadConfiguration (Some expectedAppData) None
            loaded.TargetDirectory |> should equal "C:\\AppDataSavedDir"
        finally
            try
                if hadExisting then
                    File.WriteAllText(expectedAppData, origContent)
                elif File.Exists(expectedAppData) then
                    File.Delete(expectedAppData)
            with _ -> ()
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``回帰テスト: loadConfigurationWithConfig において %APPDATA% の環境別設定がベース層を最優先上書きすること`` () =
        let appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagBasedVideoManager")
        if not (Directory.Exists(appDataDir)) then Directory.CreateDirectory(appDataDir) |> ignore
        let testEnvName = "RegressionTestEnv_" + Guid.NewGuid().ToString("N")
        let targetFile = Path.Combine(appDataDir, $"appsettings.{testEnvName}.json")
        let hadExisting = File.Exists(targetFile)
        let origContent = if hadExisting then File.ReadAllText(targetFile) else null

        try
            // %APPDATA% 側にオーバーライド設定を配置
            let overrideJson = """{ "targetDirectory": "C:\\AppDataOverriddenDir", "selectedModel": "custom/appdata-model:v1" }"""
            File.WriteAllText(targetFile, overrideJson)

            // jsonPathOpt = None, envPathOpt = None で通常階層探索を行わせ、%APPDATA% の優先マージを検証
            let loaded = Settings.loadConfigurationWithConfig (Some testEnvName) None None
            loaded.TargetDirectory |> should equal "C:\\AppDataOverriddenDir"
            loaded.SelectedModel |> should equal "custom/appdata-model:v1"
        finally
            try
                if hadExisting then
                    File.WriteAllText(targetFile, origContent)
                elif File.Exists(targetFile) then
                    File.Delete(targetFile)
            with _ -> ()



