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
            let runId = Guid.NewGuid()
            let model = { initialModel with Candidates = [ p1; p2 ]; IsRequestingAi = true; AiPipelineRunId = Some runId }

            let updatedModel, _ = State.update (CandidateAiProcessing (runId, p1.OriginalFullPath)) model
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
            let runId = Guid.NewGuid()
            let model = { initialModel with Candidates = [ p1 ]; IsRequestingAi = true; AiPipelineRunId = Some runId }

            let proposed: RenameProposal = {
                p1 with
                    ProposedFileName = "v1_short.mp4"
                    ProposedLength = 20
                    AiComment = Some "ddgs検索結果より短縮"
                    IsAiProposed = true
                    IsAiProcessing = false
            }
            let updatedModel, _ = State.update (CandidateAiProposed (runId, proposed)) model
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
            let runId = Guid.NewGuid()
            let cts = new System.Threading.CancellationTokenSource()
            let model = { initialModel with IsRequestingAi = true; AiCancellationCts = Some cts; AiPipelineRunId = Some runId }

            let updatedModel, _ = State.update (AllAiProposalsCompleted runId) model
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
            let baseIsolatedModel = initIsolatedModel tempSavePath
            let initialModel = {
                baseIsolatedModel with
                    Settings = { baseIsolatedModel.Settings with ApiKey = Some "sk-or-v1-regression-valid-001" }
            }
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
            try
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
                let runId = scannedModel.AiPipelineRunId |> Option.defaultValue Guid.Empty
                let aiUpdatedModel, _ = State.update (CandidateAiProposed (runId, failedProposal)) scannedModel
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
            finally
                scannedModel.AiCancellationCts |> Option.iter (fun c -> try c.Dispose() with _ -> ())
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
    /// RetryCandidateAiProposal の API キー未設定時エラー検証（統一ガード仕様）。
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
                    ApiKeySourcePath = None
                    Candidates = [ cand ]
                    IsRequestingAi = false
            }

            let newModel, _ = State.update (RetryCandidateAiProposal "C:\\Videos\\existing.mp4") model
            newModel.IsRequestingAi |> should equal false
            newModel.AiPipelineRunId |> should equal None
            newModel.Candidates.Head.IsAiProcessing |> should equal false
            newModel.ErrorMessage |> should not' (equal None)
            newModel.ErrorMessage.Value |> should contain "OpenRouter APIキーが未設定"
            newModel.ErrorMessage.Value |> should contain tempSavePath
            newModel.ErrorMessage.Value |> should contain "\"apiKey\": \"sk-or-v1-...\""
            newModel.ErrorMessage.Value |> should contain "🔄 設定を再読込"
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

    // =========================================================================
    // Phase 2.2 先行失敗テスト (TDD Red: State.fs 統一ガード・再読込・レース・ロールバック)
    // =========================================================================

    /// <summary>テスト用一時 ConfigLoadContext（擬似リポジトリ階層）を構築し、実行後に確実に削除する</summary>
    let private withIsolatedRepoContext (envName: string) (action: string -> string -> ConfigLoadContext -> unit) : unit =
        withIsolatedStateTest (fun _ ->
            let tempRoot = Path.Combine(Path.GetTempPath(), "RenamerStateCtx_" + Guid.NewGuid().ToString("N"))
            let repoDir = Path.Combine(tempRoot, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            let appBaseDir = Path.Combine(tempRoot, "bin")
            let appDataDir = Path.Combine(tempRoot, "appdata")
            Directory.CreateDirectory(renamerDir) |> ignore
            Directory.CreateDirectory(appBaseDir) |> ignore
            Directory.CreateDirectory(appDataDir) |> ignore

            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            try
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", envName)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)
                let ctx: ConfigLoadContext = {
                    CurrentDirectory = repoDir
                    AppBaseDirectory = appBaseDir
                    AppDataDirectory = appDataDir
                    DotEnvPath = None
                }
                action repoDir renamerDir ctx
            finally
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
                if Directory.Exists(tempRoot) then
                    for file in Directory.GetFiles(tempRoot, "*", SearchOption.AllDirectories) do
                        try
                            let attrs = File.GetAttributes(file)
                            if (attrs &&& FileAttributes.ReadOnly) <> enum 0 then
                                File.SetAttributes(file, FileAttributes.Normal)
                        with _ -> ()
                    try Directory.Delete(tempRoot, true) with _ -> ()
        )

    /// <summary>テスト用 ScanCandidate を生成するヘルパー</summary>
    let private mkScanCand (fullPath: string) : ScanCandidate =
        let fileName = Path.GetFileName(fullPath)
        let dir = Path.GetDirectoryName(fullPath)
        {
            FullPath = fullPath
            FileName = fileName
            DirectoryPath = if isNull dir then "" else dir
            PathLength = max 250 fullPath.Length
            FileNameBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
            Violation = WindowsPathOnly
            FileSizeBytes = 1024L
            LastWriteTime = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }

    /// <summary>テスト用 RenameProposal を生成するヘルパー</summary>
    let private mkProposal (fullPath: string) (aiErrOpt: string option) : RenameProposal =
        let fileName = Path.GetFileName(fullPath)
        let dir = Path.GetDirectoryName(fullPath)
        {
            OriginalFullPath = fullPath
            OriginalFileName = fileName
            DirectoryPath = if isNull dir then "" else dir
            OriginalLength = max 250 fullPath.Length
            OriginalFileNameBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
            OriginalViolation = WindowsPathOnly
            ProposedFileName = fileName
            ProposedLength = max 250 fullPath.Length
            ProposedFileNameBytes = System.Text.Encoding.UTF8.GetByteCount(fileName)
            AiComment = Some "初期コメント"
            IsAiProposed = false
            IsAiProcessing = false
            IsSelected = true
            LastWriteTime = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            AiError = aiErrOpt
        }

    /// <summary>テスト用 NamingRule を生成するヘルパー</summary>
    let private mkTestRule (id: string) (name: string) (order: int) : NamingRule =
        {
            Id = id
            Name = name
            Pattern = "{Code}_{Title}.mp4"
            PromptInstruction = $"ルール指示: {name}"
            Order = order
            EnableWebSearch = (order % 2 = 0)
        }

    /// <summary>UT-RED-04: RetryCandidateAiProposal のプレースホルダー検証欠落を再現する先行 Red テスト</summary>
    [<Fact>]
    let ``UT-RED-04: RetryCandidateAiProposal はプレースホルダー API キー時に AI 処理を開始せず出典パス付きエラーを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let sourcePath = "C:\\repo\\src\\TagBasedVideoManager.Renamer\\appsettings.Development.json"
            let cand = mkProposal "C:\\Videos\\placeholder_retry_target.mp4" (Some "前回失敗")
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "xxx" }
                    ApiKeySourcePath = Some sourcePath
                    Candidates = [ cand ]
                    IsRequestingAi = false
            }

            let updatedModel, _ = State.update (RetryCandidateAiProposal cand.OriginalFullPath) model

            updatedModel.IsRequestingAi |> should equal false
            updatedModel.AiPipelineRunId |> should equal None
            updatedModel.Candidates.Head.IsAiProcessing |> should equal false
            updatedModel.ErrorMessage |> should not' (equal None)
            updatedModel.ErrorMessage.Value |> should contain sourcePath
            updatedModel.ErrorMessage.Value |> should contain "プレースホルダー"
            updatedModel.ErrorMessage.Value |> should contain "🔄 設定を再読込"
        )

    /// <summary>ST-INIT-01: initWithContext による同期メタデータロード確定と起動時二重ロード廃止を検証する</summary>
    [<Fact>]
    let ``ST-INIT-01: initWithContext はメタデータと保存先を同期確定し冗長な起動時 SettingsLoaded コマンドを発行しない`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let basePath = Path.Combine(renamerDir, "appsettings.json")
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null, "pathLengthThreshold": 240 }""")
            File.WriteAllText(
                devPath,
                """{
                  "apiKey": "sk-or-v1-init-valid-001",
                  "selectedModel": "custom/init-model",
                  "pathLengthThreshold": 210,
                  "rules": [
                    { "id": "r-first", "name": "先頭ルール", "pattern": "{Code}.mp4", "promptInstruction": "指示", "order": 0, "enableWebSearch": true }
                  ]
                }"""
            )

            let model, cmd = State.initWithContext (Some ctx)

            model.Settings.ApiKey |> should equal (Some "sk-or-v1-init-valid-001")
            model.Settings.SelectedModel |> should equal "custom/init-model"
            model.CurrentThreshold |> should equal 210
            model.SelectedRuleId |> should equal "r-first"
            model.ActiveEnvironment |> should equal "Development"
            model.SettingsSavePath |> should equal devPath
            model.ApiKeySourcePath |> should equal (Some devPath)
            model.AiPipelineRunId |> should equal None
            model.ErrorMessage |> should equal None
            // 起動時 loadSettingsCmd は廃止され checkDockerCmd の1本のみ発行されること
            cmd.Length |> should equal 1
        )

    /// <summary>ST-INIT-02: initWithContext で起動時 JSON 構文エラー・旧形式警告が ErrorMessage へ反映されることを検証する</summary>
    [<Fact>]
    let ``ST-INIT-02: initWithContext は起動時 JSON 構文エラーや旧 value オブジェクト形式の警告を ErrorMessage バナーへ反映する`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let basePath = Path.Combine(renamerDir, "appsettings.json")
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null, "selectedModel": "fallback/model" }""")
            File.WriteAllText(devPath, """{ "apiKey": "sk-or-v1-broken" """)

            let syntaxErrModel, _ = State.initWithContext (Some ctx)
            syntaxErrModel.Settings.SelectedModel |> should equal "fallback/model"
            syntaxErrModel.ErrorMessage |> should not' (equal None)
            syntaxErrModel.ErrorMessage.Value |> should contain devPath
            syntaxErrModel.ErrorMessage.Value |> should contain "構文解析に失敗しました"

            File.WriteAllText(devPath, """{ "apiKey": { "value": "sk-or-v1-legacy-obj" } }""")
            let legacyModel, _ = State.initWithContext (Some ctx)
            legacyModel.Settings.ApiKey |> should equal None
            legacyModel.ErrorMessage |> should not' (equal None)
            legacyModel.ErrorMessage.Value |> should contain devPath
            legacyModel.ErrorMessage.Value |> should contain "文字列形式"
        )

    /// <summary>ST-SCAN-01: 有効 API キー時の ScanCompleted が先行 CTS を破棄し新 CTS・新 runId を発行することを検証する</summary>
    [<Fact>]
    let ``ST-SCAN-01: ScanCompleted は有効 API キー時に先行 CTS をキャンセル破棄し新 CTS と AiPipelineRunId を設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let oldCts = new System.Threading.CancellationTokenSource()
            let oldRunId = Guid.NewGuid()
            let cand = mkScanCand "C:\\Videos\\valid_scan_01.mp4"
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-scan-01" }
                    IsScanning = true
                    AiCancellationCts = Some oldCts
                    AiPipelineRunId = Some oldRunId
            }

            let newModel, _ = State.updateWithContext None (ScanCompleted (Ok [ cand ])) model
            try
                oldCts.IsCancellationRequested |> should equal true
                Assert.Throws<ObjectDisposedException>(fun () -> ignore oldCts.Token) |> ignore
                newModel.IsScanning |> should equal false
                newModel.HasScanned |> should equal true
                newModel.ScannedVideoCount |> should equal 1
                newModel.IsRequestingAi |> should equal true
                newModel.AiCancellationCts.IsSome |> should equal true
                newModel.AiPipelineRunId.IsSome |> should equal true
                newModel.AiPipelineRunId.Value |> should not' (equal oldRunId)
                newModel.ErrorMessage |> should equal None
            finally
                newModel.AiCancellationCts |> Option.iter (fun c -> try c.Dispose() with _ -> ())
                try oldCts.Dispose() with _ -> ()
        )

    /// <summary>ST-SCAN-02: プレースホルダー API キー時の ScanCompleted が出典パス・最小 JSON 例・再読込案内を設定することを検証する</summary>
    [<Fact>]
    let ``ST-SCAN-02: ScanCompleted はプレースホルダー API キー時に AI 開始せず出典パスと再読込案内を表示する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let sourcePath = "/cfg/appsettings.Development.json"
            let cand = mkScanCand "C:\\Videos\\placeholder_scan_02.mp4"
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "xxx" }
                    ApiKeySourcePath = Some sourcePath
                    SettingsSavePath = tempSavePath
                    IsScanning = true
            }

            let newModel, cmd = State.updateWithContext None (ScanCompleted (Ok [ cand ])) model
            newModel.IsScanning |> should equal false
            newModel.IsRequestingAi |> should equal false
            newModel.AiPipelineRunId |> should equal None
            newModel.HasScanned |> should equal true
            newModel.ScannedVideoCount |> should equal 1
            newModel.Candidates.Length |> should equal 1
            cmd.IsEmpty |> should equal true
            newModel.ErrorMessage |> should not' (equal None)
            newModel.ErrorMessage.Value |> should contain sourcePath
            newModel.ErrorMessage.Value |> should contain "プレースホルダー"
            newModel.ErrorMessage.Value |> should contain "\"apiKey\": \"sk-or-v1-...\""
            newModel.ErrorMessage.Value |> should contain "🔄 設定を再読込"
        )

    /// <summary>ST-SCAN-03: API キー未設定時の ScanCompleted がベースではなく環境別 SettingsSavePath を案内することを検証する</summary>
    [<Fact>]
    let ``ST-SCAN-03: ScanCompleted は API キー未設定時に環境別 SettingsSavePath と最小 JSON 例を案内する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let envSavePath = "C:\\repo\\src\\TagBasedVideoManager.Renamer\\appsettings.Development.json"
            let cand = mkScanCand "C:\\Videos\\missing_key_scan_03.mp4"
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = None }
                    ApiKeySourcePath = None
                    SettingsSavePath = envSavePath
                    IsScanning = true
            }

            let newModel, cmd = State.updateWithContext None (ScanCompleted (Ok [ cand ])) model
            newModel.IsScanning |> should equal false
            newModel.IsRequestingAi |> should equal false
            newModel.AiPipelineRunId |> should equal None
            newModel.Candidates.Length |> should equal 1
            cmd.IsEmpty |> should equal true
            newModel.ErrorMessage |> should not' (equal None)
            newModel.ErrorMessage.Value |> should contain envSavePath
            Assert.False(
                newModel.ErrorMessage.Value.Contains("\\appsettings.json"),
                "ベース appsettings.json ではなく環境別保存先を案内する必要があります。"
            )
            newModel.ErrorMessage.Value |> should contain "\"apiKey\": \"sk-or-v1-...\""
            newModel.ErrorMessage.Value |> should contain "🔄 設定を再読込"
        )

    /// <summary>ST-SCAN-04: 非 AI 分岐での ScanCompleted 単体呼び出しでも IsRequestingAi=false / AiPipelineRunId=None / AiCancellationCts=None へリセットされることを検証する</summary>
    [<Fact>]
    let ``ST-SCAN-04: ScanCompleted 単体の全非 AI 分岐でも IsRequestingAi と AiPipelineRunId と AiCancellationCts が確実にリセットされる`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cand = mkScanCand "C:\\Videos\\direct_scan_04.mp4"

            // 1. 通常の候補 0 件完了
            let baseModel = { initialModel with IsScanning = true; ScannedVideoCount = 3 }
            let emptyModel, cmdEmpty = State.updateWithContext None (ScanCompleted (Ok [])) baseModel
            emptyModel.IsScanning |> should equal false
            emptyModel.IsRequestingAi |> should equal false
            emptyModel.AiPipelineRunId |> should equal None
            emptyModel.AiCancellationCts |> should equal None
            emptyModel.HasScanned |> should equal true
            emptyModel.ScannedVideoCount |> should equal 0
            emptyModel.ErrorMessage |> should equal (Some "閾値以上の危険な長パス動画ファイルは見つかりませんでした。")
            cmdEmpty.IsEmpty |> should equal true

            // 2. ExecuteScanAndPropose を事前に挟まず IsRequestingAi=true / AiPipelineRunId=Some / AiCancellationCts=Some 状態で
            //    直接 ScanCompleted の全非 AI 分岐（Ok [], Error _, PlaceholderApiKey, MissingApiKey）を呼んでもリセットされること
            let assertDirectReset (label: string) (apiKeyOpt: string option) (msg: Msg) =
                let activeRunId = Guid.NewGuid()
                let activeCts = new System.Threading.CancellationTokenSource()
                try
                    let activeModel = {
                        initialModel with
                            Settings = { initialModel.Settings with ApiKey = apiKeyOpt }
                            ApiKeySourcePath = if apiKeyOpt.IsSome then Some tempSavePath else None
                            IsScanning = true
                            IsRequestingAi = true
                            AiPipelineRunId = Some activeRunId
                            AiCancellationCts = Some activeCts
                    }
                    let res, _ = State.updateWithContext None msg activeModel
                    Assert.False(res.IsScanning, $"{label}: IsScanning は false になる必要があります")
                    Assert.False(res.IsRequestingAi, $"{label}: IsRequestingAi は false にリセットされる必要があります")
                    Assert.True(res.AiPipelineRunId.IsNone, $"{label}: AiPipelineRunId は None にリセットされる必要があります")
                    Assert.True(res.AiCancellationCts.IsNone, $"{label}: AiCancellationCts は None にリセットされる必要があります")
                finally
                    try activeCts.Dispose() with _ -> ()

            assertDirectReset "Direct Ok []" (Some "sk-or-v1-valid-04") (ScanCompleted (Ok []))
            assertDirectReset "Direct Error _" (Some "sk-or-v1-valid-04") (ScanCompleted (Error (IoError ("直接走査エラー", None))))
            assertDirectReset "Direct PlaceholderApiKey" (Some "xxx") (ScanCompleted (Ok [ cand ]))
            assertDirectReset "Direct MissingApiKey" None (ScanCompleted (Ok [ cand ]))
        )

    /// <summary>ST-SCAN-05: IsRequestingAi=true 中の割り込み ExecuteScanAndPropose 後に全非 AI 分岐で IsRequestingAi=false / AiPipelineRunId=None にリセットされることを検証する</summary>
    [<Fact>]
    let ``ST-SCAN-05: IsRequestingAi=true 中の割り込み再スキャン後の全非 AI 分岐で IsRequestingAi と AiPipelineRunId が確実にリセットされる`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cand = mkScanCand "C:\\Videos\\interrupt_scan_05.mp4"

            // ① 割り込み -> 対象 0 件 ScanCompleted (Ok []) -> 遅延 oldRunId 到着
            let oldRunId1 = Guid.NewGuid()
            let oldCts1 = new System.Threading.CancellationTokenSource()
            try
                let busy1 = {
                    initialModel with
                        Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos"; ApiKey = Some "sk-or-v1-valid-1234" }
                        IsRequestingAi = true
                        AiPipelineRunId = Some oldRunId1
                        AiCancellationCts = Some oldCts1
                }
                let scanning1, _ = State.updateWithContext None ExecuteScanAndPropose busy1
                oldCts1.IsCancellationRequested |> should equal true
                scanning1.IsScanning |> should equal true
                scanning1.IsRequestingAi |> should equal false
                scanning1.AiPipelineRunId |> should equal None
                scanning1.AiCancellationCts |> should equal None

                let empty1, _ = State.updateWithContext None (ScanCompleted (Ok [])) scanning1
                empty1.IsScanning |> should equal false
                empty1.IsRequestingAi |> should equal false
                empty1.AiPipelineRunId |> should equal None
                empty1.AiCancellationCts |> should equal None

                let afterDelayed1, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId1) empty1
                afterDelayed1 |> should equal empty1
            finally
                try oldCts1.Dispose() with _ -> ()

            // ② 割り込み -> ScanCompleted (Error _)
            let oldRunId2 = Guid.NewGuid()
            let oldCts2 = new System.Threading.CancellationTokenSource()
            try
                let busy2 = {
                    initialModel with
                        Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos" }
                        IsRequestingAi = true
                        AiPipelineRunId = Some oldRunId2
                        AiCancellationCts = Some oldCts2
                }
                let scanning2, _ = State.updateWithContext None ExecuteScanAndPropose busy2
                let err2, _ = State.updateWithContext None (ScanCompleted (Error (IoError ("フォルダ不在", None)))) scanning2
                err2.IsScanning |> should equal false
                err2.IsRequestingAi |> should equal false
                err2.AiPipelineRunId |> should equal None
                err2.AiCancellationCts |> should equal None
                let afterDelayed2, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId2) err2
                afterDelayed2 |> should equal err2
            finally
                try oldCts2.Dispose() with _ -> ()

            // ③ 割り込み -> PlaceholderApiKey 分岐
            let oldRunId3 = Guid.NewGuid()
            let oldCts3 = new System.Threading.CancellationTokenSource()
            try
                let busy3 = {
                    initialModel with
                        Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos"; ApiKey = Some "xxx" }
                        ApiKeySourcePath = Some tempSavePath
                        IsRequestingAi = true
                        AiPipelineRunId = Some oldRunId3
                        AiCancellationCts = Some oldCts3
                }
                let scanning3, _ = State.updateWithContext None ExecuteScanAndPropose busy3
                let placeholder3, _ = State.updateWithContext None (ScanCompleted (Ok [ cand ])) scanning3
                placeholder3.IsScanning |> should equal false
                placeholder3.IsRequestingAi |> should equal false
                placeholder3.AiPipelineRunId |> should equal None
                placeholder3.AiCancellationCts |> should equal None
                let afterDelayed3, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId3) placeholder3
                afterDelayed3 |> should equal placeholder3
            finally
                try oldCts3.Dispose() with _ -> ()

            // ④ 割り込み -> MissingApiKey 分岐
            let oldRunId4 = Guid.NewGuid()
            let oldCts4 = new System.Threading.CancellationTokenSource()
            try
                let busy4 = {
                    initialModel with
                        Settings = { initialModel.Settings with TargetDirectory = "C:\\Videos"; ApiKey = None }
                        ApiKeySourcePath = None
                        SettingsSavePath = tempSavePath
                        IsRequestingAi = true
                        AiPipelineRunId = Some oldRunId4
                        AiCancellationCts = Some oldCts4
                }
                let scanning4, _ = State.updateWithContext None ExecuteScanAndPropose busy4
                let missing4, _ = State.updateWithContext None (ScanCompleted (Ok [ cand ])) scanning4
                missing4.IsScanning |> should equal false
                missing4.IsRequestingAi |> should equal false
                missing4.AiPipelineRunId |> should equal None
                missing4.AiCancellationCts |> should equal None
                missing4.ErrorMessage |> should not' (equal None)
                missing4.ErrorMessage.Value |> should contain tempSavePath
                let afterDelayed4, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId4) missing4
                afterDelayed4 |> should equal missing4
            finally
                try oldCts4.Dispose() with _ -> ()

            // ⑤ TargetDirectory = "" による早期リターン分岐でもリセットされること
            let oldRunId5 = Guid.NewGuid()
            let oldCts5 = new System.Threading.CancellationTokenSource()
            try
                let busy5 = {
                    initialModel with
                        Settings = { initialModel.Settings with TargetDirectory = "" }
                        IsRequestingAi = true
                        AiPipelineRunId = Some oldRunId5
                        AiCancellationCts = Some oldCts5
                }
                let early5, cmd5 = State.updateWithContext None ExecuteScanAndPropose busy5
                oldCts5.IsCancellationRequested |> should equal true
                early5.IsScanning |> should equal false
                early5.IsRequestingAi |> should equal false
                early5.AiPipelineRunId |> should equal None
                early5.AiCancellationCts |> should equal None
                cmd5.IsEmpty |> should equal true
                let afterDelayed5, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId5) early5
                afterDelayed5 |> should equal early5
            finally
                try oldCts5.Dispose() with _ -> ()
        )

    /// <summary>ST-RETRY-01: RetryCandidateAiProposal が先行 CTS を破棄し新 CTS と新 runId を保持することを検証する</summary>
    [<Fact>]
    let ``ST-RETRY-01: RetryCandidateAiProposal は有効キー時に先行 CTS をキャンセル破棄し新 CTS と新 AiPipelineRunId を設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let oldCts = new System.Threading.CancellationTokenSource()
            let oldRunId = Guid.NewGuid()
            let targetCand = mkProposal "C:\\Videos\\retry_target.mp4" (Some "HTTP 500")
            let otherCand = mkProposal "C:\\Videos\\retry_other.mp4" (Some "他候補エラー")
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-retry-01" }
                    Candidates = [ targetCand; otherCand ]
                    IsRequestingAi = true
                    AiCancellationCts = Some oldCts
                    AiPipelineRunId = Some oldRunId
            }

            let newModel, _ = State.updateWithContext None (RetryCandidateAiProposal targetCand.OriginalFullPath) model
            try
                oldCts.IsCancellationRequested |> should equal true
                Assert.Throws<ObjectDisposedException>(fun () -> ignore oldCts.Token) |> ignore
                newModel.IsRequestingAi |> should equal true
                newModel.AiCancellationCts.IsSome |> should equal true
                newModel.AiPipelineRunId.IsSome |> should equal true
                newModel.AiPipelineRunId.Value |> should not' (equal oldRunId)
                let updatedTarget = newModel.Candidates |> List.find (fun c -> c.OriginalFullPath = targetCand.OriginalFullPath)
                updatedTarget.IsAiProcessing |> should equal true
                updatedTarget.AiError |> should equal None
                let untouchedOther = newModel.Candidates |> List.find (fun c -> c.OriginalFullPath = otherCand.OriginalFullPath)
                untouchedOther.IsAiProcessing |> should equal false
                untouchedOther.AiError |> should equal (Some "他候補エラー")
            finally
                newModel.AiCancellationCts |> Option.iter (fun c -> try c.Dispose() with _ -> ())
                try oldCts.Dispose() with _ -> ()
        )

    /// <summary>ST-RETRY-02: RetryCandidateAiProposal のプレースホルダー検証が ScanCompleted と同一の統一ガードで動作することを検証する</summary>
    [<Fact>]
    let ``ST-RETRY-02: RetryCandidateAiProposal はプレースホルダー時に ScanCompleted と同一形式の出典パス明示エラーを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let sourcePath = "/cfg/dev.json"
            let candProp = mkProposal "C:\\Videos\\retry_placeholder.mp4" (Some "前回エラー")
            let scanCand = mkScanCand candProp.OriginalFullPath
            let baseRetryModel = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "[API_KEY]" }
                    ApiKeySourcePath = Some sourcePath
                    SettingsSavePath = tempSavePath
                    Candidates = [ candProp ]
            }
            let baseScanModel = {
                baseRetryModel with
                    IsScanning = true
                    Candidates = []
            }

            let retryOut, retryCmd = State.updateWithContext None (RetryCandidateAiProposal candProp.OriginalFullPath) baseRetryModel
            let scanOut, _ = State.updateWithContext None (ScanCompleted (Ok [ scanCand ])) baseScanModel

            retryOut.IsRequestingAi |> should equal false
            retryOut.AiPipelineRunId |> should equal None
            retryOut.Candidates.Head.IsAiProcessing |> should equal false
            retryCmd.IsEmpty |> should equal true
            retryOut.ErrorMessage |> should not' (equal None)
            retryOut.ErrorMessage.Value |> should contain sourcePath
            retryOut.ErrorMessage |> should equal scanOut.ErrorMessage
        )

    /// <summary>ST-RETRY-03: RetryCandidateAiProposal の未設定検証が ScanCompleted と同一の統一ガードで動作することを検証する</summary>
    [<Fact>]
    let ``ST-RETRY-03: RetryCandidateAiProposal は API キー未設定時に ScanCompleted と同一形式の環境別保存先エラーを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let envSavePath = "C:\\repo\\src\\TagBasedVideoManager.Renamer\\appsettings.Development.json"
            let candProp = mkProposal "C:\\Videos\\retry_missing.mp4" (Some "前回エラー")
            let scanCand = mkScanCand candProp.OriginalFullPath
            let baseRetryModel = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = None }
                    ApiKeySourcePath = None
                    SettingsSavePath = envSavePath
                    Candidates = [ candProp ]
            }
            let baseScanModel = {
                baseRetryModel with
                    IsScanning = true
                    Candidates = []
            }

            let retryOut, retryCmd = State.updateWithContext None (RetryCandidateAiProposal candProp.OriginalFullPath) baseRetryModel
            let scanOut, _ = State.updateWithContext None (ScanCompleted (Ok [ scanCand ])) baseScanModel

            retryOut.IsRequestingAi |> should equal false
            retryOut.AiPipelineRunId |> should equal None
            retryOut.Candidates.Head.IsAiProcessing |> should equal false
            retryCmd.IsEmpty |> should equal true
            retryOut.ErrorMessage |> should not' (equal None)
            retryOut.ErrorMessage.Value |> should contain envSavePath
            retryOut.ErrorMessage |> should equal scanOut.ErrorMessage
        )

    /// <summary>ST-RETRY-04: RetryCandidateAiProposal で存在しないパスが指定された場合は状態不変であることを検証する</summary>
    [<Fact>]
    let ``ST-RETRY-04: RetryCandidateAiProposal は存在しないパス指定時に状態を一切変更しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let cand = mkProposal "C:\\Videos\\existing_04.mp4" None
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-04" }
                    Candidates = [ cand ]
            }

            let newModel, cmd = State.updateWithContext None (RetryCandidateAiProposal "C:\\not_found.mp4") model
            newModel |> should equal model
            cmd.IsEmpty |> should equal true
        )

    /// <summary>ST-RETRY-05: 古い runId のパイプライン通知無視・現行 runId 完了解除・CancelAiProposal 時の AiPipelineRunId=None を検証する</summary>
    [<Fact>]
    let ``ST-RETRY-05: 古い runId の非同期通知を無視し現行 runId 完了および CancelAiProposal で AiPipelineRunId が None に戻る`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let oldRunId = Guid.NewGuid()
            let newRunId = Guid.NewGuid()
            let cand = mkProposal "C:\\Videos\\race_target.mp4" None
            let cts = new System.Threading.CancellationTokenSource()
            try
                let activeModel = {
                    initialModel with
                        IsRequestingAi = true
                        AiPipelineRunId = Some newRunId
                        AiCancellationCts = Some cts
                        Candidates = [ cand ]
                }

                // 1. 古い oldRunId の3通知はいずれも無視されること
                let afterOldProc, _ = State.updateWithContext None (CandidateAiProcessing (oldRunId, cand.OriginalFullPath)) activeModel
                afterOldProc |> should equal activeModel

                let oldProposed = { cand with ProposedFileName = "old_should_ignore.mp4"; IsAiProposed = true }
                let afterOldProp, _ = State.updateWithContext None (CandidateAiProposed (oldRunId, oldProposed)) activeModel
                afterOldProp |> should equal activeModel

                let afterOldDone, _ = State.updateWithContext None (AllAiProposalsCompleted oldRunId) activeModel
                afterOldDone |> should equal activeModel
                afterOldDone.IsRequestingAi |> should equal true
                afterOldDone.AiPipelineRunId |> should equal (Some newRunId)

                // 2. 現行 newRunId の通知は反映・完了解除されること
                let afterNewProc, _ = State.updateWithContext None (CandidateAiProcessing (newRunId, cand.OriginalFullPath)) activeModel
                afterNewProc.Candidates.Head.IsAiProcessing |> should equal true

                let newProposed = { cand with ProposedFileName = "new_valid.mp4"; ProposedLength = 25; IsAiProposed = true }
                let afterNewProp, _ = State.updateWithContext None (CandidateAiProposed (newRunId, newProposed)) afterNewProc
                afterNewProp.Candidates.Head.ProposedFileName |> should equal "new_valid.mp4"
                afterNewProp.Candidates.Head.IsAiProposed |> should equal true
                afterNewProp.Candidates.Head.IsAiProcessing |> should equal false

                let afterNewDone, _ = State.updateWithContext None (AllAiProposalsCompleted newRunId) afterNewProp
                afterNewDone.IsRequestingAi |> should equal false
                afterNewDone.AiPipelineRunId |> should equal None
                afterNewDone.AiCancellationCts |> should equal None
            finally
                try cts.Dispose() with _ -> ()

            // 3. CancelAiProposal で AiPipelineRunId=None となり、その後の旧 newRunId 通知も無視されること
            let cancelRunId = Guid.NewGuid()
            let cancelCts = new System.Threading.CancellationTokenSource()
            try
                let runningModel = {
                    initialModel with
                        IsRequestingAi = true
                        AiPipelineRunId = Some cancelRunId
                        AiCancellationCts = Some cancelCts
                        Candidates = [ { cand with IsAiProcessing = true } ]
                }
                let cancelled, _ = State.updateWithContext None CancelAiProposal runningModel
                cancelCts.IsCancellationRequested |> should equal true
                cancelled.IsRequestingAi |> should equal false
                cancelled.AiPipelineRunId |> should equal None
                cancelled.AiCancellationCts |> should equal None
                cancelled.Candidates.Head.IsAiProcessing |> should equal false

                let afterCancelProc, _ = State.updateWithContext None (CandidateAiProcessing (cancelRunId, cand.OriginalFullPath)) cancelled
                afterCancelProc |> should equal cancelled

                let afterCancelDone, _ = State.updateWithContext None (AllAiProposalsCompleted cancelRunId) cancelled
                afterCancelDone |> should equal cancelled
            finally
                try cancelCts.Dispose() with _ -> ()
        )

    /// <summary>ST-RELOAD-01: ReloadSettings で設定を同期再読込し選択中 TargetDirectory を維持しつつ再起動なしで回復できることを検証する</summary>
    [<Fact>]
    let ``ST-RELOAD-01: ReloadSettings は同期再読込で ApiKey を更新し選択中の TargetDirectory を維持して完了通知を表示する`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let basePath = Path.Combine(renamerDir, "appsettings.json")
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(basePath, """{ "apiKey": null }""")
            File.WriteAllText(devPath, """{ "apiKey": "xxx" }""")

            let initModel, _ = State.initWithContext (Some ctx)
            let pickedModel = {
                initModel with
                    Settings = { initModel.Settings with TargetDirectory = "D:\\UserPicked" }
                    ErrorMessage = Some "⚠️ OpenRouter APIキーがプレースホルダー値 ('xxx') のため..."
            }

            // 最小 JSON（targetDirectory 記載なし）へ修正後に ReloadSettings
            File.WriteAllText(devPath, """{ "apiKey": "sk-or-v1-valid0000" }""")
            let reloaded, cmd = State.updateWithContext (Some ctx) ReloadSettings pickedModel

            reloaded.Settings.ApiKey |> should equal (Some "sk-or-v1-valid0000")
            reloaded.ApiKeySourcePath |> should equal (Some devPath)
            reloaded.SettingsSavePath |> should equal devPath
            reloaded.Settings.TargetDirectory |> should equal "D:\\UserPicked"
            reloaded.ErrorMessage |> should equal (Some "💡 設定ファイルを再読込しました (appsettings.Development.json)")
            cmd.IsEmpty |> should equal true

            // ファイル側で明示的に非空 targetDirectory / selectedModel / pathLengthThreshold が指定された場合は上書き採用されること
            File.WriteAllText(
                devPath,
                """{
                  "apiKey": "sk-or-v1-valid0000",
                  "targetDirectory": "E:\\ConfiguredDir",
                  "selectedModel": "reloaded/model-2",
                  "pathLengthThreshold": 195
                }"""
            )
            let reloadedWithDir, _ = State.updateWithContext (Some ctx) ReloadSettings reloaded
            reloadedWithDir.Settings.TargetDirectory |> should equal "E:\\ConfiguredDir"
            reloadedWithDir.Settings.SelectedModel |> should equal "reloaded/model-2"
            reloadedWithDir.Settings.PathLengthThreshold |> should equal 195
            reloadedWithDir.CurrentThreshold |> should equal 195

            // Warnings 非空時（構文エラー / 旧 { "value": ... } 形式）は "💡" で上書きされず警告が維持されること
            File.WriteAllText(devPath, """{ "apiKey": "sk-or-v1-broken" """)
            let reloadedSyntaxWarn, _ = State.updateWithContext (Some ctx) ReloadSettings reloadedWithDir
            reloadedSyntaxWarn.ErrorMessage |> should not' (equal None)
            Assert.False(reloadedSyntaxWarn.ErrorMessage.Value.StartsWith("💡"), "Warnings 非空時に正常再読込バナーで上書きされてはいけません。")
            reloadedSyntaxWarn.ErrorMessage.Value |> should contain devPath
            reloadedSyntaxWarn.ErrorMessage.Value |> should contain "構文解析に失敗しました"

            File.WriteAllText(devPath, """{ "apiKey": { "value": "sk-or-v1-legacy-reload" } }""")
            let reloadedLegacyWarn, _ = State.updateWithContext (Some ctx) ReloadSettings reloadedWithDir
            reloadedLegacyWarn.ErrorMessage |> should not' (equal None)
            Assert.False(reloadedLegacyWarn.ErrorMessage.Value.StartsWith("💡"), "旧形式警告時に正常再読込バナーで上書きされてはいけません。")
            reloadedLegacyWarn.ErrorMessage.Value |> should contain devPath
            reloadedLegacyWarn.ErrorMessage.Value |> should contain "文字列形式"
        )

    /// <summary>ST-RELOAD-02: IsScanning または IsRenaming 中は ReloadSettings を排他無視することを検証する</summary>
    [<Fact>]
    let ``ST-RELOAD-02: ReloadSettings は IsScanning または IsRenaming 中は排他ガードにより無視される`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(devPath, """{ "apiKey": "sk-or-v1-reload-busy" }""")
            let initialModel = initIsolatedModel devPath

            for busyModel in [ { initialModel with IsScanning = true }; { initialModel with IsRenaming = true } ] do
                let updated, cmd = State.updateWithContext (Some ctx) ReloadSettings busyModel
                updated |> should equal busyModel
                cmd.IsEmpty |> should equal true
        )

    /// <summary>ST-RELOAD-03: ReloadSettings 時の SelectedRuleId フォールバックおよび維持を検証する</summary>
    [<Fact>]
    let ``ST-RELOAD-03: ReloadSettings は削除された SelectedRuleId を先頭ルールへ安全フォールバックし残存時は維持する`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(
                devPath,
                """{
                  "rules": [
                    { "id": "new-first", "name": "新先頭", "pattern": "{Title}.mp4", "promptInstruction": "指示1", "order": 0, "enableWebSearch": false },
                    { "id": "kept-second", "name": "残存2番目", "pattern": "{Code}.mp4", "promptInstruction": "指示2", "order": 1, "enableWebSearch": true }
                  ]
                }"""
            )
            let initialModel = initIsolatedModel devPath

            let deletedSelModel = { initialModel with SelectedRuleId = "deleted-rule" }
            let afterDeleted, _ = State.updateWithContext (Some ctx) ReloadSettings deletedSelModel
            afterDeleted.SelectedRuleId |> should equal "new-first"

            let keptSelModel = { initialModel with SelectedRuleId = "kept-second" }
            let afterKept, _ = State.updateWithContext (Some ctx) ReloadSettings keptSelModel
            afterKept.SelectedRuleId |> should equal "kept-second"
        )

    /// <summary>ST-MODEL-01: ModelSelected 正常時に selectedModel のみ Patch Save され他キーが保持されることを検証する</summary>
    [<Fact>]
    let ``ST-MODEL-01: ModelSelected は selectedModel のみを Patch Save し既存 apiKey や未知キーを保持する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            File.WriteAllText(
                tempSavePath,
                """{
                  "apiKey": "sk-or-v1-keep-secret-01",
                  "targetDirectory": "D:\\KeepVideos",
                  "selectedModel": "old/model",
                  "customUnknownKey": "preserve-value"
                }"""
            )
            let initialModel = initIsolatedModel tempSavePath
            let model = {
                initialModel with
                    Settings = { initialModel.Settings with SelectedModel = "old/model" }
                    ErrorMessage = Some "前回エラー"
            }

            let updated, _ = State.updateWithContext None (ModelSelected "google/gemini-2.0-flash") model
            updated.Settings.SelectedModel |> should equal "google/gemini-2.0-flash"
            updated.ErrorMessage |> should equal None

            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(tempSavePath))
            let root = doc.RootElement
            root.GetProperty("selectedModel").GetString() |> should equal "google/gemini-2.0-flash"
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-keep-secret-01"
            root.GetProperty("targetDirectory").GetString() |> should equal "D:\\KeepVideos"
            root.GetProperty("customUnknownKey").GetString() |> should equal "preserve-value"
        )

    /// <summary>ST-MODEL-02: ModelSelected 保存失敗時に SelectedModel がロールバックされ詳細エラー通知されることを検証する</summary>
    [<Fact>]
    let ``ST-MODEL-02: ModelSelected は ReadOnly または破損 JSON 時に SelectedModel をロールバックし段階・パス付きエラーを通知する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath

            // 1. ReadOnly 失敗ロールバック
            File.WriteAllText(tempSavePath, """{ "apiKey": "sk-or-v1-ro", "selectedModel": "model/original" }""")
            File.SetAttributes(tempSavePath, FileAttributes.ReadOnly)
            try
                let mRo = { initialModel with Settings = { initialModel.Settings with SelectedModel = "model/original" } }
                let failedRo, _ = State.updateWithContext None (ModelSelected "google/gemini-2.0-flash") mRo
                failedRo.Settings.SelectedModel |> should equal "model/original"
                failedRo.ErrorMessage |> should not' (equal None)
                failedRo.ErrorMessage.Value |> should contain tempSavePath
                failedRo.ErrorMessage.Value |> should contain "AtomicMove"
            finally
                try File.SetAttributes(tempSavePath, FileAttributes.Normal) with _ -> ()

            // 2. 破損 JSON 失敗ロールバック（元バイト列不変）
            let brokenRaw = """{ "selectedModel": "broken"""
            File.WriteAllText(tempSavePath, brokenRaw)
            let mBroken = { initialModel with Settings = { initialModel.Settings with SelectedModel = "model/original" } }
            let failedBroken, _ = State.updateWithContext None (ModelSelected "google/gemini-2.0-flash") mBroken
            failedBroken.Settings.SelectedModel |> should equal "model/original"
            failedBroken.ErrorMessage |> should not' (equal None)
            failedBroken.ErrorMessage.Value |> should contain tempSavePath
            failedBroken.ErrorMessage.Value |> should contain "ParseJson"
            File.ReadAllText(tempSavePath) |> should equal brokenRaw
        )

    /// <summary>ST-MODEL-03: IsScanning / IsRenaming 中の ModelSelected が排他拒否されファイル書込しないことを検証する</summary>
    [<Fact>]
    let ``ST-MODEL-03: ModelSelected は IsScanning または IsRenaming 中に排他拒否されファイルへ書き込まない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            for busyModel in [ { initialModel with IsScanning = true }; { initialModel with IsRenaming = true } ] do
                let updated, cmd = State.updateWithContext None (ModelSelected "other-model") busyModel
                updated |> should equal busyModel
                cmd.IsEmpty |> should equal true
                File.Exists(tempSavePath) |> should equal false
        )

    /// <summary>ST-RULE-01: ルール変更4メッセージで rules のみ Patch Save され他キー保持＆先頭ルールが起動時既定になることを検証する</summary>
    [<Fact>]
    let ``ST-RULE-01: SaveEditingRule / SaveRule / DeleteRule / MoveRuleOrder は rules のみ Patch Save し先頭ルールが次回起動既定になる`` () =
        withIsolatedRepoContext "Development" (fun _ renamerDir ctx ->
            let devPath = Path.Combine(renamerDir, "appsettings.Development.json")
            File.WriteAllText(
                devPath,
                """{
                  "apiKey": "sk-or-v1-rule-keep",
                  "targetDirectory": "D:\\RuleVideos",
                  "customNote": "keep-this-unknown"
                }"""
            )
            let r1 = mkTestRule "r1" "ルール1" 0
            let r2 = mkTestRule "r2" "ルール2" 1
            let initialModel = initIsolatedModel devPath
            let baseModel = {
                initialModel with
                    Settings = { initialModel.Settings with Rules = [ r1; r2 ] }
                    SelectedRuleId = "r1"
            }

            // 1. SaveEditingRule（新規 r3 追加）
            let editingR3 = mkTestRule "r3" "ルール3" 2
            let mAfterEditSave, _ = State.updateWithContext (Some ctx) SaveEditingRule { baseModel with EditingRule = Some editingR3 }
            mAfterEditSave.ErrorMessage |> should equal None
            mAfterEditSave.Settings.Rules |> List.map (fun r -> r.Id) |> should equal [ "r1"; "r2"; "r3" ]

            // 2. SaveRule（r2 名称変更）
            let mAfterSaveRule, _ = State.updateWithContext (Some ctx) (SaveRule { r2 with Name = "ルール2改" }) mAfterEditSave
            mAfterSaveRule.ErrorMessage |> should equal None
            (mAfterSaveRule.Settings.Rules |> List.find (fun r -> r.Id = "r2")).Name |> should equal "ルール2改"

            // 3. DeleteRule（r1 削除 -> r2, r3）
            let mAfterDelete, _ = State.updateWithContext (Some ctx) (DeleteRule "r1") mAfterSaveRule
            mAfterDelete.ErrorMessage |> should equal None
            mAfterDelete.Settings.Rules |> List.map (fun r -> r.Id) |> should equal [ "r2"; "r3" ]

            // 4. MoveRuleOrder（r3 を先頭へ移動 -> r3, r2）
            let mAfterMove, _ = State.updateWithContext (Some ctx) (MoveRuleOrder ("r3", -1)) mAfterDelete
            mAfterMove.ErrorMessage |> should equal None
            mAfterMove.Settings.Rules |> List.map (fun r -> r.Id) |> should equal [ "r3"; "r2" ]

            // Patch Save 非破壊確認（apiKey, targetDirectory, customNote 保持）
            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(devPath))
            let root = doc.RootElement
            root.GetProperty("apiKey").GetString() |> should equal "sk-or-v1-rule-keep"
            root.GetProperty("targetDirectory").GetString() |> should equal "D:\\RuleVideos"
            root.GetProperty("customNote").GetString() |> should equal "keep-this-unknown"

            // 再起動相当で先頭ルール r3 が既定選択されること
            let rebooted, _ = State.initWithContext (Some ctx)
            rebooted.Settings.Rules.Head.Id |> should equal "r3"
            rebooted.SelectedRuleId |> should equal "r3"
        )

    /// <summary>ST-RULE-02: SaveEditingRule でルール名が空白のみの場合は保存せずエラー通知することを検証する</summary>
    [<Fact>]
    let ``ST-RULE-02: SaveEditingRule はルール名が空白のみの場合に保存せずバリデーションエラーを設定する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let blankRule = { mkTestRule "r-blank" "   " 0 with PromptInstruction = "指示" }
            let model = { initialModel with EditingRule = Some blankRule }

            let updated, cmd = State.updateWithContext None SaveEditingRule model
            updated.ErrorMessage |> should equal (Some "ルール名を入力してください。")
            updated.Settings.Rules |> should equal initialModel.Settings.Rules
            File.Exists(tempSavePath) |> should equal false
            cmd.IsEmpty |> should equal true
        )

    /// <summary>ST-RULE-03: ルール残り 1 件時の RequestDeleteRule が削除拒否エラーを返すことを検証する</summary>
    [<Fact>]
    let ``ST-RULE-03: RequestDeleteRule はルール残り 1 件時に削除拒否エラーを設定し ConfirmDialog を開かない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let singleRule = mkTestRule "only-one" "唯一ルール" 0
            let model = { initialModel with Settings = { initialModel.Settings with Rules = [ singleRule ] } }

            let updated, cmd = State.updateWithContext None (RequestDeleteRule singleRule.Id) model
            updated.ErrorMessage |> should equal (Some "最後の1件の命名規則は削除できません。")
            updated.ConfirmDialog |> should equal None
            cmd.IsEmpty |> should equal true
        )

    /// <summary>ST-RULE-04: MoveRuleOrder の範囲外移動で状態不変・保存不発であることを検証する</summary>
    [<Fact>]
    let ``ST-RULE-04: MoveRuleOrder は先頭の上移動および末尾の下移動で状態不変かつ保存を発火しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let r1 = mkTestRule "r1" "先頭" 0
            let r2 = mkTestRule "r2" "末尾" 1
            let model = { initialModel with Settings = { initialModel.Settings with Rules = [ r1; r2 ] } }

            let mTop, cmdTop = State.updateWithContext None (MoveRuleOrder ("r1", -1)) model
            mTop |> should equal model
            cmdTop.IsEmpty |> should equal true

            let mBottom, cmdBottom = State.updateWithContext None (MoveRuleOrder ("r2", +1)) model
            mBottom |> should equal model
            cmdBottom.IsEmpty |> should equal true
            File.Exists(tempSavePath) |> should equal false
        )

    /// <summary>ST-RULE-05: ルール保存失敗時に Rules / SelectedRuleId がロールバックされ DeleteRule 時 ConfirmDialog=None・EditingRule 保持となることを検証する</summary>
    [<Fact>]
    let ``ST-RULE-05: ルール保存失敗時は Rules と SelectedRuleId をロールバックし DeleteRule 時は ConfirmDialog を閉じ EditingRule 入力を保持する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let r1 = mkTestRule "r1" "初期1" 0
            let r2 = mkTestRule "r2" "初期2" 1
            let editedInput = { mkTestRule "r-new" "編集中入力名" 2 with PromptInstruction = "消えてはいけないプロンプト" }
            let confirmDialog: DialogConfig = {
                Title = "削除確認"
                Message = "削除しますか？"
                ConfirmText = "削除する"
                CancelText = "キャンセル"
                OnConfirm = DeleteRule "r2"
            }

            File.WriteAllText(tempSavePath, """{ "apiKey": "sk-or-v1-ro-rule" }""")
            File.SetAttributes(tempSavePath, FileAttributes.ReadOnly)
            try
                let modalModel = {
                    initialModel with
                        Settings = { initialModel.Settings with Rules = [ r1; r2 ] }
                        SelectedRuleId = "r1"
                        IsRuleManagerOpen = true
                        EditingRule = Some editedInput
                        ConfirmDialog = Some confirmDialog
                }

                // 1. SaveEditingRule 失敗 -> Rules/SelectedRuleId ロールバック, EditingRule 保持
                let afterEditFail, _ = State.updateWithContext None SaveEditingRule modalModel
                afterEditFail.Settings.Rules |> should equal [ r1; r2 ]
                afterEditFail.SelectedRuleId |> should equal "r1"
                afterEditFail.EditingRule |> should equal (Some editedInput)
                afterEditFail.ErrorMessage |> should not' (equal None)
                afterEditFail.ErrorMessage.Value |> should contain tempSavePath
                afterEditFail.ErrorMessage.Value |> should contain "AtomicMove"

                // 2. DeleteRule 失敗 -> Rules ロールバック, ConfirmDialog=None 閉鎖, EditingRule 保持（削除対象 r2 を編集中でも消さない）
                let editingSameTargetR2 = { r2 with Name = "削除失敗時も保持されるべき r2 編集中入力" }
                let modalModelEditingTarget = { modalModel with EditingRule = Some editingSameTargetR2 }
                let afterDelFail, _ = State.updateWithContext None (DeleteRule "r2") modalModelEditingTarget
                afterDelFail.Settings.Rules |> should equal [ r1; r2 ]
                afterDelFail.SelectedRuleId |> should equal "r1"
                afterDelFail.ConfirmDialog |> should equal None
                afterDelFail.EditingRule |> should equal (Some editingSameTargetR2)
                afterDelFail.ErrorMessage |> should not' (equal None)
                afterDelFail.ErrorMessage.Value |> should contain tempSavePath

                // 3. MoveRuleOrder 失敗 -> Rules ロールバック
                let afterMoveFail, _ = State.updateWithContext None (MoveRuleOrder ("r2", -1)) modalModel
                afterMoveFail.Settings.Rules |> should equal [ r1; r2 ]
                afterMoveFail.ErrorMessage |> should not' (equal None)
                afterMoveFail.ErrorMessage.Value |> should contain tempSavePath
            finally
                try File.SetAttributes(tempSavePath, FileAttributes.Normal) with _ -> ()

            // 4. 破損 JSON 時の SaveRule 失敗 -> ロールバック + ParseJson エラー通知 + ファイル不変
            let brokenJsonRaw = "{ broken-rules-json"
            File.WriteAllText(tempSavePath, brokenJsonRaw)
            let modalBroken = {
                initialModel with
                    Settings = { initialModel.Settings with Rules = [ r1; r2 ] }
                    SelectedRuleId = "r1"
                    IsRuleManagerOpen = true
                    EditingRule = Some editedInput
            }
            let afterSaveRuleFail, _ = State.updateWithContext None (SaveRule (mkTestRule "r3" "新規" 2)) modalBroken
            afterSaveRuleFail.Settings.Rules |> should equal [ r1; r2 ]
            afterSaveRuleFail.SelectedRuleId |> should equal "r1"
            afterSaveRuleFail.EditingRule |> should equal (Some editedInput)
            afterSaveRuleFail.ErrorMessage |> should not' (equal None)
            afterSaveRuleFail.ErrorMessage.Value |> should contain tempSavePath
            afterSaveRuleFail.ErrorMessage.Value |> should contain "ParseJson"
            File.ReadAllText(tempSavePath) |> should equal brokenJsonRaw
        )

    // =========================================================================
    // Task 4.1: Diff C1 >= 85% & Adversarial QA 回帰テスト
    // =========================================================================

    /// <summary>QA-ST-01: ModelSelected ガード（null・空白・同値モデル指定時に ReadOnly 保存先でも savePatch が発火せず状態不変）を検証する</summary>
    [<Fact>]
    let ``QA-ST-01: ModelSelected は null・空白・現在と同値モデル指定時に保存を発火せず ErrorMessage も設定しない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            File.WriteAllText(tempSavePath, """{ "apiKey": "sk-or-v1-guard-ro", "selectedModel": "current/model" }""")
            File.SetAttributes(tempSavePath, FileAttributes.ReadOnly)
            try
                let initialModel = initIsolatedModel tempSavePath
                let model = {
                    initialModel with
                        Settings = { initialModel.Settings with SelectedModel = "current/model" }
                        ErrorMessage = None
                }

                for invalidOrSame in [ null; ""; "   "; "current/model" ] do
                    let updated, cmd = State.updateWithContext None (ModelSelected invalidOrSame) model
                    updated |> should equal model
                    updated.ErrorMessage |> should equal None
                    cmd.IsEmpty |> should equal true
            finally
                try File.SetAttributes(tempSavePath, FileAttributes.Normal) with _ -> ()
        )

    /// <summary>QA-ST-02: RetryCandidateAiProposal のスキャン/リネーム中排他と、他候補 IsAiProcessing リセット・先行 CTS 破棄を検証する</summary>
    [<Fact>]
    let ``QA-ST-02: RetryCandidateAiProposal はビジー中無視され、実行時はキー失敗で全候補false・有効キーで他候補falseかつ先行CTS破棄する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let targetCand = mkProposal "C:\\Videos\\retry_target.mp4" (Some "前回エラー")
            let otherBusyCand = { mkProposal "C:\\Videos\\other_busy.mp4" None with IsAiProcessing = true }

            // 1. IsScanning = true および IsRenaming = true 中の排他無視
            for busyModel in [
                { initialModel with IsScanning = true; IsRequestingAi = false; Candidates = [ targetCand ] }
                { initialModel with IsRenaming = true; IsRequestingAi = false; Candidates = [ targetCand ] }
            ] do
                let ignored, cmd = State.updateWithContext None (RetryCandidateAiProposal targetCand.OriginalFullPath) busyModel
                ignored |> should equal busyModel
                ignored.IsRequestingAi |> should equal false
                cmd.IsEmpty |> should equal true

            // 2. 先行他候補が IsAiProcessing = true かつキー検証失敗時 -> 全候補 IsAiProcessing = false + resetAiPipelineState
            let failOldCts = new System.Threading.CancellationTokenSource()
            let failOldRunId = Guid.NewGuid()
            try
                let failModel = {
                    initialModel with
                        Settings = { initialModel.Settings with ApiKey = None }
                        ApiKeySourcePath = None
                        IsRequestingAi = true
                        AiPipelineRunId = Some failOldRunId
                        AiCancellationCts = Some failOldCts
                        Candidates = [ targetCand; otherBusyCand ]
                }
                let afterFail, failCmd = State.updateWithContext None (RetryCandidateAiProposal targetCand.OriginalFullPath) failModel
                failOldCts.IsCancellationRequested |> should equal true
                Assert.Throws<ObjectDisposedException>(fun () -> ignore failOldCts.Token) |> ignore
                afterFail.IsRequestingAi |> should equal false
                afterFail.AiPipelineRunId |> should equal None
                afterFail.AiCancellationCts |> should equal None
                afterFail.Candidates |> List.forall (fun c -> not c.IsAiProcessing) |> should equal true
                afterFail.ErrorMessage |> should not' (equal None)
                failCmd.IsEmpty |> should equal true
            finally
                try failOldCts.Dispose() with _ -> ()

            // 3. 先行他候補が IsAiProcessing = true かつ有効キー時 -> 対象候補のみ true・他候補 false・先行 CTS キャンセル破棄
            let okOldCts = new System.Threading.CancellationTokenSource()
            let okOldRunId = Guid.NewGuid()
            let mutable newCtsOpt: System.Threading.CancellationTokenSource option = None
            try
                let okModel = {
                    initialModel with
                        Settings = { initialModel.Settings with ApiKey = Some "sk-or-v1-valid-retry-key" }
                        IsRequestingAi = true
                        AiPipelineRunId = Some okOldRunId
                        AiCancellationCts = Some okOldCts
                        Candidates = [ targetCand; otherBusyCand ]
                }
                let afterOk, _ = State.updateWithContext None (RetryCandidateAiProposal targetCand.OriginalFullPath) okModel
                newCtsOpt <- afterOk.AiCancellationCts

                okOldCts.IsCancellationRequested |> should equal true
                Assert.Throws<ObjectDisposedException>(fun () -> ignore okOldCts.Token) |> ignore
                afterOk.IsRequestingAi |> should equal true
                afterOk.AiPipelineRunId.IsSome |> should equal true
                afterOk.AiPipelineRunId.Value |> should not' (equal okOldRunId)
                afterOk.AiCancellationCts.IsSome |> should equal true

                let updatedTarget = afterOk.Candidates |> List.find (fun c -> c.OriginalFullPath = targetCand.OriginalFullPath)
                let updatedOther = afterOk.Candidates |> List.find (fun c -> c.OriginalFullPath = otherBusyCand.OriginalFullPath)
                updatedTarget.IsAiProcessing |> should equal true
                updatedTarget.AiError |> should equal None
                updatedOther.IsAiProcessing |> should equal false
            finally
                try okOldCts.Dispose() with _ -> ()
                newCtsOpt |> Option.iter (fun c -> try c.Dispose() with _ -> ())
        )

    /// <summary>QA-ST-03: ルール CRUD の境界バリデーション（DeleteRule 残り1件拒否・SaveEditingRule 欠損・SaveRule 4項目欠損）を検証する</summary>
    [<Fact>]
    let ``QA-ST-03: DeleteRule・SaveEditingRule・SaveRule の各境界バリデーションでエラー設定されファイル保存されない`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath
            let singleRule = mkTestRule "only-rule" "唯一ルール" 0
            let dummyDialog: DialogConfig = {
                Title = "削除確認"
                Message = "削除しますか？"
                ConfirmText = "削除する"
                CancelText = "キャンセル"
                OnConfirm = DeleteRule singleRule.Id
            }

            // 1. DeleteRule 直接呼び出し（Rules.Length <= 1）
            let singleModel = {
                initialModel with
                    Settings = { initialModel.Settings with Rules = [ singleRule ] }
                    ConfirmDialog = Some dummyDialog
            }
            let afterDirectDelete, delCmd = State.updateWithContext None (DeleteRule singleRule.Id) singleModel
            afterDirectDelete.ErrorMessage |> should equal (Some "最後の1件の命名規則は削除できません。")
            afterDirectDelete.ConfirmDialog |> should equal None
            afterDirectDelete.Settings.Rules |> should equal [ singleRule ]
            delCmd.IsEmpty |> should equal true

            // 2. SaveEditingRule: EditingRule = None および Id = null / "   "
            let mEditNone, cmdNone = State.updateWithContext None SaveEditingRule { initialModel with EditingRule = None }
            mEditNone.ErrorMessage |> should equal (Some "編集中の命名規則がありません。")
            cmdNone.IsEmpty |> should equal true

            for badId in [ null; "   " ] do
                let badIdRule = { singleRule with Id = badId }
                let mBadId, cmdBadId = State.updateWithContext None SaveEditingRule { initialModel with EditingRule = Some badIdRule }
                mBadId.ErrorMessage |> should equal (Some "ルールIDが不正です。")
                cmdBadId.IsEmpty |> should equal true

            // 3. SaveRule: Id・Name・Pattern・PromptInstruction のそれぞれが null / 空白の全パターン
            let invalidSaveRules: NamingRule list = [
                { singleRule with Id = null }
                { singleRule with Id = "   " }
                { singleRule with Name = null }
                { singleRule with Name = "   " }
                { singleRule with Pattern = null }
                { singleRule with Pattern = "   " }
                { singleRule with PromptInstruction = null }
                { singleRule with PromptInstruction = "   " }
            ]
            for invalidRule in invalidSaveRules do
                let mBadSave, cmdBadSave = State.updateWithContext None (SaveRule invalidRule) singleModel
                mBadSave.ErrorMessage |> should equal (Some "命名規則の必須項目（ID・ルール名・パターン・プロンプト指示）を入力してください。")
                mBadSave.Settings.Rules |> should equal [ singleRule ]
                cmdBadSave.IsEmpty |> should equal true

            File.Exists(tempSavePath) |> should equal false
        )

    /// <summary>QA-ST-04: CandidateAiProposed 整合・既破棄 CTS への CancelAiProposal 保護・SettingsSavePath="" フォールバック経路を検証する</summary>
    [<Fact>]
    let ``QA-ST-04: CandidateAiProposed の失敗整合・既破棄 CTS 保護・SettingsSavePath 空時フォールバックが安全動作する`` () =
        withIsolatedStateTest (fun tempSavePath ->
            let initialModel = initIsolatedModel tempSavePath

            // 1. CandidateAiProposed: IsAiProposed = false & AiError = Some "失敗" で IsAiProposed が true に化けないこと
            let activeRunId = Guid.NewGuid()
            let baseCand = { mkProposal "C:\\Videos\\proposed_check.mp4" None with IsAiProcessing = true; IsAiProposed = false }
            let modelWithRun = {
                initialModel with
                    AiPipelineRunId = Some activeRunId
                    Candidates = [ baseCand ]
            }
            let failedProposal = {
                baseCand with
                    IsAiProposed = false
                    IsAiProcessing = true
                    AiError = Some "失敗"
            }
            let afterFailedProp, _ = State.updateWithContext None (CandidateAiProposed (activeRunId, failedProposal)) modelWithRun
            afterFailedProp.Candidates.Head.IsAiProcessing |> should equal false
            afterFailedProp.Candidates.Head.IsAiProposed |> should equal false
            afterFailedProp.Candidates.Head.AiError |> should equal (Some "失敗")

            // AiError = Some なのに IsAiProposed = true で来た矛盾データも false に補正されること
            let inconsistentProposal = { failedProposal with IsAiProposed = true; AiError = Some "矛盾エラー" }
            let afterInconsistent, _ = State.updateWithContext None (CandidateAiProposed (activeRunId, inconsistentProposal)) modelWithRun
            afterInconsistent.Candidates.Head.IsAiProposed |> should equal false

            // 2. 既に Dispose() 済みの CTS を持つモデルへの CancelAiProposal で例外が発生せず状態リセットされること
            let disposedCts = new System.Threading.CancellationTokenSource()
            disposedCts.Dispose()
            let modelWithDisposedCts = {
                initialModel with
                    IsRequestingAi = true
                    AiPipelineRunId = Some activeRunId
                    AiCancellationCts = Some disposedCts
                    Candidates = [ baseCand ]
            }
            let afterCancel, _ = State.updateWithContext None CancelAiProposal modelWithDisposedCts
            afterCancel.IsRequestingAi |> should equal false
            afterCancel.AiPipelineRunId |> should equal None
            afterCancel.AiCancellationCts |> should equal None
            afterCancel.Candidates.Head.IsAiProcessing |> should equal false
            afterCancel.Candidates.Head.AiComment |> should equal (Some "（AI提案が中止されました）")

            // 3. SettingsSavePath = "" 状態での validateApiKeyForAi と saveSettingsPatchWithFeedback フォールバック経路
            let tempRoot = Path.Combine(Path.GetTempPath(), "RenamerFallbackSavePath_" + Guid.NewGuid().ToString("N"))
            let repoDir = Path.Combine(tempRoot, "repo")
            let renamerDir = Path.Combine(repoDir, "src", "TagBasedVideoManager.Renamer")
            Directory.CreateDirectory(renamerDir) |> ignore
            File.WriteAllText(Path.Combine(renamerDir, "appsettings.json"), """{ "apiKey": null }""")

            let origCwd = Directory.GetCurrentDirectory()
            let origDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            let origAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            let fallbackEnv = "IsolatedFallback_" + Guid.NewGuid().ToString("N")
            let expectedEnvPath = Path.Combine(renamerDir, $"appsettings.{fallbackEnv}.json")
            try
                Directory.SetCurrentDirectory(repoDir)
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", fallbackEnv)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null)

                // 3a. validateApiKeyForAi フォールバック経路（ScanCompleted）
                let scanCand = mkScanCand "C:\\Videos\\fallback_path_target.mp4"
                let missingPathModel = {
                    initialModel with
                        Settings = { initialModel.Settings with ApiKey = None }
                        ApiKeySourcePath = None
                        SettingsSavePath = ""
                        IsScanning = true
                }
                let afterScanFallback, _ = State.updateWithContext None (ScanCompleted (Ok [ scanCand ])) missingPathModel
                afterScanFallback.IsRequestingAi |> should equal false
                afterScanFallback.ErrorMessage |> should not' (equal None)
                afterScanFallback.ErrorMessage.Value |> should contain expectedEnvPath

                // 3b. saveSettingsPatchWithFeedback フォールバック経路（ModelSelected）
                let patchModel = {
                    initialModel with
                        Settings = { initialModel.Settings with SelectedModel = "old/model" }
                        SettingsSavePath = ""
                }
                let afterModelFallback, _ = State.updateWithContext None (ModelSelected "fallback/patched-model") patchModel
                afterModelFallback.ErrorMessage |> should equal None
                afterModelFallback.Settings.SelectedModel |> should equal "fallback/patched-model"
                afterModelFallback.SettingsSavePath |> should equal expectedEnvPath
                File.Exists(expectedEnvPath) |> should equal true
            finally
                Directory.SetCurrentDirectory(origCwd)
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", origDotnet)
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", origAspnet)
                if Directory.Exists(tempRoot) then Directory.Delete(tempRoot, true)
        )

