namespace TagBasedVideoManager.Renamer.Tests

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
            IsSelected = true
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
            IsSelected = true
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
