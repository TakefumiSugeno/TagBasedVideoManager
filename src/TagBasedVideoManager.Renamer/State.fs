namespace TagBasedVideoManager.Renamer

open System
open System.IO
open Elmish
open TagBasedVideoManager.Renamer

type DialogConfig = {
    Title: string
    Message: string
    ConfirmText: string
    CancelText: string
    OnConfirm: Msg
}

and Model = {
    // 設定
    Settings: RenamerSettings
    CurrentThreshold: int         // UIで変更可能なセッション限定閾値 (非保存)
    SelectedRuleId: string

    // 状態
    IsScanning: bool
    IsRequestingAi: bool
    IsRenaming: bool
    IsDockerBusy: bool
    ErrorMessage: string option

    // データ
    Candidates: RenameProposal list
    UndoStack: UndoRecord list list // 1回のリネーム単位でスタック保持
    Docker: DockerStatus

    // UI表示設定
    Layout: LayoutMode
    SortCriterion: SortCriterion
    IsRuleManagerOpen: bool
    EditingRule: NamingRule option
    ConfirmDialog: DialogConfig option
    AiCancellationCts: System.Threading.CancellationTokenSource option
}

and Msg =
    // 初期化・設定
    | SettingsLoaded of Result<RenamerSettings, RenamerError>
    | ThresholdChanged of int
    | TargetDirectoryChanged of string
    | RuleSelected of string
    | ModelSelected of string

    // 走査 & AI提案 (ワンアクション & 非同期パイプライン)
    | ExecuteScanAndPropose
    | ScanCompleted of Result<ScanCandidate list, RenamerError>
    | CandidateAiProcessing of fullPath: string
    | CandidateAiProposed of RenameProposal
    | AllAiProposalsCompleted
    | CancelAiProposal
    | AiProposeCompleted of Result<RenameProposal list, RenamerError>

    // 候補編集・ソート
    | ToggleCandidateSelect of fullPath: string
    | SetCandidateSelect of fullPath: string * isSelected: bool
    | SelectAllCandidates of bool
    | UpdateProposedName of fullPath: string * newName: string
    | ChangeSortCriterion of SortCriterion

    // リネーム実行 & Undo
    | ExecuteRenameOnly
    | ExecuteRenameAndRestart
    | RenameCompleted of restartContainer: bool * Result<UndoRecord list, RenamerError>
    | RequestUndo
    | ExecuteUndo
    | UndoCompleted of Result<unit, RenamerError>

    // Docker連携
    | CheckDockerStatus
    | DockerStatusUpdated of DockerStatus
    | ExecuteDockerAction of action: string
    | DockerCommandCompleted of action: string * Result<string, RenamerError>

    // 表示切り替え & ルール管理モーダル
    | SetLayoutMode of LayoutMode
    | OpenRuleManager
    | CloseRuleManager
    | StartAddRule
    | UpdateEditingRuleName of string
    | UpdateEditingRulePattern of string
    | UpdateEditingRulePrompt of string
    | UpdateEditingRuleWebSearch of bool
    | SaveEditingRule
    | SaveRule of NamingRule
    | DeleteRule of ruleId: string
    | MoveRuleOrder of ruleId: string * direction: int // -1: up, +1: down
    | DismissError
    | DismissConfirm

