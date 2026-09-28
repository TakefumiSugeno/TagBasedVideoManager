namespace TagBasedVideoManager.Renamer.Tests

open System
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module StateTests =

    [<Fact>]
    let ``init は初期設定（閾値240文字・Verticalレイアウト・空候補）を生成する`` () =
        let model, _cmd = State.init ()
        model.CurrentThreshold |> should equal 240
        model.Layout |> should equal Vertical
        model.Candidates |> should be Empty
        model.UndoStack |> should be Empty
        model.IsScanning |> should equal false
        model.IsRequestingAi |> should equal false
        model.IsRenaming |> should equal false
        model.ConfirmDialog |> should equal None

    [<Fact>]
    let ``ThresholdChanged はセッション内閾値を更新し、元の設定構造体は変更しない`` () =
        let initialModel, _ = State.init ()
        let newModel, _ = State.update (ThresholdChanged 200) initialModel

        newModel.CurrentThreshold |> should equal 200
        newModel.Settings.PathLengthThreshold |> should equal 240 // 設定本体は不変

    [<Fact>]
    let ``UpdateProposedName はインライン編集時に ProposedLength を即座に再計算する`` () =
        let initialModel, _ = State.init ()
        let sampleProposal: RenameProposal = {
            OriginalFullPath = "C:\\Videos\\VeryLongPath.mp4"
            OriginalFileName = "VeryLongPath.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 26
            ProposedFileName = "Short.mp4"
            ProposedLength = 19
            AiComment = None
            IsAiProposed = true
            IsSelected = true
            LastWriteTime = System.DateTime.UtcNow
        }
        let modelWithCandidate = { initialModel with Candidates = [ sampleProposal ] }

        let newProposedName = "EvenShorter.mp4"
        let updatedModel, _ = State.update (UpdateProposedName (sampleProposal.OriginalFullPath, newProposedName)) modelWithCandidate

        updatedModel.Candidates.Length |> should equal 1
        let updated = updatedModel.Candidates.Head
        updated.ProposedFileName |> should equal "EvenShorter.mp4"
        updated.ProposedLength |> should equal ("C:\\Videos\\" + newProposedName).Length

    [<Fact>]
    let ``ToggleCandidateSelect および SelectAllCandidates で選択状態を切り替えられる`` () =
        let initialModel, _ = State.init ()
        let p1 = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 15
            ProposedFileName = "n1.mp4"
            ProposedLength = 15
            AiComment = None
            IsAiProposed = true
            IsSelected = true
            LastWriteTime = System.DateTime.UtcNow
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

    [<Fact>]
    let ``SetCandidateSelect は指定パスの選択状態を明示的に更新し、冪等に動作する`` () =
        let initialModel, _ = State.init ()
        let p1 = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 15
            ProposedFileName = "n1.mp4"
            ProposedLength = 15
            AiComment = None
            IsAiProposed = true
            IsSelected = false
            LastWriteTime = System.DateTime.UtcNow
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

    [<Fact>]
    let ``RequestUndo は UndoStack に履歴がある場合、コンテナ再起動警告を含むダイアログを設定する`` () =
        let initialModel, _ = State.init ()
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

    [<Fact>]
    let ``SetLayoutMode は Vertical と Horizontal を正しく切り替える`` () =
        let initialModel, _ = State.init ()
        let horizontalModel, _ = State.update (SetLayoutMode Horizontal) initialModel
        horizontalModel.Layout |> should equal Horizontal

        let verticalModel, _ = State.update (SetLayoutMode Vertical) horizontalModel
        verticalModel.Layout |> should equal Vertical

    [<Fact>]
    let ``ChangeSortCriterion は SortCriterion を更新し候補を即座に並び替える`` () =
        let initialModel, _ = State.init ()
        let pShort = {
            OriginalFullPath = "C:\\Videos\\Short.mp4"
            OriginalFileName = "Short.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 20
            ProposedFileName = "Short.mp4"
            ProposedLength = 20
            AiComment = None
            IsAiProposed = true
            IsSelected = true
            LastWriteTime = System.DateTime.UtcNow
        }
        let pLong = {
            OriginalFullPath = "C:\\Videos\\VeryLongLongName.mp4"
            OriginalFileName = "VeryLongLongName.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 35
            ProposedFileName = "VeryLongLongName.mp4"
            ProposedLength = 35
            AiComment = None
            IsAiProposed = true
            IsSelected = true
            LastWriteTime = System.DateTime.UtcNow
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

    [<Fact>]
    let ``TargetDirectoryChanged は走査対象ディレクトリパスを即座に更新する`` () =
        let initialModel, _ = State.init ()
        let newDir = "D:\\Selected\\Videos\\Folder"
        let updatedModel, _ = State.update (TargetDirectoryChanged newDir) initialModel
        updatedModel.Settings.TargetDirectory |> should equal newDir

    [<Fact>]
    let ``OpenRuleManager と CloseRuleManager はモーダル開閉状態 IsRuleManagerOpen を正しく遷移させる`` () =
        let initialModel, _ = State.init ()
        initialModel.IsRuleManagerOpen |> should equal false

        // 管理ボタン押下 -> オープン
        let openModel, _ = State.update OpenRuleManager initialModel
        openModel.IsRuleManagerOpen |> should equal true

        // 閉じる操作 -> クローズ
        let closedModel, _ = State.update CloseRuleManager openModel
        closedModel.IsRuleManagerOpen |> should equal false

    [<Fact>]
    let ``MoveRuleOrder は命名規則の優先度順序を正しく入れ替える`` () =
        let initialModel, _ = State.init ()
        let rule1 = { Id = "r1"; Name = "Rule 1"; Pattern = "{P1}"; PromptInstruction = "I1"; Order = 0 }
        let rule2 = { Id = "r2"; Name = "Rule 2"; Pattern = "{P2}"; PromptInstruction = "I2"; Order = 1 }
        let modelWithRules = { initialModel with Settings = { initialModel.Settings with Rules = [ rule1; rule2 ] } }

        // rule2 を上へ移動 (direction = -1)
        let movedModel, _ = State.update (MoveRuleOrder ("r2", -1)) modelWithRules
        movedModel.Settings.Rules.Head.Id |> should equal "r2"
        movedModel.Settings.Rules.[1].Id |> should equal "r1"

    [<Fact>]
    let ``ScanCompleted は APIキー未設定時にAIのフリをせず、未提案状態として保持し警告案内を表示する`` () =
        let initialModel, _ = State.init ()
        let modelWithoutKey = {
            initialModel with
                Settings = { initialModel.Settings with ApiKey = None }
        }

        let dummyCandidate = {
            FullPath = "E:\\test\\very_long_file_name_over_limit_testing_sample.mp4"
            FileName = "very_long_file_name_over_limit_testing_sample.mp4"
            DirectoryPath = "E:\\test"
            PathLength = 265
            FileSizeBytes = 1024L
            LastWriteTime = DateTime(2025, 8, 12)
        }

        let updatedModel, cmd = State.update (ScanCompleted (Ok [ dummyCandidate ])) modelWithoutKey
        updatedModel.Candidates.Length |> should equal 1
        let proposal = updatedModel.Candidates.Head

        // 重要: LLM未接続なので「AI提案済」ではなく未提案状態
        proposal.IsAiProposed |> should equal false
        // 重要: 勝手な固定コメントを捏造しない
        proposal.AiComment |> should equal None
        // 初期状態は元ファイル名（手動編集用）
        proposal.ProposedFileName |> should equal dummyCandidate.FileName
        // 警告案内メッセージを表示
        updatedModel.ErrorMessage |> should not' (equal None)
        let errMsg = updatedModel.ErrorMessage.Value
        errMsg.Contains("OpenRouter APIキーが未設定") |> should equal true


