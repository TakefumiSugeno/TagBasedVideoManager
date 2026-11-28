namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Text
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
    FilterNonJapaneseOnly: bool   // 日本語を含まないファイル名のみ抽出するフラグ (セッション限定, 非保存)
    FilterConstraint: ConstraintFilter // 表示フィルタ (セッション限定, 非保存)
    SelectedRuleId: string

    // 状態
    IsScanning: bool
    IsRequestingAi: bool
    IsRenaming: bool
    IsDockerBusy: bool
    ErrorMessage: string option

    // 拡張状態 (Phase 2 スピナー & 環境バッジ用)
    HasScanned: bool
    ScannedVideoCount: int
    ActiveEnvironment: string
    SettingsSavePath: string
    ApiKeySourcePath: string option

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
    AiPipelineRunId: Guid option
}

and Msg =
    // 初期化・設定
    | SettingsLoaded of Result<RenamerSettings, RenamerError>
    | ReloadSettings
    | ThresholdChanged of int
    | ToggleFilterNonJapaneseOnly of bool
    | SetConstraintFilter of ConstraintFilter
    | TargetDirectoryChanged of string
    | RuleSelected of string
    | ModelSelected of string

    // 走査 & AI提案 (ワンアクション & 非同期パイプライン)
    | ExecuteScanAndPropose
    | ScanCompleted of Result<ScanCandidate list, RenamerError>
    | CandidateAiProcessing of runId: Guid * fullPath: string
    | CandidateAiProposed of runId: Guid * RenameProposal
    | AllAiProposalsCompleted of runId: Guid
    | CancelAiProposal
    | RetryCandidateAiProposal of fullPath: string
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
    | StartEditRule of ruleId: string
    | CancelEditRule
    | UpdateEditingRuleName of string
    | UpdateEditingRulePattern of string
    | UpdateEditingRulePrompt of string
    | UpdateEditingRuleWebSearch of bool
    | SaveEditingRule
    | SaveRule of NamingRule
    | RequestDeleteRule of ruleId: string
    | DeleteRule of ruleId: string
    | MoveRuleOrder of ruleId: string * direction: int // -1: up, +1: down
    | DismissError
    | DismissConfirm