module State =

    let private defaultSettingsPath =
        Settings.defaultSavePath ()

    let private initialDockerStatus: DockerStatus = {
        State = NotFound
        IsPortAccessible = false
        ContainerId = None
        LastChecked = DateTime.UtcNow
    }

    /// 画面表示用ソート済Proposalsの順序にScanCandidatesを整列する純粋関数
    let alignCandidatesWithProposals (candidates: ScanCandidate list) (proposals: RenameProposal list) : ScanCandidate list =
        let candidateMap = candidates |> List.map (fun c -> c.FullPath, c) |> Map.ofList
        proposals
        |> List.choose (fun p -> Map.tryFind p.OriginalFullPath candidateMap)

    let private createAsyncPipelineCmd
        (apiKey: string)
        (modelName: string)
        (rule: NamingRule)
        (candidates: ScanCandidate list)
        (cancellationToken: System.Threading.CancellationToken)
        : Cmd<Msg> =
        let sub (dispatch: Msg -> unit) =
            let runPipeline () =
                task {
                    try
                        try
                            for candidate in candidates do
                                if not cancellationToken.IsCancellationRequested then
                                    dispatch (CandidateAiProcessing candidate.FullPath)

                                    // Web検索が有効な場合は ddgs 検索を実行
                                    let! searchSnippets =
                                        task {
                                            if rule.EnableWebSearch then
                                                let query = WebSearchClient.extractSearchQuery candidate.FileName
                                                let! res = WebSearchClient.searchAsync query 3 cancellationToken
                                                match res with
                                                | Ok items -> return items
                                                | Error _ -> return []
                                            else
                                                return []
                                        }

                                    if not cancellationToken.IsCancellationRequested then
                                        let! proposalResult =
                                            Async.StartAsTask(
                                                OpenRouterClient.requestSingleProposal
                                                    None
                                                    (Some apiKey)
                                                    modelName
                                                    rule
                                                    candidate
                                                    searchSnippets
                                                    cancellationToken,
                                                cancellationToken = cancellationToken
                                            )

                                        match proposalResult with
                                        | Ok proposed ->
                                            dispatch (CandidateAiProposed proposed)
                                        | Error err ->
                                            // エラー時でも元候補をベースにエラーコメント付きで更新
                                            let errProposal = {
                                                OriginalFullPath = candidate.FullPath
                                                OriginalFileName = candidate.FileName
                                                DirectoryPath = candidate.DirectoryPath
                                                OriginalLength = candidate.PathLength
                                                ProposedFileName = candidate.FileName
                                                ProposedLength = candidate.PathLength
                                                AiComment = Some $"（AI提案取得失敗: {err}）"
                                                IsSelected = true
                                                LastWriteTime = candidate.LastWriteTime
                                                IsAiProposed = false
                                                IsAiProcessing = false
                                            }
                                            dispatch (CandidateAiProposed errProposal)

                                    // レートリミット対策で少し待機
                                    do! System.Threading.Tasks.Task.Delay(300, cancellationToken)
                        with
                        | :? System.OperationCanceledException -> ()
                        | _ -> ()
                    finally
                        dispatch AllAiProposalsCompleted
                }
            runPipeline () |> ignore

        [ sub ]

    let init () : Model * Cmd<Msg> =
        let initialSettings = Settings.loadConfiguration None None
        let firstRuleId = initialSettings.Rules |> List.tryHead |> Option.map (fun r -> r.Id) |> Option.defaultValue ""
        let model = {
            Settings = initialSettings
            CurrentThreshold = initialSettings.PathLengthThreshold
            SelectedRuleId = firstRuleId
            IsScanning = false
            IsRequestingAi = false
            IsRenaming = false
            IsDockerBusy = false
            ErrorMessage = None
            Candidates = []
            UndoStack = []
            Docker = initialDockerStatus
            Layout = Vertical
            SortCriterion = PathLengthDesc
            IsRuleManagerOpen = false
            EditingRule = None
            ConfirmDialog = None
            AiCancellationCts = None
        }

        // 起動時に外部設定ファイルロードおよびDockerステータス確認を発行
        let loadSettingsCmd =
            Cmd.OfAsync.perform
                (fun () -> async { return Settings.loadConfiguration None None })
                ()
                (fun cfg -> SettingsLoaded (Ok cfg))

        let checkDockerCmd =
            Cmd.OfAsync.perform (fun () -> DockerController.checkStatus None None (Directory.GetCurrentDirectory())) () DockerStatusUpdated

        model, Cmd.batch [ loadSettingsCmd; checkDockerCmd ]

    let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
        match msg with
        | SettingsLoaded (Ok loaded) ->
            let firstRuleId = loaded.Rules |> List.tryHead |> Option.map (fun r -> r.Id) |> Option.defaultValue ""
            {
                model with
                    Settings = loaded
                    CurrentThreshold = loaded.PathLengthThreshold // ロード時の初期値反映
                    SelectedRuleId = if String.IsNullOrEmpty(model.SelectedRuleId) then firstRuleId else model.SelectedRuleId
            }, Cmd.none

        | SettingsLoaded (Error _) ->
            // 設定ファイルが存在しない場合は初期既定値を維持
            model, Cmd.none

        | ThresholdChanged newThreshold ->
            // 最重要: セッション内一時変更（Settings.save は呼ばない）
            { model with CurrentThreshold = max 1 newThreshold }, Cmd.none

        | TargetDirectoryChanged newDir ->
            let updatedSettings = { model.Settings with TargetDirectory = newDir }
            { model with Settings = updatedSettings }, Cmd.none

        | RuleSelected ruleId ->
            { model with SelectedRuleId = ruleId }, Cmd.none

        | ModelSelected newModel ->
            let updatedSettings = { model.Settings with SelectedModel = newModel }
            Settings.save defaultSettingsPath updatedSettings |> ignore
            { model with Settings = updatedSettings }, Cmd.none

        // ワンアクション走査 & AI提案
        | ExecuteScanAndPropose ->
            // 先行するAI提案処理が動いていれば自動キャンセル
            model.AiCancellationCts |> Option.iter (fun cts ->
                try
                    if not cts.IsCancellationRequested then
                        cts.Cancel()
                    cts.Dispose()
                with _ -> ()
            )
            if String.IsNullOrWhiteSpace(model.Settings.TargetDirectory) then
                { model with ErrorMessage = Some "対象ディレクトリが指定されていません。"; AiCancellationCts = None }, Cmd.none
            else
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async { return FileScanner.scanLongPaths model.Settings.TargetDirectory model.CurrentThreshold })
                        ()
                        ScanCompleted
                { model with IsScanning = true; ErrorMessage = None; Candidates = []; AiCancellationCts = None }, cmd

        | ScanCompleted (Ok candidates) ->
            if List.isEmpty candidates then
                { model with IsScanning = false; ErrorMessage = Some "閾値以上の危険な長パス動画ファイルは見つかりませんでした。" }, Cmd.none
            else
                let selectedRule =
                    model.Settings.Rules
                    |> List.tryFind (fun r -> r.Id = model.SelectedRuleId)
                    |> Option.defaultValue (List.head model.Settings.Rules)

                // 走査直後の初期候補データ（即時IO描画用・未提案状態）
                let initialProposals =
                    candidates
                    |> List.map Proposal.createInitial
                    |> FileScanner.sortCandidates model.SortCriterion

                let sortedCandidates = alignCandidatesWithProposals candidates initialProposals

                match model.Settings.ApiKey with
                | Some key when not (String.IsNullOrWhiteSpace(key)) ->
                    let cleanKey = key.Trim().Trim('"', '\'')
                    if cleanKey = "xxx" || cleanKey.Length < 10 then
                        {
                            model with
                                IsScanning = false
                                Candidates = initialProposals
                                ErrorMessage = Some "⚠️ OpenRouter APIキーが未設定またはプレースホルダー ('xxx') のため、AI提案は実行されませんでした（手動編集・リネームは可能です）。AI自動命名を利用する場合は appsettings.json の apiKey に 'sk-or-v1-...' から始まる有効なキーを設定してください。"
                        }, Cmd.none
                    else
                        // 先行CTSがあれば破棄
                        model.AiCancellationCts |> Option.iter (fun cts ->
                            try
                                if not cts.IsCancellationRequested then
                                    cts.Cancel()
                                cts.Dispose()
                            with _ -> ()
                        )
                        let newCts = new System.Threading.CancellationTokenSource()
                        let aiCmd =
                            createAsyncPipelineCmd
                                cleanKey
                                model.Settings.SelectedModel
                                selectedRule
                                sortedCandidates
                                newCts.Token

                        {
                            model with
                                IsScanning = false
                                IsRequestingAi = true
                                Candidates = initialProposals
                                AiCancellationCts = Some newCts
                        }, aiCmd
                | _ ->
                    // APIキー未設定時はAIのフリをせず、未提案状態としてユーザーに設定を案内
                    {
                        model with
                            IsScanning = false
                            Candidates = initialProposals
                            ErrorMessage = Some "⚠️ OpenRouter APIキーが未設定のため、AI提案は実行されませんでした（手動編集・リネームは可能です）。AI自動命名を利用する場合は appsettings.json の apiKey に 'sk-or-v1-...' から始まる有効なキーを設定してください。"
                    }, Cmd.none

        | ScanCompleted (Error (IoError (msg, _))) ->
            { model with IsScanning = false; ErrorMessage = Some msg }, Cmd.none
        | ScanCompleted (Error other) ->
            { model with IsScanning = false; ErrorMessage = Some ($"走査失敗: {other}") }, Cmd.none

        | CandidateAiProcessing fullPath ->
            let updatedCandidates =
                model.Candidates
                |> List.map (fun c ->
                    if c.OriginalFullPath = fullPath then
                        { c with IsAiProcessing = true; AiComment = Some "（Web検索・LLM提案を実行中...）" }
                    else c
                )
            { model with Candidates = updatedCandidates }, Cmd.none

        | CandidateAiProposed proposed ->
            let updatedCandidates =
                model.Candidates
                |> List.map (fun c ->
                    if c.OriginalFullPath = proposed.OriginalFullPath then
                        { proposed with IsAiProcessing = false; IsAiProposed = true }
                    else c
                )
                |> FileScanner.sortCandidates model.SortCriterion
            { model with Candidates = updatedCandidates }, Cmd.none

        | AllAiProposalsCompleted ->
            let cleanedCandidates =
                model.Candidates
                |> List.map (fun c ->
                    if c.IsAiProcessing then
                        { c with IsAiProcessing = false }
                    else c
                )
            {
                model with
                    IsRequestingAi = false
                    Candidates = cleanedCandidates
                    AiCancellationCts = None
            }, Cmd.none

        | CancelAiProposal ->
            model.AiCancellationCts |> Option.iter (fun cts ->
                try
                    if not cts.IsCancellationRequested then
                        cts.Cancel()
                    cts.Dispose()
                with _ -> ()
            )
            let cancelledCandidates =
                model.Candidates
                |> List.map (fun c ->
                    if c.IsAiProcessing then
                        { c with
                            IsAiProcessing = false
                            AiComment = Some "（AI提案が中止されました）" }
                    else c
                )
            {
                model with
                    IsRequestingAi = false
                    Candidates = cancelledCandidates
                    AiCancellationCts = None
            }, Cmd.none

        | AiProposeCompleted (Ok proposals) ->
            let merged =
                if List.isEmpty proposals then model.Candidates
                else proposals |> FileScanner.sortCandidates model.SortCriterion
            { model with IsRequestingAi = false; Candidates = merged }, Cmd.none

        | AiProposeCompleted (Error (OpenRouterError (code, msg))) ->
            // AI通信エラー時もローカル提案候補を維持し、ユーザーが手動編集・リネームできるようにする
            {
                model with
                    IsRequestingAi = false
                    ErrorMessage = Some ($"⚠️ OpenRouter AI通信エラー ({code}) が発生したため、ローカル短縮候補を維持しました: {msg}")
            }, Cmd.none
        | AiProposeCompleted (Error other) ->
            {
                model with
                    IsRequestingAi = false
                    ErrorMessage = Some ($"⚠️ AI提案の取得に失敗したため、ローカル短縮候補を維持しました: {other}")
            }, Cmd.none

        // 候補編集・ソート
        | ToggleCandidateSelect fullPath ->
            let updated =
                model.Candidates
                |> List.map (fun c ->
                    if c.OriginalFullPath = fullPath then { c with IsSelected = not c.IsSelected }
                    else c
                )
            { model with Candidates = updated }, Cmd.none

        | SetCandidateSelect (fullPath, isSelected) ->
            let updated =
                model.Candidates
                |> List.map (fun c ->
                    if c.OriginalFullPath = fullPath then { c with IsSelected = isSelected }
                    else c
                )
            { model with Candidates = updated }, Cmd.none

        | SelectAllCandidates selectAll ->
            let updated = model.Candidates |> List.map (fun c -> { c with IsSelected = selectAll })
            { model with Candidates = updated }, Cmd.none

        | ChangeSortCriterion newCriterion ->
            let sorted = FileScanner.sortCandidates newCriterion model.Candidates
            { model with SortCriterion = newCriterion; Candidates = sorted }, Cmd.none

        | UpdateProposedName (fullPath, newName) ->
            let updated =
                model.Candidates
                |> List.map (fun c ->
                    if c.OriginalFullPath = fullPath then
                        let newProposedPath = Path.Combine(c.DirectoryPath, newName)
                        { c with ProposedFileName = newName; ProposedLength = newProposedPath.Length }
                    else c
                )
            { model with Candidates = updated }, Cmd.none

        // リネーム実行 & Undo
        | ExecuteRenameOnly ->
            let selected = model.Candidates |> List.filter (fun c -> c.IsSelected)
            if List.isEmpty selected then
                { model with ErrorMessage = Some "リネーム対象が選択されていません。" }, Cmd.none
            else
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async { return FileRenamer.executeRename selected })
                        ()
                        (fun res -> RenameCompleted (false, res))
                { model with IsRenaming = true; ErrorMessage = None }, cmd

        | ExecuteRenameAndRestart ->
            let selected = model.Candidates |> List.filter (fun c -> c.IsSelected)
            if List.isEmpty selected then
                { model with ErrorMessage = Some "リネーム対象が選択されていません。" }, Cmd.none
            else
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async { return FileRenamer.executeRename selected })
                        ()
                        (fun res -> RenameCompleted (true, res))
                { model with IsRenaming = true; ErrorMessage = None }, cmd

        | RenameCompleted (restartContainer, Ok records) ->
            let recordMap =
                records
                |> List.map (fun r -> r.OriginalFullPath, r.RenamedFullPath)
                |> Map.ofList

            let updatedCandidates =
                model.Candidates
                |> List.choose (fun c ->
                    match Map.tryFind c.OriginalFullPath recordMap with
                    | Some renamedFullPath ->
                        // リネーム実行対象: 抽出基準（閾値）に該当しなくなった場合は表示せず今後の処理対象としない
                        if renamedFullPath.Length < model.CurrentThreshold then
                            None
                        else
                            // リネーム後も抽出基準に該当する場合、最新ファイル情報に更新して候補に残す
                            let newFileName = System.IO.Path.GetFileName(renamedFullPath)
                            Some {
                                c with
                                    OriginalFullPath = renamedFullPath
                                    OriginalFileName = newFileName
                                    OriginalLength = renamedFullPath.Length
                                    ProposedFileName = newFileName
                                    ProposedLength = renamedFullPath.Length
                                    IsSelected = false
                                    IsAiProposed = false
                                    IsAiProcessing = false
                                    AiComment = None
                            }
                    | None ->
                        // リネーム対象外（未選択）の候補はそのまま保持
                        Some c
                )

            let newUndoStack = records :: model.UndoStack
            let newModel = { model with IsRenaming = false; Candidates = updatedCandidates; UndoStack = newUndoStack }

            if restartContainer then
                let restartCmd =
                    Cmd.OfAsync.perform
                        (fun () -> DockerController.executeAction None (Directory.GetCurrentDirectory()) "restart tag-based-video-manager")
                        ()
                        (fun res -> DockerCommandCompleted ("restart", res))
                newModel, Cmd.batch [ restartCmd; Cmd.ofMsg CheckDockerStatus ]
            else
                newModel, Cmd.none

        | RenameCompleted (_, Error (IoError (msg, _))) ->
            { model with IsRenaming = false; ErrorMessage = Some msg }, Cmd.none
        | RenameCompleted (_, Error other) ->
            { model with IsRenaming = false; ErrorMessage = Some ($"リネーム実行エラー: {other}") }, Cmd.none

        | RequestUndo ->
            match model.UndoStack with
            | [] -> { model with ErrorMessage = Some "元に戻す履歴がありません。" }, Cmd.none
            | lastRecords :: _ ->
                // Undo確認ダイアログを表示
                let dialog = {
                    Title = "リネームの取り消し (Undo)"
                    Message = $"直前に実行した {lastRecords.Length} 件のリネームを元に戻します。\n元の長いファイル名に復元され、コンテナが再起動します。よろしいですか？"
                    ConfirmText = "元に戻して再起動"
                    CancelText = "キャンセル"
                    OnConfirm = ExecuteUndo
                }
                { model with ConfirmDialog = Some dialog }, Cmd.none

        | ExecuteUndo ->
            match model.UndoStack with
            | [] -> { model with ConfirmDialog = None }, Cmd.none
            | lastRecords :: remainingStack ->
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async {
                            match FileRenamer.executeUndo lastRecords with
                            | Ok () ->
                                // 元ファイル復元成功後、コンテナを再起動
                                let! _ = DockerController.executeAction None (Directory.GetCurrentDirectory()) "restart tag-based-video-manager"
                                return Ok ()
                            | Error err ->
                                return Error err
                        })
                        ()
                        UndoCompleted
                { model with IsRenaming = true; ConfirmDialog = None; UndoStack = remainingStack }, cmd

        | UndoCompleted (Ok ()) ->
            { model with IsRenaming = false }, Cmd.ofMsg CheckDockerStatus

        | UndoCompleted (Error (UndoConflictError (path, msg))) ->
            { model with IsRenaming = false; ErrorMessage = Some ($"Undo衝突エラー: {msg} (Path: {path})") }, Cmd.none
        | UndoCompleted (Error other) ->
            { model with IsRenaming = false; ErrorMessage = Some ($"Undoエラー: {other}") }, Cmd.none

        // Docker 連携
        | CheckDockerStatus ->
            let cmd =
                Cmd.OfAsync.perform
                    (fun () -> DockerController.checkStatus None None (Directory.GetCurrentDirectory()))
                    ()
                    DockerStatusUpdated
            model, cmd

        | DockerStatusUpdated status ->
            { model with Docker = status; IsDockerBusy = false }, Cmd.none

        | ExecuteDockerAction action ->
            let cmd =
                Cmd.OfAsync.perform
                    (fun () -> DockerController.executeAction None (Directory.GetCurrentDirectory()) action)
                    ()
                    (fun res -> DockerCommandCompleted (action, res))
            { model with IsDockerBusy = true }, cmd

        | DockerCommandCompleted (_, Ok _) ->
            model, Cmd.ofMsg CheckDockerStatus
        | DockerCommandCompleted (action, Error (DockerError (_, _, stderr))) ->
            { model with IsDockerBusy = false; ErrorMessage = Some ($"Docker コマンド '{action}' エラー: {stderr}") }, Cmd.ofMsg CheckDockerStatus
        | DockerCommandCompleted (_, Error other) ->
            { model with IsDockerBusy = false; ErrorMessage = Some ($"Docker エラー: {other}") }, Cmd.ofMsg CheckDockerStatus

        // UI切り替え
        | SetLayoutMode mode ->
            { model with Layout = mode }, Cmd.none

        | OpenRuleManager ->
            let templateRule = {
                Id = Guid.NewGuid().ToString("N")
                Name = ""
                Pattern = "*.mp4"
                PromptInstruction = ""
                Order = model.Settings.Rules.Length
                EnableWebSearch = true
            }
            { model with IsRuleManagerOpen = true; EditingRule = Some templateRule }, Cmd.none

        | CloseRuleManager ->
            { model with IsRuleManagerOpen = false; EditingRule = None }, Cmd.none

        | StartAddRule ->
            let templateRule = {
                Id = Guid.NewGuid().ToString("N")
                Name = ""
                Pattern = "*.mp4"
                PromptInstruction = ""
                Order = model.Settings.Rules.Length
                EnableWebSearch = true
            }
            { model with EditingRule = Some templateRule }, Cmd.none

        | UpdateEditingRuleName name ->
            let updated =
                model.EditingRule
                |> Option.map (fun r -> { r with Name = name })
            { model with EditingRule = updated }, Cmd.none

        | UpdateEditingRulePattern pattern ->
            let updated =
                model.EditingRule
                |> Option.map (fun r -> { r with Pattern = pattern })
            { model with EditingRule = updated }, Cmd.none

        | UpdateEditingRulePrompt prompt ->
            let updated =
                model.EditingRule
                |> Option.map (fun r -> { r with PromptInstruction = prompt })
            { model with EditingRule = updated }, Cmd.none

        | UpdateEditingRuleWebSearch enabled ->
            let updated =
                model.EditingRule
                |> Option.map (fun r -> { r with EnableWebSearch = enabled })
            { model with EditingRule = updated }, Cmd.none

        | SaveEditingRule ->
            match model.EditingRule with
            | Some rule when not (String.IsNullOrWhiteSpace(rule.Name)) ->
                let newRule = {
                    rule with
                        Name = rule.Name.Trim()
                        Pattern = if String.IsNullOrWhiteSpace(rule.Pattern) then "*.*" else rule.Pattern.Trim()
                        PromptInstruction = if String.IsNullOrWhiteSpace(rule.PromptInstruction) then "簡潔に短縮してください。" else rule.PromptInstruction.Trim()
                        Order = model.Settings.Rules.Length
                }
                let updatedRules = model.Settings.Rules @ [ newRule ]
                let newSettings = { model.Settings with Rules = updatedRules }
                Settings.save defaultSettingsPath newSettings |> ignore
                let nextTemplate = {
                    Id = Guid.NewGuid().ToString("N")
                    Name = ""
                    Pattern = "*.mp4"
                    PromptInstruction = ""
                    Order = updatedRules.Length
                    EnableWebSearch = true
                }
                { model with
                    Settings = newSettings
                    SelectedRuleId = newRule.Id
                    EditingRule = Some nextTemplate
                }, Cmd.none
            | _ ->
                { model with ErrorMessage = Some "ルール名を入力してください。" }, Cmd.none

        | SaveRule rule ->
            let existingIdx = model.Settings.Rules |> List.tryFindIndex (fun r -> r.Id = rule.Id)
            let updatedRules =
                match existingIdx with
                | Some idx ->
                    model.Settings.Rules |> List.mapi (fun i r -> if i = idx then rule else r)
                | None ->
                    model.Settings.Rules @ [ { rule with Order = model.Settings.Rules.Length } ]
            let newSettings = { model.Settings with Rules = updatedRules }
            Settings.save defaultSettingsPath newSettings |> ignore
            { model with Settings = newSettings }, Cmd.none

        | DeleteRule ruleId ->
            let filteredRules =
                model.Settings.Rules
                |> List.filter (fun r -> r.Id <> ruleId)
                |> List.mapi (fun idx r -> { r with Order = idx })
            let newSettings = { model.Settings with Rules = filteredRules }
            Settings.save defaultSettingsPath newSettings |> ignore
            { model with Settings = newSettings }, Cmd.none

        | MoveRuleOrder (ruleId, direction) ->
            let rules = model.Settings.Rules
            let idxOpt = rules |> List.tryFindIndex (fun r -> r.Id = ruleId)
            match idxOpt with
            | Some idx when (direction = -1 && idx > 0) || (direction = 1 && idx < rules.Length - 1) ->
                let targetIdx = idx + direction
                let mutable arr = rules |> List.toArray
                let temp = arr.[idx]
                arr.[idx] <- arr.[targetIdx]
                arr.[targetIdx] <- temp
                let reordered = arr |> Array.mapi (fun i r -> { r with Order = i }) |> Array.toList
                let newSettings = { model.Settings with Rules = reordered }
                Settings.save defaultSettingsPath newSettings |> ignore
                { model with Settings = newSettings }, Cmd.none
            | _ ->
                model, Cmd.none

        | DismissError ->
            { model with ErrorMessage = None }, Cmd.none

        | DismissConfirm ->
            { model with ConfirmDialog = None }, Cmd.none
