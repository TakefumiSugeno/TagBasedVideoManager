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
            IsAiProcessing = false
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
            IsAiProcessing = false
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
            IsAiProcessing = false
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
            IsAiProcessing = false
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
            IsAiProcessing = false
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
        let rule1 = { Id = "r1"; Name = "Rule 1"; Pattern = "{P1}"; PromptInstruction = "I1"; Order = 0; EnableWebSearch = false }
        let rule2 = { Id = "r2"; Name = "Rule 2"; Pattern = "{P2}"; PromptInstruction = "I2"; Order = 1; EnableWebSearch = true }
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
        // 初期状態は待機中コメント
        proposal.AiComment |> should equal (Some "（AI提案の開始を待機しています...）")
        // 初期状態は元ファイル名（手動編集用）
        proposal.ProposedFileName |> should equal dummyCandidate.FileName
        // 警告案内メッセージを表示
        updatedModel.ErrorMessage |> should not' (equal None)
        let errMsg = updatedModel.ErrorMessage.Value
        errMsg.Contains("OpenRouter APIキーが未設定") |> should equal true

    [<Fact>]
    let ``RenameCompleted はリネーム後のパス長が閾値未満になった候補を除外する`` () =
        let initialModel, _ = State.init ()
        let model = { initialModel with CurrentThreshold = 50 }

        let p1 = {
            OriginalFullPath = "C:\\Videos\\long_path_that_was_over_limit_sample_1.mp4"
            OriginalFileName = "long_path_that_was_over_limit_sample_1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 250
            ProposedFileName = "short1.mp4"
            ProposedLength = 20
            AiComment = None
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
            IsAiProposed = true
            IsAiProcessing = false
        }
        let p2 = {
            OriginalFullPath = "C:\\Videos\\long_path_that_was_over_limit_sample_2.mp4"
            OriginalFileName = "long_path_that_was_over_limit_sample_2.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 260
            ProposedFileName = "still_somewhat_long_file_name_that_exceeds_threshold.mp4"
            ProposedLength = 245
            AiComment = None
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
            IsAiProposed = true
            IsAiProcessing = false
        }
        let p3_unselected = {
            OriginalFullPath = "C:\\Videos\\unselected_long_file.mp4"
            OriginalFileName = "unselected_long_file.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 255
            ProposedFileName = "unselected_long_file.mp4"
            ProposedLength = 255
            AiComment = None
            IsSelected = false
            LastWriteTime = DateTime.UtcNow
            IsAiProposed = false
            IsAiProcessing = false
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

    [<Fact>]
    let ``ModelSelected は選択モデルを更新する`` () =
        let initialModel, _ = State.init ()
        let newModelName = "google/gemini-2.0-flash-exp:free"
        let updatedModel, _ = State.update (ModelSelected newModelName) initialModel
        updatedModel.Settings.SelectedModel |> should equal newModelName

    [<Fact>]
    let ``OpenRuleManager と SaveEditingRule で新規命名規則を追加できる`` () =
        let initialModel, _ = State.init ()
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
        savedModel.Settings.Rules.Length |> should equal (initialRuleCount + 1)
        let added = savedModel.Settings.Rules |> List.last
        added.Name |> should equal "テスト新ルール"
        added.Pattern |> should equal "*.mkv"
        added.PromptInstruction |> should equal "テスト用プロンプト指示"
        added.EnableWebSearch |> should equal true
        savedModel.SelectedRuleId |> should equal added.Id

    [<Fact>]
    let ``Proposal.createInitial はスキャン候補から即時IO描画用の初期未提案レコードを生成する`` () =
        let candidate: ScanCandidate = {
            FullPath = "C:\\Videos\\long_test_sample.mp4"
            FileName = "long_test_sample.mp4"
            DirectoryPath = "C:\\Videos"
            PathLength = 30
            FileSizeBytes = 2048L
            LastWriteTime = DateTime(2025, 1, 1)
        }
        let initial = Proposal.createInitial candidate
        initial.OriginalFullPath |> should equal candidate.FullPath
        initial.OriginalFileName |> should equal candidate.FileName
        initial.DirectoryPath |> should equal candidate.DirectoryPath
        initial.OriginalLength |> should equal candidate.PathLength
        initial.ProposedFileName |> should equal candidate.FileName
        initial.ProposedLength |> should equal candidate.PathLength
        initial.AiComment |> should equal (Some "（AI提案の開始を待機しています...）")
        initial.IsSelected |> should equal true
        initial.LastWriteTime |> should equal candidate.LastWriteTime
        initial.IsAiProposed |> should equal false
        initial.IsAiProcessing |> should equal false

    [<Fact>]
    let ``CandidateAiProcessing は対象候補を処理中状態（IsAiProcessing = true）に更新する`` () =
        let initialModel, _ = State.init ()
        let p1 = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 15
            ProposedFileName = "v1.mp4"
            ProposedLength = 15
            AiComment = Some "（待機中...）"
            IsAiProposed = false
            IsAiProcessing = false
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
        }
        let p2 = { p1 with OriginalFullPath = "C:\\Videos\\v2.mp4"; OriginalFileName = "v2.mp4" }
        let model = { initialModel with Candidates = [ p1; p2 ]; IsRequestingAi = true }

        let updatedModel, _ = State.update (CandidateAiProcessing p1.OriginalFullPath) model
        let updatedP1 = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = p1.OriginalFullPath)
        let updatedP2 = updatedModel.Candidates |> List.find (fun c -> c.OriginalFullPath = p2.OriginalFullPath)
        updatedP1.IsAiProcessing |> should equal true
        updatedP2.IsAiProcessing |> should equal false

    [<Fact>]
    let ``CandidateAiProposed は1件ごとの提案結果を反映し IsAiProcessing を解除する`` () =
        let initialModel, _ = State.init ()
        let p1 = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 250
            ProposedFileName = "v1.mp4"
            ProposedLength = 250
            AiComment = Some "（待機中...）"
            IsAiProposed = false
            IsAiProcessing = true
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
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

    [<Fact>]
    let ``AllAiProposalsCompleted は IsRequestingAi を false にし CTS をクリアする`` () =
        let initialModel, _ = State.init ()
        let cts = new System.Threading.CancellationTokenSource()
        let model = { initialModel with IsRequestingAi = true; AiCancellationCts = Some cts }

        let updatedModel, _ = State.update AllAiProposalsCompleted model
        updatedModel.IsRequestingAi |> should equal false
        updatedModel.AiCancellationCts |> should equal None

    [<Fact>]
    let ``CancelAiProposal は CTS をキャンセルし、処理中フラグを解除して中止コメントを付与する`` () =
        let initialModel, _ = State.init ()
        let cts = new System.Threading.CancellationTokenSource()
        let p1 = {
            OriginalFullPath = "C:\\Videos\\v1.mp4"
            OriginalFileName = "v1.mp4"
            DirectoryPath = "C:\\Videos"
            OriginalLength = 250
            ProposedFileName = "v1.mp4"
            ProposedLength = 250
            AiComment = Some "（待機中...）"
            IsAiProposed = false
            IsAiProcessing = true
            IsSelected = true
            LastWriteTime = DateTime.UtcNow
        }
        let model = { initialModel with Candidates = [ p1 ]; IsRequestingAi = true; AiCancellationCts = Some cts }

        let updatedModel, _ = State.update CancelAiProposal model
        cts.IsCancellationRequested |> should equal true
        updatedModel.IsRequestingAi |> should equal false
        updatedModel.AiCancellationCts |> should equal None
        let updatedP1 = updatedModel.Candidates.Head
        updatedP1.IsAiProcessing |> should equal false
        updatedP1.AiComment |> should equal (Some "（AI提案が中止されました）")

    [<Fact>]
    let ``ExecuteScanAndPropose は先行の AI 処理 CTS が存在する場合に自動キャンセルする`` () =
        let initialModel, _ = State.init ()
        let cts = new System.Threading.CancellationTokenSource()
        let model = { initialModel with IsRequestingAi = true; AiCancellationCts = Some cts }

        let updatedModel, _ = State.update ExecuteScanAndPropose model
        cts.IsCancellationRequested |> should equal true
        updatedModel.IsScanning |> should equal true
        updatedModel.AiCancellationCts |> should equal None

    [<Fact>]
    let ``alignCandidatesWithProposals は画面表示用ソート済Proposalsの順序にScanCandidatesを整列する`` () =
        let cShort: ScanCandidate = {
            FullPath = "C:\\a.mp4"
            FileName = "a.mp4"
            DirectoryPath = "C:\\"
            PathLength = 8
            FileSizeBytes = 100L
            LastWriteTime = DateTime.UtcNow
        }
        let cLong: ScanCandidate = {
            FullPath = "C:\\very_long_path_sample_file_12345.mp4"
            FileName = "very_long_path_sample_file_12345.mp4"
            DirectoryPath = "C:\\"
            PathLength = 40
            FileSizeBytes = 100L
            LastWriteTime = DateTime.UtcNow
        }
        let cMid: ScanCandidate = {
            FullPath = "C:\\middle_length_path.mp4"
            FileName = "middle_length_path.mp4"
            DirectoryPath = "C:\\"
            PathLength = 25
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