module State =

    let private initialDockerStatus: DockerStatus = {
        State = NotFound
        IsPortAccessible = false
        ContainerId = None
        LastChecked = DateTime.UtcNow
    }

    /// <summary>設定解決トレース・読込ファイル・出典パス（秘密値除外）を標準エラー出力へ出力する</summary>
    let private emitConfigDiagnostics (meta: LoadedConfiguration) : unit =
        for line in meta.TraceLogs do
            // [WARN] 行は Settings 側で既に出力済みのため、重複出力を避けて [TRACE] 等のみ出力する
            if not (line.StartsWith("[WARN]", StringComparison.Ordinal)) then
                eprintfn "%s" line
        let loaded =
            if List.isEmpty meta.LoadedFiles then "(なし)"
            else String.Join(", ", meta.LoadedFiles)
        let source = meta.ApiKeySourcePath |> Option.defaultValue "(未設定)"
        eprintfn "[TRACE] 設定確定: Env=%s | SavePath=%s | LoadedFiles=[%s] | ApiKeySource=%s" meta.EnvironmentName meta.SavePath loaded source

    /// <summary>コンテキストの有無に応じて設定メタデータを同期ロードし、診断トレースを出力する（単一の設定解決経路）</summary>
    let private loadConfigWithContext (ctxOpt: ConfigLoadContext option) : LoadedConfiguration =
        let meta =
            match ctxOpt with
            | Some ctx -> Settings.loadConfigurationWithMetadataWithContext ctx None None None
            | None -> Settings.loadConfigurationWithMetadata None None None
        emitConfigDiagnostics meta
        meta

    /// <summary>設定保存先パスが未指定・空白の場合に規定パスへフォールバック解決する</summary>
    let private resolveEffectiveSavePath (preferredSavePath: string) : string =
        if String.IsNullOrWhiteSpace(preferredSavePath) then
            Settings.resolveSavePath None
        else
            preferredSavePath

    /// <summary>希望するルール ID が一覧に存在すれば維持し、存在しない場合は先頭ルール ID（空時は ""）へ解決する</summary>
    let private resolveSelectedRuleId (preferredRuleId: string) (rules: NamingRule list) : string =
        if not (String.IsNullOrWhiteSpace(preferredRuleId))
           && (rules |> List.exists (fun r -> r.Id = preferredRuleId)) then
            preferredRuleId
        else
            rules
            |> List.tryHead
            |> Option.map (fun r -> r.Id)
            |> Option.defaultValue ""

    /// <summary>先行する AI パイプライン用 CancellationTokenSource を安全にキャンセル・破棄する（リーク防止）</summary>
    let private disposeAndClearCts (ctsOpt: System.Threading.CancellationTokenSource option) : unit =
        ctsOpt
        |> Option.iter (fun cts ->
            try
                if not cts.IsCancellationRequested then
                    cts.Cancel()
                cts.Dispose()
            with
            | :? ObjectDisposedException -> ()
            | ex ->
                eprintfn "[WARN] CTSの破棄またはキャンセル中に予期せぬエラーが発生しました: %s" ex.Message
        )

    /// <summary>先行 CTS を破棄した上で新しい実行 ID (runId) と CTS を発行する</summary>
    let private issueNewAiPipeline (model: Model) : Guid * System.Threading.CancellationTokenSource =
        disposeAndClearCts model.AiCancellationCts
        Guid.NewGuid(), new System.Threading.CancellationTokenSource()

    /// <summary>非 AI 分岐や中止時に AI パイプライン状態と CTS を確実にリセットしスピナーのゾンビ化を防ぐ</summary>
    let private resetAiPipelineState (model: Model) : Model =
        disposeAndClearCts model.AiCancellationCts
        {
            model with
                IsRequestingAi = false
                AiPipelineRunId = None
                AiCancellationCts = None
        }

    /// <summary>全候補の処理中フラグ (IsAiProcessing) を一括解除する</summary>
    let private clearAllAiProcessing (candidates: RenameProposal list) : RenameProposal list =
        candidates
        |> List.map (fun c -> if c.IsAiProcessing then { c with IsAiProcessing = false } else c)

    /// <summary>新規ルール編集用の初期テンプレートを生成する</summary>
    let private createRuleTemplate (order: int) : NamingRule =
        {
            Id = Guid.NewGuid().ToString("N")
            Name = ""
            Pattern = "{Code}_{Summary}_{Actor}"
            PromptInstruction = ""
            Order = order
            EnableWebSearch = true
        }

    /// <summary>現在選択中の命名規則を取得する（未一致時は先頭または既定ルールへフォールバック）</summary>
    let private resolveActiveRule (model: Model) : NamingRule =
        model.Settings.Rules
        |> List.tryFind (fun r -> r.Id = model.SelectedRuleId)
        |> Option.orElseWith (fun () -> List.tryHead model.Settings.Rules)
        |> Option.defaultWith (fun () -> (Settings.defaultSettings ()).Rules.Head)

    /// <summary>
    /// ScanCompleted と RetryCandidateAiProposal で共有する統一 API キー検証ガード。
    /// 未設定時は環境別保存先を、プレースホルダー時は出典ファイルパスを案内し AI 処理を抑止する。
    /// </summary>
    let private validateApiKeyForAi (model: Model) : Result<string, string> =
        let fallbackSavePath = resolveEffectiveSavePath model.SettingsSavePath

        match Settings.validateApiKey model.Settings.ApiKey with
        | ValidApiKey cleanKey ->
            Ok cleanKey
        | MissingApiKey ->
            Error $"⚠️ OpenRouter APIキーが未設定のため、AI提案はスキップされました（手動編集・リネームは可能です）。設定ファイル ({fallbackSavePath}) に {{ \"apiKey\": \"sk-or-v1-...\" }} の形式で設定後、「🔄 設定を再読込」をクリックしてください。"
        | PlaceholderApiKey rawValue ->
            let sourcePath =
                match model.ApiKeySourcePath with
                | Some p when not (String.IsNullOrWhiteSpace(p)) -> p
                | _ -> fallbackSavePath
            Error $"⚠️ OpenRouter APIキーがプレースホルダー値 ('{rawValue}') のため、AI提案は実行されませんでした（手動編集・リネームは可能です）。設定ファイル ({sourcePath}) の apiKey に {{ \"apiKey\": \"sk-or-v1-...\" }} の形式で設定後、「🔄 設定を再読込」をクリックしてください。"

    /// 画面表示用ソート済Proposalsの順序にScanCandidatesを整列する純粋関数
    let alignCandidatesWithProposals (candidates: ScanCandidate list) (proposals: RenameProposal list) : ScanCandidate list =
        let candidateMap = candidates |> List.map (fun c -> c.FullPath, c) |> Map.ofList
        proposals
        |> List.choose (fun p -> Map.tryFind p.OriginalFullPath candidateMap)

    /// 現在の表示フィルタ設定に従ってリネーム候補リストを絞り込む
    let getFilteredCandidates (filter: ConstraintFilter) (candidates: RenameProposal list) : RenameProposal list =
        match filter with
        | FilterAll -> candidates
        | FilterLinuxOnly ->
            candidates |> List.filter (fun c -> c.OriginalViolation = LinuxFileNameOnly)
        | FilterWindowsOnly ->
            candidates |> List.filter (fun c -> c.OriginalViolation = WindowsPathOnly)
        | FilterBothOnly ->
            candidates |> List.filter (fun c -> c.OriginalViolation = BothViolated)

    let private createAsyncPipelineCmd
        (runId: Guid)
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
                                    dispatch (CandidateAiProcessing (runId, candidate.FullPath))

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
                                            dispatch (CandidateAiProposed (runId, proposed))
                                        | Error err ->
                                            // エラー時でも元候補をベースにエラーコメント付きで更新
                                            let errProposal = {
                                                OriginalFullPath = candidate.FullPath
                                                OriginalFileName = candidate.FileName
                                                DirectoryPath = candidate.DirectoryPath
                                                OriginalLength = candidate.PathLength
                                                OriginalFileNameBytes = candidate.FileNameBytes
                                                OriginalViolation = candidate.Violation
                                                ProposedFileName = candidate.FileName
                                                ProposedLength = candidate.PathLength
                                                ProposedFileNameBytes = candidate.FileNameBytes
                                                AiComment = Some $"（AI提案取得失敗: {err}）"
                                                IsSelected = true
                                                LastWriteTime = candidate.LastWriteTime
                                                IsAiProposed = false
                                                IsAiProcessing = false
                                                AiError = Some $"AI提案失敗: {err}"
                                            }
                                            dispatch (CandidateAiProposed (runId, errProposal))

                                    // レートリミット対策で少し待機
                                    do! System.Threading.Tasks.Task.Delay(300, cancellationToken)
                        with
                        | :? System.OperationCanceledException -> ()
                        | ex ->
                            eprintfn "[WARN] AI提案パイプライン実行中に予期せぬエラーが発生しました (runId=%O): %s" runId ex.Message
                    finally
                        dispatch (AllAiProposalsCompleted runId)
                }
            runPipeline () |> ignore

        [ sub ]

    /// <summary>
    /// API キー検証後に AI パイプラインを起動し、検証失敗時はパイプライン状態と処理中フラグを安全にリセットする。
    /// </summary>
    let private startAiPipelineOrReset
        (baseModel: Model)
        (targetCandidates: ScanCandidate list)
        (onStarted: Model -> Model)
        (onApiKeyError: string -> Model -> Model)
        : Model * Cmd<Msg> =
        match validateApiKeyForAi baseModel with
        | Ok cleanKey ->
            let runId, cts = issueNewAiPipeline baseModel
            let selectedRule = resolveActiveRule baseModel
            let pipelineCmd =
                createAsyncPipelineCmd runId cleanKey baseModel.Settings.SelectedModel selectedRule targetCandidates cts.Token
            let nextModel =
                {
                    baseModel with
                        IsRequestingAi = true
                        AiCancellationCts = Some cts
                        AiPipelineRunId = Some runId
                }
                |> onStarted
            nextModel, pipelineCmd
        | Error errMsg ->
            let resetModel =
                baseModel
                |> resetAiPipelineState
                |> onApiKeyError errMsg
            resetModel, Cmd.none

    /// <summary>
    /// コンテキスト注入対応の初期化処理。
    /// 設定を同期メタデータロードで確定して二重ロードを排除し、起動時警告があればバナーへ反映する。
    /// </summary>
    let initWithContext (ctxOpt: ConfigLoadContext option) : Model * Cmd<Msg> =
        let meta = loadConfigWithContext ctxOpt
        let firstRuleId = resolveSelectedRuleId "" meta.Settings.Rules
        let startupWarning =
            if List.isEmpty meta.Warnings then None
            else Some (String.Join("\n", meta.Warnings))

        let model = {
            Settings = meta.Settings
            CurrentThreshold = meta.Settings.PathLengthThreshold
            FilterNonJapaneseOnly = false
            FilterConstraint = FilterAll
            SelectedRuleId = firstRuleId
            IsScanning = false
            IsRequestingAi = false
            IsRenaming = false
            IsDockerBusy = false
            ErrorMessage = startupWarning
            HasScanned = false
            ScannedVideoCount = 0
            ActiveEnvironment = meta.EnvironmentName
            SettingsSavePath = meta.SavePath
            ApiKeySourcePath = meta.ApiKeySourcePath
            Candidates = []
            UndoStack = []
            Docker = initialDockerStatus
            Layout = Vertical
            SortCriterion = PathLengthDesc
            IsRuleManagerOpen = false
            EditingRule = None
            ConfirmDialog = None
            AiCancellationCts = None
            AiPipelineRunId = None
        }

        let checkDockerCmd =
            Cmd.OfAsync.perform
                (fun () -> ContainerController.checkStatus (ContainerController.defaultRuntime ()) None None (Directory.GetCurrentDirectory()))
                ()
                DockerStatusUpdated

        model, checkDockerCmd

    /// <summary>既定コンテキストで初期化する本番用エントリーポイント</summary>
    let init () : Model * Cmd<Msg> =
        initWithContext None

    /// <summary>指定パッチを設定ファイルへ部分保存し、保存先パスと失敗時エラーメッセージを返す</summary>
    let private saveSettingsPatchWithFeedback (preferredSavePath: string) (patch: SettingsPatch) : string * string option =
        let savePath = resolveEffectiveSavePath preferredSavePath
        let err =
            match Settings.savePatch savePath patch with
            | Ok () -> None
            | Error e -> Some (RenamerError.toMessage e)
        savePath, err

    /// <summary>設定パッチを部分保存し、成功時は設定更新と onSuccess、失敗時はロールバックと onFailure を適用する</summary>
    let private applySettingsPatch
        (model: Model)
        (patch: SettingsPatch)
        (applyToSettings: RenamerSettings -> RenamerSettings)
        (onSuccess: Model -> Model)
        (onFailure: Model -> Model)
        : Model * Cmd<Msg> =
        let savePath, errOpt = saveSettingsPatchWithFeedback model.SettingsSavePath patch
        match errOpt with
        | None ->
            let updatedModel =
                {
                    model with
                        Settings = applyToSettings model.Settings
                        ErrorMessage = None
                        SettingsSavePath = savePath
                }
                |> onSuccess
            updatedModel, Cmd.none
        | Some errMsg ->
            let rolledBackModel =
                {
                    model with
                        ErrorMessage = Some errMsg
                        SettingsSavePath = savePath
                }
                |> onFailure
            rolledBackModel, Cmd.none

    /// <summary>ルール変更を Patch Save し、成功時は状態反映、失敗時はルール一覧・選択ルールをロールバックする</summary>
    let private applyRulesPatch
        (model: Model)
        (updatedRules: NamingRule list)
        (onSuccess: Model -> Model)
        (onFailure: Model -> Model)
        : Model * Cmd<Msg> =
        applySettingsPatch
            model
            { SelectedModel = None; Rules = Some updatedRules }
            (fun s -> { s with Rules = updatedRules })
            onSuccess
            onFailure

    /// <summary>指定パスの候補のみを更新する共通ヘルパー</summary>
    let private updateCandidateByPath (targetPath: string) (updater: RenameProposal -> RenameProposal) (candidates: RenameProposal list) : RenameProposal list =
        candidates
        |> List.map (fun c -> if c.OriginalFullPath = targetPath then updater c else c)

    /// <summary>編集中の命名規則フィールドを更新する共通ヘルパー</summary>
    let private mapEditingRule (updater: NamingRule -> NamingRule) (model: Model) : Model * Cmd<Msg> =
        { model with EditingRule = model.EditingRule |> Option.map updater }, Cmd.none

    /// <summary>リネーム非同期コマンドを開始する共通ヘルパー</summary>
    let private startRename (restartContainer: bool) (model: Model) : Model * Cmd<Msg> =
        let selected = model.Candidates |> List.filter (fun c -> c.IsSelected)
        if List.isEmpty selected then
            { model with ErrorMessage = Some "リネーム対象が選択されていません。" }, Cmd.none
        else
            let cmd =
                Cmd.OfAsync.perform
                    (fun () -> async { return FileRenamer.executeRename selected })
                    ()
                    (fun res -> RenameCompleted (restartContainer, res))
            { model with IsRenaming = true; ErrorMessage = None }, cmd

    /// <summary>コンテキスト注入対応の状態更新処理（テストと本番 Elmish ループで同一ロジックを共有する）</summary>
    let updateWithContext (ctxOpt: ConfigLoadContext option) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
        match msg with
        | SettingsLoaded (Ok loaded) ->
            {
                model with
                    Settings = loaded
                    CurrentThreshold = loaded.PathLengthThreshold
                    SelectedRuleId = resolveSelectedRuleId model.SelectedRuleId loaded.Rules
            }, Cmd.none

        | SettingsLoaded (Error _) ->
            model, Cmd.none

        // 排他ガード: スキャン中またはリネーム中の設定再読込・入力割り込み・個別再試行を無視
        | ReloadSettings
        | TargetDirectoryChanged _
        | ThresholdChanged _
        | RuleSelected _
        | ModelSelected _
        | ToggleFilterNonJapaneseOnly _
        | SetConstraintFilter _
        | RetryCandidateAiProposal _ when model.IsScanning || model.IsRenaming ->
            model, Cmd.none

        | ReloadSettings ->
            let meta = loadConfigWithContext ctxOpt
            // ファイル側で targetDirectory が空（最小 JSON 等）なら画面上の選択フォルダを維持する
            let preservedTargetDir =
                if String.IsNullOrWhiteSpace(meta.Settings.TargetDirectory) then
                    model.Settings.TargetDirectory
                else
                    meta.Settings.TargetDirectory
            let mergedSettings = { meta.Settings with TargetDirectory = preservedTargetDir }
            let nextRuleId = resolveSelectedRuleId model.SelectedRuleId mergedSettings.Rules

            let nextMessage =
                if List.isEmpty meta.Warnings then
                    let fileName =
                        if String.IsNullOrWhiteSpace(meta.SavePath) then $"appsettings.{meta.EnvironmentName}.json"
                        else Path.GetFileName(meta.SavePath)
                    Some $"💡 設定ファイルを再読込しました ({fileName})"
                else
                    Some (String.Join("\n", meta.Warnings))

            {
                model with
                    Settings = mergedSettings
                    CurrentThreshold = mergedSettings.PathLengthThreshold
                    SelectedRuleId = nextRuleId
                    ActiveEnvironment = meta.EnvironmentName
                    SettingsSavePath = meta.SavePath
                    ApiKeySourcePath = meta.ApiKeySourcePath
                    ErrorMessage = nextMessage
            }, Cmd.none

        | ThresholdChanged newThreshold ->
            // 最重要: セッション内一時変更（設定ファイルへは保存しない）
            { model with CurrentThreshold = max 1 newThreshold }, Cmd.none

        | ToggleFilterNonJapaneseOnly isChecked ->
            { model with FilterNonJapaneseOnly = isChecked }, Cmd.none

        | SetConstraintFilter filter ->
            { model with FilterConstraint = filter }, Cmd.none

        | TargetDirectoryChanged newDir ->
            { model with Settings = { model.Settings with TargetDirectory = newDir } }, Cmd.none

        | RuleSelected ruleId ->
            { model with SelectedRuleId = ruleId }, Cmd.none

        | ModelSelected newModel ->
            if String.IsNullOrWhiteSpace(newModel) || newModel = model.Settings.SelectedModel then
                model, Cmd.none
            else
                applySettingsPatch
                    model
                    { SelectedModel = Some newModel; Rules = None }
                    (fun s -> { s with SelectedModel = newModel })
                    id
                    id

        // ワンアクション走査 & AI提案
        | ExecuteScanAndPropose ->
            let resetModel = resetAiPipelineState model
            if String.IsNullOrWhiteSpace(resetModel.Settings.TargetDirectory) then
                { resetModel with IsScanning = false; ErrorMessage = None }, Cmd.none
            else
                let cmd =
                    Cmd.OfAsync.perform
                        (fun () -> async { return FileScanner.scanLongPaths resetModel.Settings.TargetDirectory resetModel.CurrentThreshold resetModel.FilterNonJapaneseOnly })
                        ()
                        ScanCompleted
                { resetModel with IsScanning = true; ErrorMessage = None; Candidates = [] }, cmd

        | ScanCompleted (Ok candidates) ->
            let totalCount = candidates.Length
            if List.isEmpty candidates then
                let resetModel = resetAiPipelineState model
                {
                    resetModel with
                        IsScanning = false
                        HasScanned = true
                        ScannedVideoCount = totalCount
                        ErrorMessage = Some "閾値以上の危険な長パス動画ファイルは見つかりませんでした。"
                }, Cmd.none
            else
                let initialProposals =
                    candidates
                    |> List.map Proposal.createInitial
                    |> FileScanner.sortCandidates model.SortCriterion
                let sortedCandidates = alignCandidatesWithProposals candidates initialProposals
                let scannedBase = {
                    model with
                        IsScanning = false
                        HasScanned = true
                        ScannedVideoCount = totalCount
                        Candidates = initialProposals
                }
                startAiPipelineOrReset
                    scannedBase
                    sortedCandidates
                    (fun m -> { m with ErrorMessage = None })
                    (fun errMsg m -> { m with ErrorMessage = Some errMsg })

        | ScanCompleted (Error err) ->
            let resetModel = resetAiPipelineState model
            {
                resetModel with
                    IsScanning = false
                    HasScanned = true
                    ErrorMessage = Some (RenamerError.toMessage err)
            }, Cmd.none

        | CandidateAiProcessing (runId, fullPath) ->
            if model.AiPipelineRunId <> Some runId then
                model, Cmd.none
            else
                let updatedCandidates =
                    updateCandidateByPath fullPath (fun c -> { c with IsAiProcessing = true; AiComment = Some "（Web検索・LLM提案を実行中...）" }) model.Candidates
                { model with Candidates = updatedCandidates }, Cmd.none

        | CandidateAiProposed (runId, proposed) ->
            if model.AiPipelineRunId <> Some runId then
                model, Cmd.none
            else
                let updatedCandidates =
                    updateCandidateByPath
                        proposed.OriginalFullPath
                        (fun _ ->
                            {
                                proposed with
                                    IsAiProcessing = false
                                    IsAiProposed = (proposed.IsAiProposed && proposed.AiError.IsNone)
                            })
                        model.Candidates
                    |> FileScanner.sortCandidates model.SortCriterion
                { model with Candidates = updatedCandidates }, Cmd.none

        | AllAiProposalsCompleted runId ->
            if model.AiPipelineRunId <> Some runId then
                model, Cmd.none
            else
                let resetModel = resetAiPipelineState model
                { resetModel with Candidates = clearAllAiProcessing model.Candidates }, Cmd.none

        | CancelAiProposal ->
            let cancelledCandidates =
                model.Candidates
                |> List.map (fun c ->
                    if c.IsAiProcessing then
                        { c with IsAiProcessing = false; AiComment = Some "（AI提案が中止されました）" }
                    else c
                )
            let resetModel = resetAiPipelineState model
            { resetModel with Candidates = cancelledCandidates }, Cmd.none

        | RetryCandidateAiProposal targetPath ->
            match model.Candidates |> List.tryFind (fun c -> c.OriginalFullPath = targetPath) with
            | None ->
                model, Cmd.none
            | Some cand ->
                let scanCand: ScanCandidate = {
                    FullPath = cand.OriginalFullPath
                    FileName = cand.OriginalFileName
                    DirectoryPath = cand.DirectoryPath
                    PathLength = cand.OriginalLength
                    FileNameBytes = cand.OriginalFileNameBytes
                    Violation = cand.OriginalViolation
                    FileSizeBytes = 0L
                    LastWriteTime = cand.LastWriteTime
                }
                startAiPipelineOrReset
                    model
                    [ scanCand ]
                    (fun m ->
                        let updatedCandidates =
                            m.Candidates
                            |> List.map (fun c ->
                                if c.OriginalFullPath = targetPath then
                                    { c with IsAiProcessing = true; AiError = None }
                                elif c.IsAiProcessing then
                                    { c with IsAiProcessing = false }
                                else c)
                        { m with Candidates = updatedCandidates })
                    (fun errMsg m ->
                        { m with
                            Candidates = clearAllAiProcessing m.Candidates
                            ErrorMessage = Some errMsg })

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
            { model with Candidates = updateCandidateByPath fullPath (fun c -> { c with IsSelected = not c.IsSelected }) model.Candidates }, Cmd.none

        | SetCandidateSelect (fullPath, isSelected) ->
            { model with Candidates = updateCandidateByPath fullPath (fun c -> { c with IsSelected = isSelected }) model.Candidates }, Cmd.none

        | SelectAllCandidates selectAll ->
            let updated = model.Candidates |> List.map (fun c -> { c with IsSelected = selectAll })
            { model with Candidates = updated }, Cmd.none

        | ChangeSortCriterion newCriterion ->
            let sorted = FileScanner.sortCandidates newCriterion model.Candidates
            { model with SortCriterion = newCriterion; Candidates = sorted }, Cmd.none

        | UpdateProposedName (fullPath, newName) ->
            let updated =
                updateCandidateByPath
                    fullPath
                    (fun c ->
                        let newProposedPath = Path.Combine(c.DirectoryPath, newName)
                        { c with
                            ProposedFileName = newName
                            ProposedLength = newProposedPath.Length
                            ProposedFileNameBytes = Encoding.UTF8.GetByteCount(newName) })
                    model.Candidates
            { model with Candidates = updated }, Cmd.none

        // リネーム実行 & Undo
        | ExecuteRenameOnly -> startRename false model
        | ExecuteRenameAndRestart -> startRename true model

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
                        (fun () -> ContainerController.executeAction (ContainerController.defaultRuntime ()) None (Directory.GetCurrentDirectory()) "restart")
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
                                let! _ = ContainerController.executeAction (ContainerController.defaultRuntime ()) None (Directory.GetCurrentDirectory()) "restart"
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
                    (fun () -> ContainerController.checkStatus (ContainerController.defaultRuntime ()) None None (Directory.GetCurrentDirectory()))
                    ()
                    DockerStatusUpdated
            model, cmd

        | DockerStatusUpdated status ->
            { model with Docker = status; IsDockerBusy = false }, Cmd.none

        | ExecuteDockerAction action ->
            let cmd =
                Cmd.OfAsync.perform
                    (fun () -> ContainerController.executeAction (ContainerController.defaultRuntime ()) None (Directory.GetCurrentDirectory()) action)
                    ()
                    (fun res -> DockerCommandCompleted (action, res))
            { model with IsDockerBusy = true }, cmd

        | DockerCommandCompleted (_, Ok _) ->
            model, Cmd.ofMsg CheckDockerStatus
        | DockerCommandCompleted (action, Error (ContainerError (_, _, stderr))) ->
            { model with IsDockerBusy = false; ErrorMessage = Some ($"コンテナ コマンド '{action}' エラー: {stderr}") }, Cmd.ofMsg CheckDockerStatus
        | DockerCommandCompleted (action, Error (TimeoutError cmd)) ->
            { model with IsDockerBusy = false; ErrorMessage = Some ($"コンテナ コマンド '{cmd}' がタイムアウトしました。") }, Cmd.ofMsg CheckDockerStatus

        | DockerCommandCompleted (_, Error other) ->
            { model with IsDockerBusy = false; ErrorMessage = Some ($"Docker エラー: {other}") }, Cmd.ofMsg CheckDockerStatus

        // UI切り替え
        | SetLayoutMode mode ->
            { model with Layout = mode }, Cmd.none

        | OpenRuleManager ->
            { model with IsRuleManagerOpen = true; EditingRule = Some (createRuleTemplate model.Settings.Rules.Length) }, Cmd.none

        | CloseRuleManager ->
            { model with IsRuleManagerOpen = false; EditingRule = None }, Cmd.none

        | StartAddRule ->
            { model with EditingRule = Some (createRuleTemplate model.Settings.Rules.Length) }, Cmd.none

        | StartEditRule ruleId ->
            let targetOpt = model.Settings.Rules |> List.tryFind (fun r -> r.Id = ruleId)
            match targetOpt with
            | Some r -> { model with EditingRule = Some r }, Cmd.none
            | None -> model, Cmd.none

        | CancelEditRule ->
            { model with EditingRule = Some (createRuleTemplate model.Settings.Rules.Length) }, Cmd.none

        | UpdateEditingRuleName name -> mapEditingRule (fun r -> { r with Name = name }) model
        | UpdateEditingRulePattern pattern -> mapEditingRule (fun r -> { r with Pattern = pattern }) model
        | UpdateEditingRulePrompt prompt -> mapEditingRule (fun r -> { r with PromptInstruction = prompt }) model
        | UpdateEditingRuleWebSearch enabled -> mapEditingRule (fun r -> { r with EnableWebSearch = enabled }) model

        | SaveEditingRule ->
            match model.EditingRule with
            | None ->
                { model with ErrorMessage = Some "編集中の命名規則がありません。" }, Cmd.none
            | Some rule when String.IsNullOrWhiteSpace(rule.Id) ->
                { model with ErrorMessage = Some "ルールIDが不正です。" }, Cmd.none
            | Some rule when String.IsNullOrWhiteSpace(rule.Name) ->
                { model with ErrorMessage = Some "ルール名を入力してください。" }, Cmd.none
            | Some rule ->
                let trimmedId = rule.Id.Trim()
                let trimmedName = rule.Name.Trim()
                let trimmedPattern = if String.IsNullOrWhiteSpace(rule.Pattern) then "{Title}" else rule.Pattern.Trim()
                let trimmedPrompt = if String.IsNullOrWhiteSpace(rule.PromptInstruction) then "簡潔に短縮してください。" else rule.PromptInstruction.Trim()
                let normalizedRule = {
                    rule with
                        Id = trimmedId
                        Name = trimmedName
                        Pattern = trimmedPattern
                        PromptInstruction = trimmedPrompt
                }
                let isExisting = model.Settings.Rules |> List.exists (fun r -> r.Id = trimmedId)

                let updatedRules =
                    if isExisting then
                        model.Settings.Rules
                        |> List.map (fun r -> if r.Id = trimmedId then { normalizedRule with Order = r.Order } else r)
                    else
                        model.Settings.Rules @ [ { normalizedRule with Order = model.Settings.Rules.Length } ]

                applyRulesPatch
                    model
                    updatedRules
                    (fun saved ->
                        {
                            saved with
                                SelectedRuleId = trimmedId
                                EditingRule = Some (createRuleTemplate updatedRules.Length)
                        })
                    id

        | SaveRule rule ->
            if String.IsNullOrWhiteSpace(rule.Id)
               || String.IsNullOrWhiteSpace(rule.Name)
               || String.IsNullOrWhiteSpace(rule.Pattern)
               || String.IsNullOrWhiteSpace(rule.PromptInstruction) then
                { model with ErrorMessage = Some "命名規則の必須項目（ID・ルール名・パターン・プロンプト指示）を入力してください。" }, Cmd.none
            else
                let existingIdx = model.Settings.Rules |> List.tryFindIndex (fun r -> r.Id = rule.Id)
                let updatedRules =
                    match existingIdx with
                    | Some idx ->
                        model.Settings.Rules |> List.mapi (fun i r -> if i = idx then rule else r)
                    | None ->
                        model.Settings.Rules @ [ { rule with Order = model.Settings.Rules.Length } ]
                applyRulesPatch model updatedRules id id

        | RequestDeleteRule ruleId ->
            if model.Settings.Rules.Length <= 1 then
                { model with ErrorMessage = Some "最後の1件の命名規則は削除できません。" }, Cmd.none
            else
                let targetOpt = model.Settings.Rules |> List.tryFind (fun r -> r.Id = ruleId)
                match targetOpt with
                | Some targetRule ->
                    let dialog: DialogConfig = {
                        Title = "🗑 命名規則の削除"
                        Message = $"命名規則「{targetRule.Name}」を削除しますか？\nこの操作は取り消せません。"
                        ConfirmText = "削除する"
                        CancelText = "キャンセル"
                        OnConfirm = DeleteRule ruleId
                    }
                    { model with ConfirmDialog = Some dialog }, Cmd.none
                | None -> model, Cmd.none

        | DeleteRule ruleId ->
            if model.Settings.Rules.Length <= 1 then
                { model with ConfirmDialog = None; ErrorMessage = Some "最後の1件の命名規則は削除できません。" }, Cmd.none
            else
                let filteredRules =
                    model.Settings.Rules
                    |> List.filter (fun r -> r.Id <> ruleId)
                    |> List.mapi (fun idx r -> { r with Order = idx })
                let nextEditing =
                    match model.EditingRule with
                    | Some r when r.Id = ruleId -> Some (createRuleTemplate filteredRules.Length)
                    | other -> other
                let nextSelectedRuleId = resolveSelectedRuleId model.SelectedRuleId filteredRules
                applyRulesPatch
                    model
                    filteredRules
                    (fun saved ->
                        {
                            saved with
                                ConfirmDialog = None
                                EditingRule = nextEditing
                                SelectedRuleId = nextSelectedRuleId
                        })
                    (fun failed -> { failed with ConfirmDialog = None })

        | MoveRuleOrder (ruleId, direction) ->
            let rules = model.Settings.Rules
            let idxOpt = rules |> List.tryFindIndex (fun r -> r.Id = ruleId)
            match idxOpt with
            | Some idx when (direction = -1 && idx > 0) || (direction = 1 && idx < rules.Length - 1) ->
                let targetIdx = idx + direction
                let arr = rules |> List.toArray
                let temp = arr.[idx]
                arr.[idx] <- arr.[targetIdx]
                arr.[targetIdx] <- temp
                let reordered = arr |> Array.mapi (fun i r -> { r with Order = i }) |> Array.toList
                applyRulesPatch model reordered id id
            | _ ->
                model, Cmd.none

        | DismissError ->
            { model with ErrorMessage = None }, Cmd.none

        | DismissConfirm ->
            { model with ConfirmDialog = None }, Cmd.none

    /// <summary>既定コンテキストで状態更新を行う本番用エントリーポイント</summary>
    let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
        updateWithContext None msg model
