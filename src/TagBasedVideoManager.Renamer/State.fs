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
}

and Msg =
    // 初期化・設定
    | SettingsLoaded of Result<RenamerSettings, RenamerError>
    | ThresholdChanged of int
    | TargetDirectoryChanged of string
    | RuleSelected of string

    // 走査 & AI提案 (ワンアクション)
    | ExecuteScanAndPropose
    | ScanCompleted of Result<ScanCandidate list, RenamerError>
    | AiProposeCompleted of Result<RenameProposal list, RenamerError>

    // 候補編集・ソート
    | ToggleCandidateSelect of fullPath: string
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
    | SaveRule of NamingRule
    | DeleteRule of ruleId: string
    | MoveRuleOrder of ruleId: string * direction: int // -1: up, +1: down
    | DismissError
    | DismissConfirm

module State =

    let private defaultSettingsPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion-settings.json")

    let private initialDockerStatus: DockerStatus = {
        State = NotFound
        IsPortAccessible = false
        ContainerId = None
        LastChecked = DateTime.UtcNow
    }

    let init () : Model * Cmd<Msg> =
        let initialSettings = Settings.defaultSettings ()
        let model = {
            Settings = initialSettings
            CurrentThreshold = initialSettings.PathLengthThreshold
            SelectedRuleId = (List.tryHead initialSettings.Rules |> Option.map (fun r -> r.Id) |> Option.defaultValue "")
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
        }

        // 起動時に設定ファイルロードおよびDockerステータス確認を発行
        let loadSettingsCmd =
            Cmd.OfAsync.perform (fun () -> async { return Settings.load defaultSettingsPath }) () SettingsLoaded

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

        // ワンアクション走査 & AI提案
        | ExecuteScanAndPropose ->
            if String.IsNullOrWhiteSpace(model.Settings.TargetDirectory) then
                { model with ErrorMessage = Some "対象ディレクトリが指定されていません。" }, Cmd.none
            else
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async { return FileScanner.scanLongPaths model.Settings.TargetDirectory model.CurrentThreshold })
                        ()
                        ScanCompleted
                { model with IsScanning = true; ErrorMessage = None; Candidates = [] }, cmd

        | ScanCompleted (Ok candidates) ->
            if List.isEmpty candidates then
                { model with IsScanning = false; ErrorMessage = Some "閾値以上の危険な長パス動画ファイルは見つかりませんでした。" }, Cmd.none
            else
                let selectedRule =
                    model.Settings.Rules
                    |> List.tryFind (fun r -> r.Id = model.SelectedRuleId)
                    |> Option.defaultValue (List.head model.Settings.Rules)

                // まずローカル命名規則による短縮候補を即時生成して反映！
                let localProposals =
                    OpenRouterClient.generateLocalProposals selectedRule candidates
                    |> FileScanner.sortCandidates model.SortCriterion

                match model.Settings.ApiKey with
                | Some key when not (String.IsNullOrWhiteSpace(key)) ->
                    let aiCmd =
                        Cmd.OfAsync.perform
                            (fun () ->
                                OpenRouterClient.requestProposals
                                    None
                                    (Some key)
                                    model.Settings.SelectedModel
                                    selectedRule
                                    candidates
                            )
                            ()
                            AiProposeCompleted
                    { model with IsScanning = false; IsRequestingAi = true; Candidates = localProposals }, aiCmd
                | _ ->
                    // APIキー未設定時はローカル短縮ルールをそのまま採用し、直ちにリネーム可能にする
                    {
                        model with
                            IsScanning = false
                            Candidates = localProposals
                            ErrorMessage = Some "💡 命名規則に従って短縮ファイル名（BEFORE/AFTER）を自動生成しました。必要に応じて直接手動修正し、リネームを実行できます。"
                    }, Cmd.none

        | ScanCompleted (Error (IoError (msg, _))) ->
            { model with IsScanning = false; ErrorMessage = Some msg }, Cmd.none
        | ScanCompleted (Error other) ->
            { model with IsScanning = false; ErrorMessage = Some ($"走査失敗: {other}") }, Cmd.none

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
            let remaining = model.Candidates |> List.filter (fun c -> not c.IsSelected)
            let newUndoStack = records :: model.UndoStack
            let newModel = { model with IsRenaming = false; Candidates = remaining; UndoStack = newUndoStack }

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
            { model with IsRuleManagerOpen = true; EditingRule = None }, Cmd.none

        | CloseRuleManager ->
            { model with IsRuleManagerOpen = false; EditingRule = None }, Cmd.none

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
            { model with Settings = newSettings; EditingRule = None }, Cmd.none

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
