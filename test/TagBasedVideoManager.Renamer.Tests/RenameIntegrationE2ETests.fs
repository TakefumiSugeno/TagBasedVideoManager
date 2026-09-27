namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open Avalonia
open TagBasedVideoManager.Renamer

module RenameIntegrationE2ETests =

    /// <summary>
    /// 一時テスト用ディレクトリをセットアップし、テスト終了後に確実に削除するユーティリティ
    /// </summary>
    let withTempDirectory (f: string -> unit) =
        let tempDir = Path.Combine(Path.GetTempPath(), "tbvm_e2e_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        try
            f tempDir
        finally
            try
                if Directory.Exists(tempDir) then
                    Directory.Delete(tempDir, true)
            with _ -> ()

    [<Fact>]
    let ``E2E: scan -> propose with AI comments -> physical rename -> verify -> undo restore`` () =
        withTempDirectory (fun tempDir ->
            // -----------------------------------------------------------------
            // 1. テストファイルの作成
            // -----------------------------------------------------------------
            // (A) 240文字以上の長パスファイル (正常系: 日時付き)
            let targetNameLen1 = max 20 (245 - tempDir.Length - 1)
            let longName1 = "2024-01-01_" + String('a', targetNameLen1 - 15) + ".mp4"
            let longPath1 = Path.Combine(tempDir, longName1)
            File.WriteAllText(longPath1, "video-content-1")

            // (B) 240文字以上の長パスファイル (変則系: 日時なし・AIコメント対象)
            let targetNameLen2 = max 20 (245 - tempDir.Length - 1)
            let longName2 = "strange_no_date_" + String('b', targetNameLen2 - 20) + ".mp4"
            let longPath2 = Path.Combine(tempDir, longName2)
            File.WriteAllText(longPath2, "video-content-2")

            // (C) 閾値未満の通常ファイル (走査除外対象)
            let shortName = "short_sample.mp4"
            let shortPath = Path.Combine(tempDir, shortName)
            File.WriteAllText(shortPath, "short-content")

            // パス長の前提条件検証
            (longPath1.Length >= 240) |> should equal true
            (longPath2.Length >= 240) |> should equal true
            (shortPath.Length < 240) |> should equal true

            // -----------------------------------------------------------------
            // 2. 走査 (FileScanner.scanLongPaths)
            // -----------------------------------------------------------------
            let scanResult = FileScanner.scanLongPaths tempDir 240
            match scanResult with
            | Error err -> failwith $"走査エラー: {err}"
            | Ok scannedFiles ->
                scannedFiles.Length |> should equal 2
                scannedFiles |> List.map (fun f -> f.FileName) |> should contain longName1
                scannedFiles |> List.map (fun f -> f.FileName) |> should contain longName2

                // -----------------------------------------------------------------
                // 3. AI提案のモック生成（問題時限定AIコメントの含有をシミュレート）
                // -----------------------------------------------------------------
                let proposedName1 = "2024-01-01_nature_doc_short.mp4"
                let proposedName2 = "2026-09-26_strange_video_short.mp4"
                let proposedPath1 = Path.Combine(tempDir, proposedName1)
                let proposedPath2 = Path.Combine(tempDir, proposedName2)
                let aiComment2 = "元ファイル名に撮影日時が含まれていなかったため、本日の日付で補完しました。"

                let proposal1: RenameProposal = {
                    OriginalFullPath = longPath1
                    OriginalFileName = longName1
                    DirectoryPath = tempDir
                    OriginalLength = longPath1.Length
                    ProposedFileName = proposedName1
                    ProposedLength = proposedPath1.Length
                    AiComment = None // 正常時はコメントなし
                    IsSelected = true
                    LastWriteTime = DateTime.UtcNow
                }

                let proposal2: RenameProposal = {
                    OriginalFullPath = longPath2
                    OriginalFileName = longName2
                    DirectoryPath = tempDir
                    OriginalLength = longPath2.Length
                    ProposedFileName = proposedName2
                    ProposedLength = proposedPath2.Length
                    AiComment = Some aiComment2 // 問題時のみコメントあり
                    IsSelected = true
                    LastWriteTime = DateTime.UtcNow
                }

                // AIコメントの条件付き格納検証
                proposal1.AiComment |> should equal None
                proposal2.AiComment |> should equal (Some aiComment2)

                // -----------------------------------------------------------------
                // 4. 物理リネーム実行 (FileRenamer.executeRename)
                // -----------------------------------------------------------------
                let renameResult = FileRenamer.executeRename [ proposal1; proposal2 ]
                match renameResult with
                | Error err -> failwith $"リネームに失敗しました: {err}"
                | Ok undoEntries ->
                    undoEntries.Length |> should equal 2

                    // 物理ファイルの状態検証
                    File.Exists(longPath1) |> should equal false
                    File.Exists(longPath2) |> should equal false
                    File.Exists(proposedPath1) |> should equal true
                    File.Exists(proposedPath2) |> should equal true

                    // ファイル内容が維持されていることの検証
                    File.ReadAllText(proposedPath1) |> should equal "video-content-1"
                    File.ReadAllText(proposedPath2) |> should equal "video-content-2"

                    // -----------------------------------------------------------------
                    // 5. リネーム後の再走査で対象件数が 0 件になることを検証
                    // -----------------------------------------------------------------
                    let reScanResult = FileScanner.scanLongPaths tempDir 240
                    match reScanResult with
                    | Error err -> failwith $"再走査エラー: {err}"
                    | Ok reScannedFiles ->
                        reScannedFiles.Length |> should equal 0

                    // -----------------------------------------------------------------
                    // 6. Undo逆リネーム実行 (FileRenamer.executeUndo) による完全復元
                    // -----------------------------------------------------------------
                    let undoResult = FileRenamer.executeUndo undoEntries
                    match undoResult with
                    | Error err -> failwith $"Undoに失敗しました: {err}"
                    | Ok () ->
                        // 短縮ファイルが存在しなくなり、元の長パスファイルが完全に復元されていること
                        File.Exists(proposedPath1) |> should equal false
                        File.Exists(proposedPath2) |> should equal false
                        File.Exists(longPath1) |> should equal true
                        File.Exists(longPath2) |> should equal true

                        // 復元されたファイルの内容検証
                        File.ReadAllText(longPath1) |> should equal "video-content-1"
                        File.ReadAllText(longPath2) |> should equal "video-content-2"

                        // 再度走査すると、元通り2件の長パスファイルが抽出されること
                        let restoredScanResult = FileScanner.scanLongPaths tempDir 240
                        match restoredScanResult with
                        | Error err -> failwith $"復元後走査エラー: {err}"
                        | Ok restoredScanned ->
                            restoredScanned.Length |> should equal 2
        )

    [<Fact>]
    let ``E2E: collision auto-uniquification and undo safety check`` () =
        withTempDirectory (fun tempDir ->
            // 同一フォルダ内に、AI提案が同名になってしまう2つの長パスファイルを作成
            let p = String('x', 200)
            let fileA = Path.Combine(tempDir, "video_A_" + p + ".mp4")
            let fileB = Path.Combine(tempDir, "video_B_" + p + ".mp4")
            File.WriteAllText(fileA, "content-A")
            File.WriteAllText(fileB, "content-B")

            // 両者とも "video_short.mp4" を提案
            let targetName = "video_short.mp4"
            let targetPath = Path.Combine(tempDir, targetName)

            let propA: RenameProposal = {
                OriginalFullPath = fileA
                OriginalFileName = Path.GetFileName(fileA)
                DirectoryPath = tempDir
                OriginalLength = fileA.Length
                ProposedFileName = targetName
                ProposedLength = targetPath.Length
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
            }

            let propB: RenameProposal = {
                OriginalFullPath = fileB
                OriginalFileName = Path.GetFileName(fileB)
                DirectoryPath = tempDir
                OriginalLength = fileB.Length
                ProposedFileName = targetName
                ProposedLength = targetPath.Length
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
            }

            // リネーム実行: 自動で 2件目が _1 に一意化される
            let renameResult = FileRenamer.executeRename [ propA; propB ]
            match renameResult with
            | Error err -> failwith $"衝突リネームに失敗しました: {err}"
            | Ok undoEntries ->
                undoEntries.Length |> should equal 2

                let expectedUniqueB = Path.Combine(tempDir, "video_short_1.mp4")
                File.Exists(targetPath) |> should equal true
                File.Exists(expectedUniqueB) |> should equal true
                File.ReadAllText(targetPath) |> should equal "content-A"
                File.ReadAllText(expectedUniqueB) |> should equal "content-B"

                // Undo復元
                let undoResult = FileRenamer.executeUndo undoEntries
                match undoResult with
                | Error err -> failwith $"Undoに失敗しました: {err}"
                | Ok () ->
                    File.Exists(fileA) |> should equal true
                    File.Exists(fileB) |> should equal true
                    File.Exists(targetPath) |> should equal false
                    File.Exists(expectedUniqueB) |> should equal false
        )

    // Avalonia App の初期化 (テスト実行中1回だけ)
    type UiTestApp() =
        inherit Avalonia.Application()
        override this.Initialize() =
            this.Styles.Add(Avalonia.Themes.Fluent.FluentTheme())
            this.RequestedThemeVariant <- Avalonia.Styling.ThemeVariant.Dark

    let mutable private isAppInitialized = false
    let private appInitLock = obj()
    let ensureAppInitialized () =
        lock appInitLock (fun () ->
            if not isAppInitialized then
                Avalonia.AppBuilder.Configure<UiTestApp>()
                    .UsePlatformDetect()
                    .SetupWithoutStarting() |> ignore
                isAppInitialized <- true
        )

    [<Fact>]
    let ``E2E UI: render and capture application states as visual evidence (initial, proposed vertical, proposed horizontal, undo dialog)`` () =
        ensureAppInitialized ()

        let settings = Settings.defaultSettings ()
        let sampleCandidates : RenameProposal list = [
            {
                OriginalFullPath = "D:\\Videos\\2024-01-01_extremely_long_video_name_about_nature_and_wildlife_in_the_deep_forest_recorded_with_high_definition_camera_special_edition_documentary_part_1_full_hd_1080p_60fps_surround_sound_extended_version_remastered_2024_spring_collection_sample_video.mp4"
                OriginalFileName = "2024-01-01_extremely_long_video_name_about_nature_and_wildlife_in_the_deep_forest_recorded_with_high_definition_camera_special_edition_documentary_part_1_full_hd_1080p_60fps_surround_sound_extended_version_remastered_2024_spring_collection_sample_video.mp4"
                DirectoryPath = "D:\\Videos"
                OriginalLength = 265
                ProposedFileName = "2024-01-01_nature_doc_part1.mp4"
                ProposedLength = 39
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
            }
            {
                OriginalFullPath = "D:\\Videos\\strange_title_without_date_recorded_by_random_camera_device_and_extremely_long_filename_that_exceeds_threshold_character_limit_for_docker_mount_failure_demonstration_file_sample_data.mp4"
                OriginalFileName = "strange_title_without_date_recorded_by_random_camera_device_and_extremely_long_filename_that_exceeds_threshold_character_limit_for_docker_mount_failure_demonstration_file_sample_data.mp4"
                DirectoryPath = "D:\\Videos"
                OriginalLength = 252
                ProposedFileName = "2026-09-26_strange_title_fixed.mp4"
                ProposedLength = 41
                AiComment = Some "元ファイル名に撮影日時が含まれていなかったため、本日の日付で補完しました。"
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
            }
        ]

        let baseModel : Model = {
            Settings = { settings with TargetDirectory = "D:\\Videos" }
            CurrentThreshold = 240
            SelectedRuleId = "rule-date-action"
            IsScanning = false
            IsRequestingAi = false
            IsRenaming = false
            IsDockerBusy = false
            ErrorMessage = None
            Candidates = []
            UndoStack = []
            Docker = {
                State = Running
                IsPortAccessible = true
                ContainerId = Some "tag-based-video-manager-1"
                LastChecked = DateTime.UtcNow
            }
            Layout = Vertical
            SortCriterion = PathLengthDesc
            IsRuleManagerOpen = false
            EditingRule = None
            ConfirmDialog = None
        }

        let renderAndCapture (m: Model) (fileName: string) =
            let w = new Avalonia.FuncUI.Hosts.HostWindow()
            w.Width <- 1100.0
            w.Height <- 720.0
            w.Background <- Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a1a1a"))

            Elmish.Program.mkProgram (fun () -> m, Elmish.Cmd.none) State.update Views.view
            |> Avalonia.FuncUI.Elmish.Program.withHost w
            |> Avalonia.FuncUI.Elmish.Program.runWithAvaloniaSyncDispatch ()

            w.Show()
            w.UpdateLayout()

            let pixelSize = Avalonia.PixelSize(1100, 720)
            let dpi = Avalonia.Vector(96.0, 96.0)
            let rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixelSize, dpi)
            rtb.Render(w)

            let rec findRepoRoot (dir: DirectoryInfo) =
                if dir = null then AppContext.BaseDirectory
                elif File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) then dir.FullName
                elif dir.Parent = null then AppContext.BaseDirectory
                else findRepoRoot dir.Parent
            let repoRoot = findRepoRoot (DirectoryInfo(AppContext.BaseDirectory))
            let outputDir = Path.Combine(repoRoot, "test", "TestResults")
            if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore
            let outputPath = Path.Combine(outputDir, fileName)
            rtb.Save(outputPath)
            w.Close()

            File.Exists(outputPath) |> should equal true
            FileInfo(outputPath).Length |> should be (greaterThan 1000L)

        // 1. 初期画面 (0件)
        renderAndCapture baseModel "E2E_01_Initial_State.png"

        // 2. 走査＆短縮提案完了 (上下並び・AIコメント付き)
        let proposedModel = {
            baseModel with
                Candidates = sampleCandidates
                ErrorMessage = Some "💡 2件の長パス危険ファイル（240文字超）を検出し、短縮リネーム候補を生成しました。"
        }
        renderAndCapture proposedModel "E2E_02_Proposed_Vertical_With_AI_Comment.png"

        // 3. 左右並びモード
        let horizontalModel = { proposedModel with Layout = Horizontal }
        renderAndCapture horizontalModel "E2E_03_Proposed_Horizontal_Layout.png"

        // 4. Undo確認ダイアログ
        let undoDialogModel = {
            proposedModel with
                UndoStack = [ [ {
                    Id = Guid.NewGuid()
                    Timestamp = DateTime.UtcNow
                    OriginalFullPath = sampleCandidates.[0].OriginalFullPath
                    RenamedFullPath = Path.Combine(sampleCandidates.[0].DirectoryPath, sampleCandidates.[0].ProposedFileName)
                } ] ]
                ConfirmDialog = Some {
                    Title = "直前のリネームを元に戻しますか？"
                    Message = "直前にリネームされたファイルを以前のファイル名に復元します。復旧を反映するため、Dockerコンテナが自動的に再起動されます。"
                    ConfirmText = "復元してコンテナ再起動"
                    CancelText = "キャンセル"
                    OnConfirm = DismissConfirm
                }
        }
        renderAndCapture undoDialogModel "E2E_04_Undo_Confirm_Dialog.png"

        // 5. 命名規則マネージャーモーダル表示状態
        let ruleManagerModel = { proposedModel with IsRuleManagerOpen = true }
        renderAndCapture ruleManagerModel "E2E_05_Rule_Manager_Modal.png"

        // 6. ウィンドウ幅を超える長大ファイル名（300文字超）のカード外枠非見切れ検証
        let longPathModel = {
            proposedModel with
                Candidates = [
                    { sampleCandidates.[0] with
                        OriginalFileName = "2024-01-01_" + String('x', 300) + "_extremely_long_video_name.mp4"
                        OriginalLength = 340
                    }
                ]
        }
        renderAndCapture longPathModel "E2E_06_Long_Path_No_Overflow.png"


    [<Fact>]
    let ``E2E: Zero-configuration user journey (no API key, scan long paths, fallback proposal, manual edit, physical rename and undo)`` () =
        withTempDirectory (fun tempDir ->
            // 1. テスト用の長パスファイル作成
            let targetNameLen1 = max 20 (245 - tempDir.Length - 1)
            let longName1 = "2024-05-01_" + String('a', targetNameLen1 - 15) + ".mp4"
            let longPath1 = Path.Combine(tempDir, longName1)
            File.WriteAllText(longPath1, "initial-video-data-1")

            let targetNameLen2 = max 20 (245 - tempDir.Length - 1)
            let longName2 = "random_no_date_video_" + String('b', targetNameLen2 - 25) + ".mp4"
            let longPath2 = Path.Combine(tempDir, longName2)
            File.WriteAllText(longPath2, "initial-video-data-2")

            // 2. 初期Modelセットアップ (APIキー未設定 = None)
            let initialSettings = { Settings.defaultSettings () with TargetDirectory = tempDir; ApiKey = None }
            let baseModel, _ = State.init ()
            let model1 = { baseModel with Settings = initialSettings }

            // 3. 走査実行 (FileScanner.scanLongPaths)
            let scanResult = FileScanner.scanLongPaths tempDir 240
            match scanResult with
            | Error err -> failwith $"走査失敗: {err}"
            | Ok candidates ->
                candidates.Length |> should equal 2

                // 4. State.update で ScanCompleted を処理
                // (APIキー未設定のため、ローカル短縮フォールバックが起動)
                let model2, cmd = State.update (ScanCompleted (Ok candidates)) model1
                
                // ローカル短縮候補が Candidates に格納されていることを検証
                model2.Candidates.Length |> should equal 2
                
                let prop1 = model2.Candidates |> List.find (fun c -> c.OriginalFullPath = longPath1)
                let prop2 = model2.Candidates |> List.find (fun c -> c.OriginalFullPath = longPath2)

                // 正常系 (日付あり): AIコメントなし、30文字程度に短縮
                prop1.ProposedFileName |> should startWith "2024-05-01"
                prop1.AiComment |> should equal None
                (prop1.ProposedLength < 240) |> should equal true

                // 変則系 (日付なし): AIコメントあり、本日の日付で補完
                prop2.AiComment |> should not' (equal None)
                (prop2.ProposedLength < 240) |> should equal true

                // 5. ユーザーによる手動ファイル名編集のシミュレート (UpdateProposedName)
                let customName1 = "2024-05-01_my_edited_title.mp4"
                let model3, _ = State.update (UpdateProposedName (longPath1, customName1)) model2
                let updatedProp1 = model3.Candidates |> List.find (fun c -> c.OriginalFullPath = longPath1)
                updatedProp1.ProposedFileName |> should equal customName1

                // 6. リネーム実行 (State.update ExecuteRenameOnly)
                let model4, _ = State.update ExecuteRenameOnly model3
                // 物理リネームの実行 (FileRenamer.executeRename)
                let renameRes = FileRenamer.executeRename model3.Candidates
                match renameRes with
                | Error err -> failwith $"リネーム失敗: {err}"
                | Ok undoRecords ->
                    let model5, _ = State.update (RenameCompleted (false, Ok undoRecords)) model4

                    // 物理ファイルの存在検証
                    let renamedPath1 = Path.Combine(tempDir, customName1)
                    let renamedPath2 = Path.Combine(tempDir, prop2.ProposedFileName)
                    File.Exists(longPath1) |> should equal false
                    File.Exists(longPath2) |> should equal false
                    File.Exists(renamedPath1) |> should equal true
                    File.Exists(renamedPath2) |> should equal true

                    // 7. Undoスタックに履歴が積まれ、Undo可能状態になっていることを検証
                    model5.UndoStack.IsEmpty |> should equal false

                    // 8. Undo確認ダイアログの表示と実行 (RequestUndo -> ConfirmDialog -> ExecuteUndo)
                    let model6, _ = State.update RequestUndo model5
                    model6.ConfirmDialog |> should not' (equal None)

                    let undoRes = FileRenamer.executeUndo undoRecords
                    match undoRes with
                    | Error err -> failwith $"Undo失敗: {err}"
                    | Ok () ->
                        let model7, _ = State.update (UndoCompleted (Ok ())) model6

                        // 元の長パスファイルが完全に復元されていること
                        File.Exists(longPath1) |> should equal true
                        File.Exists(longPath2) |> should equal true
                        File.Exists(renamedPath1) |> should equal false
                        File.Exists(renamedPath2) |> should equal false
                        File.ReadAllText(longPath1) |> should equal "initial-video-data-1"
                        File.ReadAllText(longPath2) |> should equal "initial-video-data-2"
        )

