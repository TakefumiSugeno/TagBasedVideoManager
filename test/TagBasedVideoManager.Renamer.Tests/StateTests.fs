namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

[<Collection("IsolatedSettingsAndState")>]
module StateTests =

    /// <summary>共有設定隔離ハーネスへの委譲ラッパー</summary>
    let private withIsolatedStateTest = SettingsIsolationHarness.withIsolatedSettingsEnvironment

    /// <summary>共有設定隔離ハーネスの一時モデル初期化ヘルパー</summary>
    let private initIsolatedModel = SettingsIsolationHarness.initIsolatedModel

    [<Fact>]
    let ``init は初期設定（閾値240文字・Verticalレイアウト・空候補）を生成する`` () =
        SettingsIsolationHarness.withCleanInitEnvironment (fun () ->
            let model, _cmd = State.init ()
            model.CurrentThreshold |> should equal 240
            model.Layout |> should equal Vertical
            model.Candidates |> should be Empty
            model.UndoStack |> should be Empty
            model.IsScanning |> should equal false
            model.IsRequestingAi |> should equal false
            model.IsRenaming |> should equal false
            model.ConfirmDialog |> should equal None
        )

    [<Fact>]
    let ``ThresholdChanged はセッション内閾値を更新し、元の設定構造体は変更しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let newModel, _ = State.update (ThresholdChanged 200) initialModel

            newModel.CurrentThreshold |> should equal 200
            newModel.Settings.PathLengthThreshold |> should equal 240 // 設定本体は不変
        )

    [<Fact>]
    let ``UpdateProposedName はインライン編集時に ProposedLength を即座に再計算する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let sampleProposal: RenameProposal = {
                OriginalFullPath = "C:\\Videos\\VeryLongPath.mp4"
                OriginalFileName = "VeryLongPath.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 26
                OriginalFileNameBytes = 16
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "Short.mp4"
                ProposedLength = 19
                ProposedFileNameBytes = 9
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = System.DateTime.UtcNow
                AiError = None
            }
            let modelWithCandidate = { initialModel with Candidates = [ sampleProposal ] }

            let newProposedName = "EvenShorter.mp4"
            let updatedModel, _ = State.update (UpdateProposedName (sampleProposal.OriginalFullPath, newProposedName)) modelWithCandidate

            updatedModel.Candidates.Length |> should equal 1
            let updated = updatedModel.Candidates.Head
            updated.ProposedFileName |> should equal "EvenShorter.mp4"
            updated.ProposedLength |> should equal ("C:\\Videos\\" + newProposedName).Length
        )

    [<Fact>]
    let ``ToggleCandidateSelect および SelectAllCandidates で選択状態を切り替えられる`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 15
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "n1.mp4"
                ProposedLength = 15
                ProposedFileNameBytes = 6
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = System.DateTime.UtcNow
                AiError = None
            }
            let p2 = { p1 with OriginalFullPath = "C:\\Videos\\v2.mp4"; OriginalFileName = "v2.mp4" }
            let modelWithCandidates = { initialModel with Candidates = [ p1; p2 ] }

            // 1. 全解除
            let unselectedAll, _ = State.update (SelectAllCandidates false) modelWithCandidates
            unselectedAll.Candidates |> List.forall (fun c -> not c.IsSelected) |> should equal true

            // 2. 1件トグル
            let toggled, _ = State.update (ToggleCandidateSelect p1.OriginalFullPath) unselectedAll
            let first = toggled.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
            first.IsSelected |> should equal true
        )

    [<Fact>]
    let ``SetCandidateSelect は指定パスの選択状態を明示的に更新し、冪等に動作する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 15
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "n1.mp4"
                ProposedLength = 15
                ProposedFileNameBytes = 6
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = false
                LastWriteTime = System.DateTime.UtcNow
                AiError = None
            }
            let p2 = { p1 with OriginalFullPath = "C:\\Videos\\v2.mp4"; OriginalFileName = "v2.mp4"; IsSelected = true }
            let model = { initialModel with Candidates = [ p1; p2 ] }

            // true に明示設定
            let setTrue, _ = State.update (SetCandidateSelect (p1.OriginalFullPath, true)) model
            let p1Updated = setTrue.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
            p1Updated.IsSelected |> should equal true

            // 冪等性: 再度 true を設定しても true のまま
            let setTrueAgain, _ = State.update (SetCandidateSelect (p1.OriginalFullPath, true)) setTrue
            let p1Again = setTrueAgain.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
            p1Again.IsSelected |> should equal true

            // false に明示設定
            let setFalse, _ = State.update (SetCandidateSelect (p1.OriginalFullPath, false)) setTrueAgain
            let p1False = setFalse.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
            p1False.IsSelected |> should equal false
            // 他の要素(p2)は影響を受けない
            let p2Unchanged = setFalse.Candidates |> List.find (fun c -> c.OriginalFullPath = p2.OriginalFullPath)
            p2Unchanged.IsSelected |> should equal true
        )

    [<Fact>]
    let ``RequestUndo は UndoStack に履歴がある場合、コンテナ再起動警告を含むダイアログを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let dummyUndoRecord: UndoRecord = {
                Id = System.Guid.NewGuid()
                Timestamp = System.DateTime.UtcNow
                OriginalFullPath = "C:\\Videos\\orig.mp4"
                RenamedFullPath = "C:\\Videos\\renamed.mp4"
            }
            let modelWithHistory = { initialModel with UndoStack = [ [ dummyUndoRecord ] ] }

            let updatedModel, _ = State.update RequestUndo modelWithHistory
            match updatedModel.ConfirmDialog with
            | None -> failwith "Expected ConfirmDialog to be Some"
            | Some dialog ->
                dialog.Title |> should contain "Undo"
                dialog.Message |> should contain "コンテナが再起動します"
                dialog.ConfirmText |> should contain "元に戻して再起動"
                dialog.OnConfirm |> should equal ExecuteUndo
        )

    [<Fact>]
    let ``SetLayoutMode は Vertical と Horizontal を正しく切り替える`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let horizontalModel, _ = State.update (SetLayoutMode Horizontal) initialModel
            horizontalModel.Layout |> should equal Horizontal

            let verticalModel, _ = State.update (SetLayoutMode Vertical) horizontalModel
            verticalModel.Layout |> should equal Vertical
        )

    [<Fact>]
    let ``ChangeSortCriterion は SortCriterion を更新し候補を即座に並び替える`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let pShort = {
                OriginalFullPath = "C:\\Videos\\Short.mp4"
                OriginalFileName = "Short.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 20
                OriginalFileNameBytes = 9
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "Short.mp4"
                ProposedLength = 20
                ProposedFileNameBytes = 9
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = System.DateTime.UtcNow
                AiError = None
            }
            let pLong = {
                OriginalFullPath = "C:\\Videos\\VeryLongLongName.mp4"
                OriginalFileName = "VeryLongLongName.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 35
                OriginalFileNameBytes = 20
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "VeryLongLongName.mp4"
                ProposedLength = 35
                ProposedFileNameBytes = 20
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = System.DateTime.UtcNow
                AiError = None
            }
            let modelWithCandidates = { initialModel with Candidates = [ pShort; pLong ] }

            // PathLengthDesc (降順) に切り替え -> Long(35) が先頭
            let descModel, _ = State.update (ChangeSortCriterion PathLengthDesc) modelWithCandidates
            descModel.SortCriterion |> should equal PathLengthDesc
            descModel.Candidates.Head.OriginalFileName |> should equal "VeryLongLongName.mp4"

            // PathLengthAsc (昇順) に切り替え -> Short(20) が先頭
            let ascModel, _ = State.update (ChangeSortCriterion PathLengthAsc) descModel
            ascModel.SortCriterion |> should equal PathLengthAsc
            ascModel.Candidates.Head.OriginalFileName |> should equal "Short.mp4"
        )

    [<Fact>]
    let ``TargetDirectoryChanged は走査対象ディレクトリパスを即座に更新する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let newDir = "D:\\Selected\\Videos\\Folder"
            let updatedModel, _ = State.update (TargetDirectoryChanged newDir) initialModel
            updatedModel.Settings.TargetDirectory |> should equal newDir
        )

    [<Fact>]
    let ``OpenRuleManager と CloseRuleManager はモーダル開閉状態 IsRuleManagerOpen を正しく遷移させる`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            initialModel.IsRuleManagerOpen |> should equal false

            // 管理ボタン押下 -> オープン
            let openModel, _ = State.update OpenRuleManager initialModel
            openModel.IsRuleManagerOpen |> should equal true

            // 閉じる操作 -> クローズ
            let closedModel, _ = State.update CloseRuleManager openModel
            closedModel.IsRuleManagerOpen |> should equal false
        )

    [<Fact>]
    let ``MoveRuleOrder は命名規則の優先度順序を正しく入れ替える`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let rule1 = { Id = "r1"; Name = "Rule 1"; Pattern = "{P1}"; PromptInstruction = "I1"; Order = 0; EnableWebSearch = false }
            let rule2 = { Id = "r2"; Name = "Rule 2"; Pattern = "{P2}"; PromptInstruction = "I2"; Order = 1; EnableWebSearch = true }
            let modelWithRules = { initialModel with Settings = { initialModel.Settings with Rules = [ rule1; rule2 ] } }

            // rule2 を上へ移動 (direction = -1)
            let movedModel, _ = State.update (MoveRuleOrder ("r2", -1)) modelWithRules
            movedModel.ErrorMessage |> should equal None
            movedModel.Settings.Rules.Head.Id |> should equal "r2"
            movedModel.Settings.Rules.[1].Id |> should equal "r1"
        )

    [<Fact>]
    let ``ScanCompleted は APIキー未設定時にAIのフリをせず、未提案状態として保持し警告案内を表示する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let modelWithoutKey = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = None }
            }

            let dummyCandidate = {
                FullPath = "E:\\test\\very_long_file_name_over_limit_testing_sample.mp4"
                FileName = "very_long_file_name_over_limit_testing_sample.mp4"
                DirectoryPath = "E:\\test"
                PathLength = 265
                FileNameBytes = 47
                Violation = WindowsPathOnly
                FileSizeBytes = 1024L
                LastWriteTime = DateTime(2025, 8, 12)
            }

            let updatedModel, cmd = State.update (ScanCompleted (Ok [ dummyCandidate ])) modelWithoutKey
            updatedModel.Candidates.Length |> should equal 1
            let proposal = updatedModel.Candidates.Head

            // 重要: LLM未接続なので「AI提案済」ではなく未提案状態
            proposal.IsAiProposed |> should equal false
            // 初期状態は待機中コメント
            proposal.AiComment |> should equal (Some "（AI提案の開始を待機しています...）")
            // 初期状態は元ファイル名（手動編集用）
            proposal.ProposedFileName |> should equal dummyCandidate.FileName
            // 警告案内メッセージを表示
            updatedModel.ErrorMessage |> should not' (equal None)
            let errMsg = updatedModel.ErrorMessage.Value
            errMsg.Contains("OpenRouter APIキーが未設定") |> should equal true
        )

    [<Fact>]
    let ``RenameCompleted はリネーム後のパス長が閾値未満になった候補を除外する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let model = { initialModel with CurrentThreshold = 50 }

            let p1 = {
                OriginalFullPath = "C:\\Videos\\long_path_that_was_over_limit_sample_1.mp4"
                OriginalFileName = "long_path_that_was_over_limit_sample_1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 43
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "short1.mp4"
                ProposedLength = 20
                ProposedFileNameBytes = 10
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                IsAiProposed = true
                IsAiProcessing = false
                AiError = None
            }
            let p2 = {
                OriginalFullPath = "C:\\Videos\\long_path_that_was_over_limit_sample_2.mp4"
                OriginalFileName = "long_path_that_was_over_limit_sample_2.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 260
                OriginalFileNameBytes = 43
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "still_somewhat_long_file_name_that_exceeds_threshold.mp4"
                ProposedLength = 245
                ProposedFileNameBytes = 55
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                IsAiProposed = true
                IsAiProcessing = false
                AiError = None
            }
            let p3_unselected = {
                OriginalFullPath = "C:\\Videos\\unselected_long_file.mp4"
                OriginalFileName = "unselected_long_file.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 255
                OriginalFileNameBytes = 24
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "unselected_long_file.mp4"
                ProposedLength = 255
                ProposedFileNameBytes = 24
                AiComment = None
                IsSelected = false
                LastWriteTime = DateTime.UtcNow
                IsAiProposed = false
                IsAiProcessing = false
                AiError = None
            }
            let modelWithCandidates = { model with Candidates = [ p1; p2; p3_unselected ] }

            // p1 はリネーム後 20文字 (< 240) -> 抽出基準に該当しなくなり除外
            // p2 はリネーム後 245文字 (>= 240) -> 依然として抽出基準に該当するため、最新パス情報に更新されて候補に残る
            // p3_unselected は未選択のためそのまま残る
            let r1 = {
                Id = Guid.NewGuid()
                Timestamp = DateTime.UtcNow
                OriginalFullPath = p1.OriginalFullPath
                RenamedFullPath = "C:\\Videos\\short1.mp4"
            }
            let r2 = {
                Id = Guid.NewGuid()
                Timestamp = DateTime.UtcNow
                OriginalFullPath = p2.OriginalFullPath
                RenamedFullPath = "C:\\Videos\\still_somewhat_long_file_name_that_exceeds_threshold.mp4"
            }

            let updatedModel, _ = State.update (RenameCompleted (false, Ok [ r1; r2 ])) modelWithCandidates

            // 結果検証
            updatedModel.Candidates.Length |> should equal 2
            // 1. p1 は除外されている
            updatedModel.Candidates |> List.exists (fun c -> c.OriginalFullPath = p1.OriginalFullPath) |> should equal false
            // 2. p2 は更新されて残っている
            let p2Updated = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = r2.RenamedFullPath)
            p2Updated.OriginalFileName |> should equal "still_somewhat_long_file_name_that_exceeds_threshold.mp4"
            p2Updated.OriginalLength |> should equal r2.RenamedFullPath.Length
            p2Updated.IsSelected |> should equal false
            p2Updated.IsAiProposed |> should equal false
            // 3. p3_unselected はそのまま残っている
            let p3Found = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = p3_unselected.OriginalFullPath)
            p3Found.IsSelected |> should equal false
        )

    [<Fact>]
    let ``ModelSelected は選択モデルを更新する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let newModelName = "google/gemini-2.0-flash-exp:free"
            let updatedModel, _ = State.update (ModelSelected newModelName) initialModel
            updatedModel.ErrorMessage |> should equal None
            updatedModel.Settings.SelectedModel |> should equal newModelName
        )

    [<Fact>]
    let ``OpenRuleManager と SaveEditingRule で新規命名規則を追加できる`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let initialRuleCount = initialModel.Settings.Rules.Length

            // 1. マネージャーオープン（編集用テンプレートが初期化される）
            let openModel, _ = State.update OpenRuleManager initialModel
            openModel.IsRuleManagerOpen |> should equal true
            openModel.EditingRule |> should not' (equal None)

            // 2. 編集内容入力
            let m1, _ = State.update (UpdateEditingRuleName "テスト新ルール") openModel
            let m2, _ = State.update (UpdateEditingRulePattern "*.mkv") m1
            let m3, _ = State.update (UpdateEditingRulePrompt "テスト用プロンプト指示") m2
            let m4, _ = State.update (UpdateEditingRuleWebSearch true) m3

            // 3. ルール保存
            let savedModel, _ = State.update SaveEditingRule m4
            savedModel.ErrorMessage |> should equal None
            savedModel.Settings.Rules.Length |> should equal (initialRuleCount + 1)
            let added = savedModel.Settings.Rules |> List.last
            added.Name |> should equal "テスト新ルール"
            added.Pattern |> should equal "*.mkv"
            added.PromptInstruction |> should equal "テスト用プロンプト指示"
            added.EnableWebSearch |> should equal true
            savedModel.SelectedRuleId |> should equal added.Id
        )

    [<Fact>]
    let ``Proposal.createInitial はスキャン候補から即時IO描画用の初期未提案レコードを生成する`` () =
        let candidate: ScanCandidate = {
            FullPath = "C:\\Videos\\long_test_sample.mp4"
            FileName = "long_test_sample.mp4"
            DirectoryPath = "C:\\Videos"
            PathLength = 30
            FileNameBytes = 20
            Violation = WindowsPathOnly
            FileSizeBytes = 2048L
            LastWriteTime = DateTime(2025, 1, 1)
        }
        let initial = Proposal.createInitial candidate
        initial.OriginalFullPath |> should equal candidate.FullPath
        initial.OriginalFileName |> should equal candidate.FileName
        initial.DirectoryPath |> should equal candidate.DirectoryPath
        initial.OriginalLength |> should equal candidate.PathLength
        initial.OriginalFileNameBytes |> should equal candidate.FileNameBytes
        initial.OriginalViolation |> should equal candidate.Violation
        initial.ProposedFileNameBytes |> should equal candidate.FileNameBytes
        initial.ProposedFileName |> should equal candidate.FileName
        initial.ProposedLength |> should equal candidate.PathLength
        initial.AiComment |> should equal (Some "（AI提案の開始を待機しています...）")
        initial.IsSelected |> should equal true
        initial.LastWriteTime |> should equal candidate.LastWriteTime
        initial.IsAiProposed |> should equal false
        initial.IsAiProcessing |> should equal false

    [<Fact>]
    let ``CandidateAiProcessing は対象候補を処理中状態（IsAiProcessing = true）に更新する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 15
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "v1.mp4"
                ProposedLength = 15
                ProposedFileNameBytes = 6
                AiComment = Some "（待機中...）"
                IsAiProposed = false
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let p2 = { p1 with OriginalFullPath = "C:\\Videos\\v2.mp4"; OriginalFileName = "v2.mp4" }
            let model = { initialModel with Candidates = [ p1; p2 ]; IsRequestingAi = true }

            let updatedModel, _ = State.update (CandidateAiProcessing p1.OriginalFullPath) model
            let updatedP1 = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
            let updatedP2 = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = p2.OriginalFullPath)
            updatedP1.IsAiProcessing |> should equal true
            updatedP2.IsAiProcessing |> should equal false
        )

    [<Fact>]
    let ``CandidateAiProposed は1件ごとの提案結果を反映し IsAiProcessing を解除する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "v1.mp4"
                ProposedLength = 250
                ProposedFileNameBytes = 6
                AiComment = Some "（待機中...）"
                IsAiProposed = false
                IsAiProcessing = true
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let model = { initialModel with Candidates = [ p1 ]; IsRequestingAi = true }

            let proposed: RenameProposal = {
                p1 with
                    ProposedFileName = "v1_short.mp4"
                    ProposedLength = 20
                    AiComment = Some "ddgs検索結果より短縮"
                    IsAiProposed = true
                    IsAiProcessing = false
            }
            let updatedModel, _ = State.update (CandidateAiProposed proposed) model
            let result = updatedModel.Candidates.Head
            result.ProposedFileName |> should equal "v1_short.mp4"
            result.ProposedLength |> should equal 20
            result.AiComment |> should equal (Some "ddgs検索結果より短縮")
            result.IsAiProposed |> should equal true
            result.IsAiProcessing |> should equal false
        )

    [<Fact>]
    let ``AllAiProposalsCompleted は IsRequestingAi を false にし CTS をクリアする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cts = new System.Threading.CancellationTokenSource()
            let model = { initialModel with IsRequestingAi = true; AiCancellationCts = Some cts }

            let updatedModel, _ = State.update AllAiProposalsCompleted model
            updatedModel.IsRequestingAi |> should equal false
            updatedModel.AiCancellationCts |> should equal None
        )

    [<Fact>]
    let ``CancelAiProposal は CTS をキャンセルし、処理中フラグを解除して中止コメントを付与する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cts = new System.Threading.CancellationTokenSource()
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "v1.mp4"
                ProposedLength = 250
                ProposedFileNameBytes = 6
                AiComment = Some "（待機中...）"
                IsAiProposed = false
                IsAiProcessing = true
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let model = { initialModel with Candidates = [ p1 ]; IsRequestingAi = true; AiCancellationCts = Some cts }

            let updatedModel, _ = State.update CancelAiProposal model
            cts.IsCancellationRequested |> should equal true
            updatedModel.IsRequestingAi |> should equal false
            updatedModel.AiCancellationCts |> should equal None
            let updatedP1 = updatedModel.Candidates.Head
            updatedP1.IsAiProcessing |> should equal false
            updatedP1.AiComment |> should equal (Some "（AI提案が中止されました）")
        )

    [<Fact>]
    let ``ExecuteScanAndPropose は先行の AI 処理 CTS が存在する場合に自動キャンセルする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cts = new System.Threading.CancellationTokenSource()
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos" }
                    IsRequestingAi = true
                    AiCancellationCts = Some cts
            }

            let updatedModel, _ = State.update ExecuteScanAndPropose model
            cts.IsCancellationRequested |> should equal true
            updatedModel.IsScanning |> should equal true
            updatedModel.AiCancellationCts |> should equal None
        )

    [<Fact>]
    let ``alignCandidatesWithProposals は画面表示用ソート済Proposalsの順序にScanCandidatesを整列する`` () =
        let cShort: ScanCandidate = {
            FullPath = "C:\\a.mp4"
            FileName = "a.mp4"
            DirectoryPath = "C:\\"
            PathLength = 8
            FileNameBytes = 5
            Violation = WindowsPathOnly
            FileSizeBytes = 100L
            LastWriteTime = DateTime.UtcNow
        }
        let cLong: ScanCandidate = {
            FullPath = "C:\\very_long_path_sample_file_12345.mp4"
            FileName = "very_long_path_sample_file_12345.mp4"
            DirectoryPath = "C:\\"
            PathLength = 40
            FileNameBytes = 36
            Violation = WindowsPathOnly
            FileSizeBytes = 100L
            LastWriteTime = DateTime.UtcNow
        }
        let cMid: ScanCandidate = {
            FullPath = "C:\\middle_length_path.mp4"
            FileName = "middle_length_path.mp4"
            DirectoryPath = "C:\\"
            PathLength = 25
            FileNameBytes = 22
            Violation = WindowsPathOnly
            FileSizeBytes = 100L
            LastWriteTime = DateTime.UtcNow
        }
        // 生の走査順（Short -> Long -> Mid）
        let rawCandidates = [ cShort; cLong; cMid ]
        let initialProposals =
            rawCandidates
            |> List.map Proposal.createInitial
            |> FileScanner.sortCandidates PathLengthDesc // 降順: Long -> Mid -> Short

        let sorted = State.alignCandidatesWithProposals rawCandidates initialProposals
        sorted |> List.map (fun c -> c.FullPath) |> should equal [ cLong.FullPath; cMid.FullPath; cShort.FullPath ]

    [<Fact>]
    let ``StartEditRule は指定IDの既存ルールを EditingRule にロードする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let rule1 = initialModel.Settings.Rules.[0]
            let modelWithDialog = { initialModel with IsRuleManagerOpen = true; EditingRule = None }

            let updatedModel, _ = State.update (StartEditRule rule1.Id) modelWithDialog
            updatedModel.EditingRule |> should equal (Some rule1)
        )

    [<Fact>]
    let ``CancelEditRule は編集中ルールを破棄し新規作成用テンプレートにリセットする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let rule1 = initialModel.Settings.Rules.[0]
            let modelEditing = { initialModel with IsRuleManagerOpen = true; EditingRule = Some rule1 }

            let updatedModel, _ = State.update CancelEditRule modelEditing
            match updatedModel.EditingRule with
            | Some newRule ->
                newRule.Id |> should not' (equal rule1.Id)
                newRule.Name |> should equal ""
            | None -> failwith "EditingRule should be Some template"
        )

    [<Fact>]
    let ``SaveEditingRule は既存ルールの編集時に同一位置・順序で上書き更新する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let rule0 = initialModel.Settings.Rules.[0]
            let rule1 = initialModel.Settings.Rules.[1]
            let modifiedRule0 = { rule0 with Name = "更新されたルール名"; PromptInstruction = "新プロンプト指示" }
            let modelEditing = { initialModel with IsRuleManagerOpen = true; EditingRule = Some modifiedRule0 }

            let updatedModel, _ = State.update SaveEditingRule modelEditing
            updatedModel.ErrorMessage |> should equal None
            updatedModel.Settings.Rules.Length |> should equal initialModel.Settings.Rules.Length
            let firstRule = updatedModel.Settings.Rules.[0]
            firstRule.Id |> should equal rule0.Id
            firstRule.Name |> should equal "更新されたルール名"
            firstRule.PromptInstruction |> should equal "新プロンプト指示"
            firstRule.Order |> should equal 0
            // 2番目のルールは影響を受けない
            updatedModel.Settings.Rules.[1].Id |> should equal rule1.Id
        )

    [<Fact>]
    let ``SaveEditingRule は新規ルール追加時に末尾に追加しOrderを正しく付与する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let initialCount = initialModel.Settings.Rules.Length
            let newRuleTemplate: NamingRule = {
                Id = Guid.NewGuid().ToString("N")
                Name = "新規追加ルール"
                Pattern = "{Title}"
                PromptInstruction = "新規指示"
                Order = initialCount
                EnableWebSearch = false
            }
            let modelAdding = { initialModel with IsRuleManagerOpen = true; EditingRule = Some newRuleTemplate }

            let updatedModel, _ = State.update SaveEditingRule modelAdding
            updatedModel.ErrorMessage |> should equal None
            updatedModel.Settings.Rules.Length |> should equal (initialCount + 1)
            let added = updatedModel.Settings.Rules |> List.last
            added.Id |> should equal newRuleTemplate.Id
            added.Name |> should equal "新規追加ルール"
            added.Order |> should equal initialCount
        )

    [<Fact>]
    let ``RequestDeleteRule は確認ダイアログを生成しConfirm時にDeleteRuleが実行される`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let ruleToDelete = initialModel.Settings.Rules.[1] // 2番目のルール
            let initialCount = initialModel.Settings.Rules.Length

            // 1. 削除要求
            let modelWithConfirm, _ = State.update (RequestDeleteRule ruleToDelete.Id) initialModel
            match modelWithConfirm.ConfirmDialog with
            | Some dialog ->
                dialog.Title |> should contain "削除"
                dialog.Message |> should contain ruleToDelete.Name
                dialog.OnConfirm |> should equal (DeleteRule ruleToDelete.Id)

                // 2. 確定実行
                let finalModel, _ = State.update dialog.OnConfirm modelWithConfirm
                finalModel.ErrorMessage |> should equal None
                finalModel.ConfirmDialog |> should equal None
                finalModel.Settings.Rules.Length |> should equal (initialCount - 1)
                finalModel.Settings.Rules |> List.exists (fun r -> r.Id = ruleToDelete.Id) |> should equal false
            | None -> failwith "ConfirmDialog should be Some"
        )

    [<Fact>]
    let ``init は初期状態で FilterNonJapaneseOnly が false である`` () =
        SettingsIsolationHarness.withCleanInitEnvironment (fun () ->
            let model, _ = State.init ()
            model.FilterNonJapaneseOnly |> should equal false
        )

    [<Fact>]
    let ``ToggleFilterNonJapaneseOnly は FilterNonJapaneseOnly フラグを正しく更新する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let modelTrue, _ = State.update (ToggleFilterNonJapaneseOnly true) initialModel
            modelTrue.FilterNonJapaneseOnly |> should equal true

            let modelFalse, _ = State.update (ToggleFilterNonJapaneseOnly false) modelTrue
            modelFalse.FilterNonJapaneseOnly |> should equal false
        )

    [<Fact>]
    let ``UT-STATE-ERR-01: DockerCommandCompleted 成功時は ErrorMessage を None のまま維持しステータス確認コマンドを発行する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let busyModel = { initialModel with IsDockerBusy = true; ErrorMessage = None }
            let updatedModel, cmd = State.update (DockerCommandCompleted ("restart", Ok "tag-based-video-manager")) busyModel

            updatedModel.ErrorMessage |> should equal None
            cmd.IsEmpty |> should equal false
        )

    [<Fact>]
    let ``UT-STATE-ERR-02: DockerCommandCompleted で ContainerError 発生時は IsDockerBusy を解除しエラーメッセージを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let busyModel = { initialModel with IsDockerBusy = true; ErrorMessage = None }
            let updatedModel, cmd =
                State.update (DockerCommandCompleted ("restart", Error (ContainerError ("restart", 1, "daemon error")))) busyModel

            updatedModel.IsDockerBusy |> should equal false
            updatedModel.ErrorMessage |> should equal (Some "コンテナ コマンド 'restart' エラー: daemon error")
            cmd.IsEmpty |> should equal false
        )

    [<Fact>]
    let ``UT-STATE-ERR-03: DockerCommandCompleted で TimeoutError および ValidationError 発生時は IsDockerBusy を解除しエラーメッセージを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let busyModel = { initialModel with IsDockerBusy = true; ErrorMessage = None }

            let timeoutModel, timeoutCmd =
                State.update (DockerCommandCompleted ("start", Error (TimeoutError "start"))) busyModel

            timeoutModel.IsDockerBusy |> should equal false
            timeoutModel.ErrorMessage |> should equal (Some "コンテナ コマンド 'start' がタイムアウトしました。")
            timeoutCmd.IsEmpty |> should equal false

            let validationModel, validationCmd =
                State.update (DockerCommandCompleted ("start", Error (ValidationError "No runtime"))) busyModel

            validationModel.IsDockerBusy |> should equal false
            validationModel.ErrorMessage |> should not' (equal None)
            validationModel.ErrorMessage.Value |> should contain "No runtime"
            validationCmd.IsEmpty |> should equal false
        )

    [<Fact>]
    let ``UT-STATE-FILTER-01: SetConstraintFilter は表示フィルタの状態を更新し、各制約種別に応じた候補絞り込みを可能にする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            initialModel.FilterConstraint |> should equal FilterAll

            // 1. FilterLinuxOnly への切り替え
            let modelLinux, _ = State.update (SetConstraintFilter FilterLinuxOnly) initialModel
            modelLinux.FilterConstraint |> should equal FilterLinuxOnly

            // 2. FilterWindowsOnly への切り替え
            let modelWin, _ = State.update (SetConstraintFilter FilterWindowsOnly) modelLinux
            modelWin.FilterConstraint |> should equal FilterWindowsOnly

            // 3. FilterBothOnly への切り替え
            let modelBoth, _ = State.update (SetConstraintFilter FilterBothOnly) modelWin
            modelBoth.FilterConstraint |> should equal FilterBothOnly

            // 4. FilterAll への復帰
            let modelAll, _ = State.update (SetConstraintFilter FilterAll) modelBoth
            modelAll.FilterConstraint |> should equal FilterAll

            // 5. State.getFilteredCandidates による候補絞り込み実動検証
            let dummyCandidate violation name = {
                OriginalFullPath = $"C:\\videos\\{name}.mp4"
                OriginalFileName = $"{name}.mp4"
                DirectoryPath = "C:\\videos"
                OriginalLength = 50
                OriginalFileNameBytes = 50
                OriginalViolation = violation
                ProposedFileName = $"{name}.mp4"
                ProposedLength = 50
                ProposedFileNameBytes = 50
                AiComment = None
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                IsAiProposed = false
                IsAiProcessing = false
                AiError = None
            }
            let candidates = [
                dummyCandidate LinuxFileNameOnly "linux_only"
                dummyCandidate WindowsPathOnly "windows_only"
                dummyCandidate BothViolated "both_violated"
            ]

            let filteredAll = State.getFilteredCandidates FilterAll candidates
            filteredAll.Length |> should equal 3

            let filteredLinux = State.getFilteredCandidates FilterLinuxOnly candidates
            filteredLinux.Length |> should equal 1
            filteredLinux.Head.OriginalViolation |> should equal LinuxFileNameOnly

            let filteredWindows = State.getFilteredCandidates FilterWindowsOnly candidates
            filteredWindows.Length |> should equal 1
            filteredWindows.Head.OriginalViolation |> should equal WindowsPathOnly

            let filteredBoth = State.getFilteredCandidates FilterBothOnly candidates
            filteredBoth.Length |> should equal 1
            filteredBoth.Head.OriginalViolation |> should equal BothViolated
        )

    [<Fact>]
    let ``ST-01: TargetDirectory = "" のクリーン状態では誤スキャンを実行せず IsScanning を false のまま維持すること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let emptyDirSettings = { initialModel.Settings with TargetDirectory = "" }
            let emptyDirModel = { initialModel with Settings = emptyDirSettings }
            let updatedModel, _ = State.update ExecuteScanAndPropose emptyDirModel
            // TargetDirectory が空文字のときはスキャンを開始せず IsScanning は false のままであること
            updatedModel.IsScanning |> should equal false
        )

    [<Fact>]
    let ``ST-01-MEMBERS: Model は HasScanned, ScannedVideoCount, ActiveEnvironment, SettingsSavePath を保持すること`` () =
        SettingsIsolationHarness.withCleanInitEnvironment (fun () ->
            let initialModel, _ = State.init ()
            let modelType = initialModel.GetType()

            let hasScannedProp = modelType.GetProperty("HasScanned")
            hasScannedProp |> should not' (be null)
            let hasScannedVal = hasScannedProp.GetValue(initialModel) :?> bool
            hasScannedVal |> should equal false

            let scannedCountProp = modelType.GetProperty("ScannedVideoCount")
            scannedCountProp |> should not' (be null)
            let scannedCountVal = scannedCountProp.GetValue(initialModel) :?> int
            scannedCountVal |> should equal 0

            let activeEnvProp = modelType.GetProperty("ActiveEnvironment")
            activeEnvProp |> should not' (be null)
            let activeEnvVal = activeEnvProp.GetValue(initialModel) :?> string
            activeEnvVal |> should not' (be EmptyString)

            let settingsPathProp = modelType.GetProperty("SettingsSavePath")
            settingsPathProp |> should not' (be null)
            let settingsPathVal = settingsPathProp.GetValue(initialModel) :?> string
            settingsPathVal |> should not' (be EmptyString)
        )

    [<Fact>]
    let ``ST-08: IsScanning = true の実行中は TargetDirectoryChanged / ThresholdChanged / RuleSelected / ModelSelected などの入力割り込みを無視・排他すること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let customSettings = {
                initialModel.Settings with
                    TargetDirectory = "C:\\OriginalDir"
                    SelectedModel = "model-1"
            }
            let busyModel = {
                initialModel with
                    Settings = customSettings
                    IsScanning = true
                    CurrentThreshold = 240
                    SelectedRuleId = "rule-1"
            }

            // 1. TargetDirectoryChanged の割り込み無視
            let m1, _ = State.update (TargetDirectoryChanged "D:\\HackedDir") busyModel
            m1.Settings.TargetDirectory |> should equal "C:\\OriginalDir"
            m1.IsScanning |> should equal true

            // 2. ThresholdChanged の割り込み無視
            let m2, _ = State.update (ThresholdChanged 100) busyModel
            m2.CurrentThreshold |> should equal 240

            // 3. RuleSelected の割り込み無視
            let m3, _ = State.update (RuleSelected "rule-other") busyModel
            m3.SelectedRuleId |> should equal "rule-1"

            // 4. ModelSelected の割り込み無視
            let m4, _ = State.update (ModelSelected "model-other") busyModel
            m4.Settings.SelectedModel |> should equal "model-1"

            // 5. ToggleFilterNonJapaneseOnly の割り込み無視
            let origFilter = busyModel.FilterNonJapaneseOnly
            let m5, _ = State.update (ToggleFilterNonJapaneseOnly (not origFilter)) busyModel
            m5.FilterNonJapaneseOnly |> should equal origFilter
        )

    [<Fact>]
    let ``ST-08: IsRenaming = true の実行中は TargetDirectoryChanged / ThresholdChanged などの入力割り込みを無視・排他すること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let customSettings = { initialModel.Settings with TargetDirectory = "C:\\RenamingDir" }
            let renamingModel = {
                initialModel with
                    Settings = customSettings
                    IsRenaming = true
                    CurrentThreshold = 240
            }

            let m1, _ = State.update (TargetDirectoryChanged "D:\\Intruder") renamingModel
            m1.Settings.TargetDirectory |> should equal "C:\\RenamingDir"
            m1.IsRenaming |> should equal true

            let m2, _ = State.update (ThresholdChanged 150) renamingModel
            m2.CurrentThreshold |> should equal 240
        )

    [<Fact>]
    let ``ST-03: ExecuteRenameOnly および ConfirmUndo 実行時に IsRenaming が true になり完了時に false に復帰すること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let candidate: RenameProposal = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 15
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "n1.mp4"
                ProposedLength = 15
                ProposedFileNameBytes = 6
                AiComment = None
                IsAiProposed = true
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let modelWithCandidate = { initialModel with Candidates = [ candidate ] }

            // リネーム実行開始時に IsRenaming = true となること
            let renamingStartModel, _ = State.update ExecuteRenameOnly modelWithCandidate
            renamingStartModel.IsRenaming |> should equal true

            // リネーム完了時に IsRenaming = false に復帰すること
            let dummyUndo: UndoRecord = {
                Id = Guid.NewGuid()
                Timestamp = DateTime.UtcNow
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                RenamedFullPath = "C:\\Videos\\n1.mp4"
            }
            let renameDoneModel, _ = State.update (RenameCompleted (false, Ok [ dummyUndo ])) renamingStartModel
            renameDoneModel.IsRenaming |> should equal false
        )

    [<Fact>]
    let ``ST-05: RenameProposal は個別 AI 失敗を保持する AiError プロパティを有すること`` () =
        let dummyProposal: RenameProposal = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 15
            OriginalFileNameBytes = 6
            OriginalViolation = WindowsPathOnly
            ProposedFileName = "n1.mp4"
            ProposedLength = 15
            ProposedFileNameBytes = 6
            AiComment = None
            IsAiProposed = true
            IsAiProcessing = false
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
            AiError = None
        }
        let proposalType = dummyProposal.GetType()
        let aiErrorProp = proposalType.GetProperty("AiError")
        aiErrorProp |> should not' (be null)

    [<Fact>]
    let ``ST-05: CancelAiProposal 実行時に IsRequestingAi が解除され全残行の IsAiProcessing が false になること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let p1 = {
                OriginalFullPath = "C:\\Videos\\v1.mp4"
                OriginalFileName = "v1.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 15
                OriginalFileNameBytes = 6
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "n1.mp4"
                ProposedLength = 15
                ProposedFileNameBytes = 6
                AiComment = None
                IsAiProposed = false
                IsAiProcessing = true
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let p2 = { p1 with OriginalFullPath = "C:\\Videos\\v2.mp4"; OriginalFileName = "v2.mp4"; IsAiProcessing = true }
            let busyAiModel = {
                initialModel with
                    IsRequestingAi = true
                    Candidates = [ p1; p2 ]
            }

            let cancelledModel, _ = State.update CancelAiProposal busyAiModel
            cancelledModel.IsRequestingAi |> should equal false
            cancelledModel.Candidates |> List.forall (fun c -> not c.IsAiProcessing) |> should equal true
        )

    [<Fact>]
    let ``回帰テスト: ScanCompleted 後に別メッセージを連続ディスパッチしても HasScanned, ScannedVideoCount, ActiveEnvironment, SettingsSavePath, AiError が維持されること`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let dummyCandidate = {
                FullPath = "C:\\Videos\\long_test_video_1.mp4"
                FileName = "long_test_video_1.mp4"
                DirectoryPath = "C:\\Videos"
                PathLength = 250
                FileNameBytes = 22
                Violation = WindowsPathOnly
                FileSizeBytes = 1024L
                LastWriteTime = DateTime.UtcNow
            }

            // 1. ScanCompleted (Ok [ dummyCandidate ])
            let scannedModel, _ = State.update (ScanCompleted (Ok [ dummyCandidate ])) initialModel
            scannedModel.HasScanned |> should equal true
            scannedModel.ScannedVideoCount |> should equal 1
            scannedModel.ActiveEnvironment |> should equal initialModel.ActiveEnvironment
            scannedModel.SettingsSavePath |> should equal initialModel.SettingsSavePath

            // 2. CandidateAiProposed (失敗エラー付きの候補を反映)
            let failedProposal = {
                scannedModel.Candidates.Head with
                    AiError = Some "AI提案失敗: HTTP 500 Internal Server Error"
                    AiComment = Some "（AI提案取得失敗: 500）"
            }
            let aiUpdatedModel, _ = State.update (CandidateAiProposed failedProposal) scannedModel
            aiUpdatedModel.Candidates.Head.AiError |> should equal (Some "AI提案失敗: HTTP 500 Internal Server Error")
            aiUpdatedModel.HasScanned |> should equal true
            aiUpdatedModel.ScannedVideoCount |> should equal 1

            // 3. ToggleCandidateSelect
            let toggledModel, _ = State.update (ToggleCandidateSelect dummyCandidate.FullPath) aiUpdatedModel
            toggledModel.HasScanned |> should equal true
            toggledModel.ScannedVideoCount |> should equal 1
            toggledModel.ActiveEnvironment |> should equal initialModel.ActiveEnvironment
            toggledModel.SettingsSavePath |> should equal initialModel.SettingsSavePath
            toggledModel.Candidates.Head.AiError |> should equal (Some "AI提案失敗: HTTP 500 Internal Server Error")

            // 4. ChangeSortCriterion
            let sortedModel, _ = State.update (ChangeSortCriterion PathLengthAsc) toggledModel
            sortedModel.HasScanned |> should equal true
            sortedModel.ScannedVideoCount |> should equal 1
            sortedModel.ActiveEnvironment |> should equal initialModel.ActiveEnvironment
            sortedModel.SettingsSavePath |> should equal initialModel.SettingsSavePath
            sortedModel.Candidates.Head.AiError |> should equal (Some "AI提案失敗: HTTP 500 Internal Server Error")

            // 5. ThresholdChanged
            let thresholdModel, _ = State.update (ThresholdChanged 200) sortedModel
            thresholdModel.HasScanned |> should equal true
            thresholdModel.ScannedVideoCount |> should equal 1
            thresholdModel.ActiveEnvironment |> should equal initialModel.ActiveEnvironment
            thresholdModel.SettingsSavePath |> should equal initialModel.SettingsSavePath
            thresholdModel.Candidates.Head.AiError |> should equal (Some "AI提案失敗: HTTP 500 Internal Server Error")

            // 6. SetConstraintFilter
            let filterModel, _ = State.update (SetConstraintFilter FilterWindowsOnly) thresholdModel
            filterModel.HasScanned |> should equal true
            filterModel.ScannedVideoCount |> should equal 1
            filterModel.ActiveEnvironment |> should equal initialModel.ActiveEnvironment
            filterModel.SettingsSavePath |> should equal initialModel.SettingsSavePath
            filterModel.Candidates.Head.AiError |> should equal (Some "AI提案失敗: HTTP 500 Internal Server Error")
        )

    [<Fact>]
    let ``ScanCompleted は有効 APIKey かつ未完了 CTS がある場合に先行 CTS をキャンセルし新 CTS と AI リクエスト状態を設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let oldCts = new System.Threading.CancellationTokenSource()
            let scanCand: ScanCandidate = {
                FullPath = "C:\\Videos\\test_candidate.mp4"
                FileName = "test_candidate.mp4"
                DirectoryPath = "C:\\Videos"
                PathLength = 250
                FileNameBytes = 18
                Violation = WindowsPathOnly
                FileSizeBytes = 1024L
                LastWriteTime = DateTime.UtcNow
            }
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-dummy-key-1234567890" }
                    AiCancellationCts = Some oldCts
                    IsScanning = true
            }

            let newModel, _ = State.update (ScanCompleted (Ok [ scanCand ])) model

            try
                // 先行 CTS がキャンセルされていること
                oldCts.IsCancellationRequested |> should equal true
                newModel.IsScanning |> should equal false
                newModel.HasScanned |> should equal true
                newModel.ScannedVideoCount |> should equal 1
                newModel.IsRequestingAi |> should equal true
                newModel.AiCancellationCts.IsSome |> should equal true
            finally
                newModel.AiCancellationCts |> Option.iter (fun cts -> cts.Dispose())
                oldCts.Dispose()
        )

    [<Fact>]
    let ``RetryCandidateAiProposal は有効キーと対象候補がある場合に該当候補のみ IsAiProcessing を true にし AiError をクリアする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let targetPath = "C:\\Videos\\target_video.mp4"
            let cand1: RenameProposal = {
                OriginalFullPath = targetPath
                OriginalFileName = "target_video.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 16
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "target_video.mp4"
                ProposedLength = 250
                ProposedFileNameBytes = 16
                AiComment = Some "エラー発生"
                IsAiProposed = false
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = Some "HTTP 429 レートリミット超過"
            }
            let otherCand: RenameProposal = {
                cand1 with
                    OriginalFullPath = "C:\\Videos\\other.mp4"
                    OriginalFileName = "other.mp4"
                    AiError = Some "他候補のエラー"
            }
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-dummy-key-1234567890" }
                    Candidates = [ cand1; otherCand ]
                    IsRequestingAi = false
            }

            let newModel, _ = State.update (RetryCandidateAiProposal targetPath) model
            newModel.IsRequestingAi |> should equal true
            let updatedTarget = newModel.Candidates |> List.find (fun c -> c.OriginalFullPath = targetPath)
            updatedTarget.IsAiProcessing |> should equal true
            updatedTarget.AiError |> should equal None

            let unaffectedOther = newModel.Candidates |> List.find (fun c -> c.OriginalFullPath = "C:\\Videos\\other.mp4")
            unaffectedOther.IsAiProcessing |> should equal false
            unaffectedOther.AiError |> should equal (Some "他候補のエラー")
        )

    [<Fact>]
    let ``RetryCandidateAiProposal は存在しないパス指定時にモデル状態を変更しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cand: RenameProposal = {
                OriginalFullPath = "C:\\Videos\\existing.mp4"
                OriginalFileName = "existing.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 12
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "existing.mp4"
                ProposedLength = 250
                ProposedFileNameBytes = 12
                AiComment = None
                IsAiProposed = false
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = None
            }
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-dummy-key-1234567890" }
                    Candidates = [ cand ]
            }

            let newModel, _ = State.update (RetryCandidateAiProposal "C:\\Videos\\non_existing.mp4") model
            newModel.Candidates |> should equal model.Candidates
            newModel.IsRequestingAi |> should equal model.IsRequestingAi
        )

    /// <summary>
    /// 【Phase 2.2 統一ガード移行対象】RetryCandidateAiProposal の API キー未設定時エラー検証。
    /// Phase 2.2〜2.3 で validateApiKeyForAi 統一ガード導入時に環境別保存先案内検証（ST-RETRY-01）へ拡張予定。
    /// </summary>
    [<Fact>]
    let ``RetryCandidateAiProposal は APIKey 未設定時にエラーメッセージを設定し処理を開始しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cand: RenameProposal = {
                OriginalFullPath = "C:\\Videos\\existing.mp4"
                OriginalFileName = "existing.mp4"
                DirectoryPath = "C:\\Videos"
                OriginalLength = 250
                OriginalFileNameBytes = 12
                OriginalViolation = WindowsPathOnly
                ProposedFileName = "existing.mp4"
                ProposedLength = 250
                ProposedFileNameBytes = 12
                AiComment = None
                IsAiProposed = false
                IsAiProcessing = false
                IsSelected = true
                LastWriteTime = DateTime.UtcNow
                AiError = Some "エラー"
            }
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = None }
                    Candidates = [ cand ]
                    IsRequestingAi = false
            }

            let newModel, _ = State.update (RetryCandidateAiProposal "C:\\Videos\\existing.mp4") model
            newModel.ErrorMessage |> should equal (Some "OpenRouter APIキーが設定されていません。")
            newModel.IsRequestingAi |> should equal false
            newModel.Candidates.Head.IsAiProcessing |> should equal false
        )

    [<Fact>]
    let ``ScanCompleted (Ok []) は危険な長パス超過候補が0件の場合にHasScannedをtrueにしメッセージを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let model = { initialModel with IsScanning = true; ScannedVideoCount = 5 }
            let newModel, cmd = State.update (ScanCompleted (Ok [])) model
            newModel.IsScanning |> should equal false
            newModel.HasScanned |> should equal true
            newModel.ScannedVideoCount |> should equal 0
            newModel.ErrorMessage |> should equal (Some "閾値以上の危険な長パス動画ファイルは見つかりませんでした。")
            newModel.Candidates.IsEmpty |> should equal true
        )

    [<Fact>]
    let ``ExecuteScanAndPropose は未キャンセルCTSがある場合に先行CTSをキャンセル・破棄してIsScanningをtrueにする`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let oldCts = new System.Threading.CancellationTokenSource()
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos" }
                    AiCancellationCts = Some oldCts
                    IsScanning = false
            }
            try
                let newModel, _ = State.update ExecuteScanAndPropose model
                oldCts.IsCancellationRequested |> should equal true
                newModel.IsScanning |> should equal true
                newModel.ErrorMessage |> should equal None
                newModel.Candidates.IsEmpty |> should equal true
                newModel.AiCancellationCts |> should equal None
            finally
                oldCts.Dispose()
        )

    [<Fact>]
    let ``State.update の各種メッセージ遷移（SettingsLoadedエラー、Dismiss系、ルールのCRUD・順序入替）が正常に処理される`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath

            // 1. SettingsLoaded (Error _)
            let mSettingsErr, _ = State.update (SettingsLoaded (Error (SettingsError "不正な設定"))) initialModel
            mSettingsErr.Settings |> should equal initialModel.Settings

            // 2. DismissError
            let mWithError = { initialModel with ErrorMessage = Some "通知メッセージ" }
            let mDismissErr, _ = State.update DismissError mWithError
            mDismissErr.ErrorMessage |> should equal None

            // 3. DismissConfirm
            let dialog: DialogConfig = {
                Title = "確認"
                Message = "本当に実行しますか？"
                ConfirmText = "OK"
                CancelText = "Cancel"
                OnConfirm = DismissError
            }
            let mWithDialog = { initialModel with ConfirmDialog = Some dialog }
            let mDismissConfirm, _ = State.update DismissConfirm mWithDialog
            mDismissConfirm.ConfirmDialog |> should equal None

            // 4. RequestDeleteRule (残り1件の時は削除不可エラー)
            let singleRule: NamingRule = {
                Id = "single-rule"
                Name = "単一ルール"
                Pattern = "{Title}.mp4"
                PromptInstruction = "短縮"
                Order = 0
                EnableWebSearch = false
            }
            let mSingleRule = { initialModel with Settings = { initialModel.Settings with Rules = [ singleRule ] } }
            let mReqDelSingle, _ = State.update (RequestDeleteRule "single-rule") mSingleRule
            mReqDelSingle.ErrorMessage |> should equal (Some "最後の1件の命名規則は削除できません。")
            mReqDelSingle.ConfirmDialog |> should equal None

            // 5. RequestDeleteRule (複数件ある時は確認ダイアログ表示) & DeleteRule
            let rule1: NamingRule = {
                Id = "rule-1"
                Name = "ルール1"
                Pattern = "{Title}_1.mp4"
                PromptInstruction = "指示1"
                Order = 0
                EnableWebSearch = false
            }
            let rule2: NamingRule = {
                Id = "rule-2"
                Name = "ルール2"
                Pattern = "{Title}_2.mp4"
                PromptInstruction = "指示2"
                Order = 1
                EnableWebSearch = true
            }
            let mMultiRule = { initialModel with Settings = { initialModel.Settings with Rules = [ rule1; rule2 ] } }
            let mReqDelMulti, _ = State.update (RequestDeleteRule "rule-1") mMultiRule
            mReqDelMulti.ConfirmDialog.IsSome |> should equal true
            mReqDelMulti.ConfirmDialog.Value.Title |> should contain "削除"

            // DeleteRule で削除実行
            let mDeleted, _ = State.update (DeleteRule "rule-1") mReqDelMulti
            mDeleted.ErrorMessage |> should equal None
            mDeleted.Settings.Rules.Length |> should equal 1
            mDeleted.Settings.Rules.Head.Id |> should equal "rule-2"
            mDeleted.ConfirmDialog |> should equal None

            // 6. SaveRule (既存更新 & 新規追加)
            let updatedRule2 = { rule2 with Name = "更新ルール2" }
            let mUpdatedRule, _ = State.update (SaveRule updatedRule2) mDeleted
            mUpdatedRule.ErrorMessage |> should equal None
            mUpdatedRule.Settings.Rules.Head.Name |> should equal "更新ルール2"

            let newRule3: NamingRule = {
                Id = "rule-3"
                Name = "ルール3"
                Pattern = "{Title}_3.mp4"
                PromptInstruction = "指示3"
                Order = 1
                EnableWebSearch = false
            }
            let mAddedRule, _ = State.update (SaveRule newRule3) mUpdatedRule
            mAddedRule.ErrorMessage |> should equal None
            mAddedRule.Settings.Rules.Length |> should equal 2

            // 7. MoveRuleOrder (順序入替)
            // 現在: rule-2 (Order 0), rule-3 (Order 1)
            // rule-3 を上へ移動 (-1)
            let mMovedUp, _ = State.update (MoveRuleOrder ("rule-3", -1)) mAddedRule
            mMovedUp.ErrorMessage |> should equal None
            mMovedUp.Settings.Rules.Head.Id |> should equal "rule-3"

            // 先頭 (rule-3) をさらに上へ移動 (-1) -> 境界チェックにより状態不変
            let mTopBound, _ = State.update (MoveRuleOrder ("rule-3", -1)) mMovedUp
            mTopBound.ErrorMessage |> should equal None
            mTopBound.Settings.Rules |> should equal mMovedUp.Settings.Rules

            // 末尾 (rule-2) をさらに下へ移動 (+1) -> 境界チェックにより状態不変
            let mBottomBound, _ = State.update (MoveRuleOrder ("rule-2", 1)) mMovedUp
            mBottomBound.ErrorMessage |> should equal None
            mBottomBound.Settings.Rules |> should equal mMovedUp.Settings.Rules
        )

    [<Fact>]
    let ``State.update は IsScanning または IsRenaming 中に入力割り込みメッセージを無視し状態を変更しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let busyScanningModel = { initialModel with IsScanning = true; IsRenaming = false }
            let busyRenamingModel = { initialModel with IsScanning = false; IsRenaming = true }

            let testMessages: Msg list = [
                TargetDirectoryChanged "C:\\NewDir"
                ThresholdChanged 180
                RuleSelected "rule-other"
                ModelSelected "model-other"
                ToggleFilterNonJapaneseOnly true
                SetConstraintFilter FilterLinuxOnly
            ]

            for busyModel in [ busyScanningModel; busyRenamingModel ] do
                for msg in testMessages do
                    let updatedModel, cmd = State.update msg busyModel
                    updatedModel |> should equal busyModel
                    cmd.IsEmpty |> should equal true
        )
