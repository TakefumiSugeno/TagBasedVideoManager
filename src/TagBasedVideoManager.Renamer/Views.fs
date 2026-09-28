namespace TagBasedVideoManager.Renamer

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Platform.Storage
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open TagBasedVideoManager.Renamer

module Views =

    // ==========================================
    // カラー・スタイル定数 (Windows 11 Fluent Dark / mockup.html 準拠)
    // ==========================================
    let private bgWindow = Color.Parse("#1a1a1a")       // bg-fluent-bg
    let private bgSurface = Color.Parse("#222222")      // bg-fluent-surface
    let private bgCard = Color.Parse("#2a2a2a")         // bg-fluent-card
    let private bgInput = Color.Parse("#18181b")        // bg-zinc-900
    let private bgBlack = Color.Parse("#09090b")        // bg-black/80
    let private borderFluent = Color.Parse("#383838")   // border-fluent-border
    let private borderZinc700 = Color.Parse("#3f3f46")  // border-zinc-700
    let private borderZinc800 = Color.Parse("#27272a")  // border-zinc-800
    let private textWhite = Color.Parse("#ffffff")      // text-fluent-text
    let private textSub = Color.Parse("#909090")        // text-fluent-subtext
    let private textZinc400 = Color.Parse("#a1a1aa")
    let private textZinc500 = Color.Parse("#71717a")

    // アクセント・ボタン色
    let private accentBlue = Color.Parse("#2563eb")     // bg-blue-600
    let private accentBlueHover = Color.Parse("#3b82f6")// bg-blue-500
    let private btnDark = Color.Parse("#27272a")        // bg-zinc-800
    let private btnPrimaryGradient = Color.Parse("#4f46e5") // Indigo-600

    // 危険・BEFORE
    let private bgBefore = Color.Parse("#3b1111")       // 暗赤背景
    let private borderBefore = Color.Parse("#7f1d1d")   // border-red-900
    let private badgeBgBefore = Color.Parse("#450a0a")  // bg-red-950
    let private textBeforeLabel = Color.Parse("#f87171")// text-red-400
    let private textBeforeFile = Color.Parse("#fecaca") // text-red-200

    // 安全・AFTER
    let private bgAfter = Color.Parse("#063327")        // 暗緑背景
    let private borderAfter = Color.Parse("#065f46")    // border-emerald-900
    let private badgeBgAfter = Color.Parse("#064e3b")   // bg-emerald-950
    let private borderAfterInput = Color.Parse("#059669")// border-emerald-600
    let private textAfterLabel = Color.Parse("#34d399") // text-emerald-400
    let private textAfterFile = Color.Parse("#6ee7b7")  // text-emerald-300

    // AIコメント・警告
    let private bgAiComment = Color.Parse("#361d06")    // bg-amber-950/40
    let private borderAiComment = Color.Parse("#78350f")// border-amber-900
    let private textAiComment = Color.Parse("#fcd34d")  // text-amber-300

    // Undoボタン
    let private bgUndo = Color.Parse("#451a03")         // bg-amber-950
    let private borderUndo = Color.Parse("#b45309")     // border-amber-700
    let private textUndo = Color.Parse("#fcd34d")       // text-amber-300

    // ==========================================
    // 1. Docker Status Controller (SECTION 1)
    // ==========================================
    let dockerStatusBar (model: Model) (dispatch: Msg -> unit) =
        let stateText, dotColor, badgeText, badgeBg, badgeBorder, badgeFg =
            match model.Docker.State with
            | Running ->
                let portInfo = if model.Docker.IsPortAccessible then " (Port 5620)" else " (Port 5620 NG)"
                "RUNNING", textAfterLabel, $"● RUNNING{portInfo}", badgeBgAfter, borderAfter, textAfterLabel
            | Stopped ->
                "STOPPED", textZinc500, "○ STOPPED", btnDark, borderZinc700, textZinc400
            | Restarting ->
                "RESTARTING", textAiComment, "🔄 RESTARTING", bgAiComment, borderAiComment, textAiComment
            | Unhealthy ->
                "UNHEALTHY", textBeforeLabel, "▲ UNHEALTHY (マウント不可: パス長超過を検出)", badgeBgBefore, borderBefore, textBeforeLabel
            | NotFound ->
                "NOT FOUND", textBeforeLabel, "✕ NOT FOUND", badgeBgBefore, borderBefore, textBeforeLabel

        Border.create [
            DockPanel.dock Dock.Top
            Border.background (SolidColorBrush bgSurface)
            Border.borderBrush (SolidColorBrush borderFluent)
            Border.borderThickness 1.0
            Border.cornerRadius 8.0
            Border.padding (12.0, 8.0)
            Border.margin (0.0, 0.0, 0.0, 10.0)
            Border.child (
                WrapPanel.create [
                    WrapPanel.orientation Orientation.Horizontal
                    WrapPanel.children [
                        // 左側: ステータス表示
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 10.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.margin (0.0, 0.0, 16.0, 4.0)
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text "🐳 Docker:"
                                    TextBlock.foreground (SolidColorBrush textSub)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.fontSize 12.0
                                ]
                                // 丸角ピルバッジ
                                Border.create [
                                    Border.background (SolidColorBrush badgeBg)
                                    Border.borderBrush (SolidColorBrush badgeBorder)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 12.0
                                    Border.padding (10.0, 3.0)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text badgeText
                                            TextBlock.foreground (SolidColorBrush badgeFg)
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.fontSize 11.0
                                        ]
                                    )
                                ]
                                TextBlock.create [
                                    TextBlock.text "tag-based-video-manager"
                                    TextBlock.foreground (SolidColorBrush textSub)
                                    TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                    TextBlock.fontSize 11.0
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]
                            ]
                        ]

                        // 右側: コントロールボタン群
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.margin (0.0, 0.0, 0.0, 4.0)
                            StackPanel.children [
                                Button.create [
                                    Button.content "▶ Start"
                                    Button.isEnabled (not model.IsDockerBusy && model.Docker.State <> Running)
                                    Button.background (SolidColorBrush btnDark)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.borderBrush (SolidColorBrush borderFluent)
                                    Button.borderThickness 1.0
                                    Button.cornerRadius 4.0
                                    Button.padding (10.0, 4.0)
                                    Button.fontSize 11.0
                                    Button.fontWeight FontWeight.Medium
                                    Button.onClick (fun _ -> dispatch (ExecuteDockerAction "up -d"))
                                ]
                                Button.create [
                                    Button.content "■ Stop"
                                    Button.isEnabled (not model.IsDockerBusy && model.Docker.State = Running)
                                    Button.background (SolidColorBrush btnDark)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.borderBrush (SolidColorBrush borderFluent)
                                    Button.borderThickness 1.0
                                    Button.cornerRadius 4.0
                                    Button.padding (10.0, 4.0)
                                    Button.fontSize 11.0
                                    Button.fontWeight FontWeight.Medium
                                    Button.onClick (fun _ -> dispatch (ExecuteDockerAction "stop"))
                                ]
                                Button.create [
                                    Button.content "🔄 Restart"
                                    Button.isEnabled (not model.IsDockerBusy)
                                    Button.background (SolidColorBrush accentBlue)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.cornerRadius 4.0
                                    Button.padding (12.0, 4.0)
                                    Button.fontSize 11.0
                                    Button.fontWeight FontWeight.Bold
                                    Button.onClick (fun _ -> dispatch (ExecuteDockerAction "restart tag-based-video-manager"))
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    let private pickFolder (dispatch: Msg -> unit) =
        let desktop =
            if box Application.Current <> null then
                match Application.Current.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as d -> Some d
                | _ -> None
            else None
        match desktop with
        | Some d when box d.MainWindow <> null ->
            task {
                let sp = d.MainWindow.StorageProvider
                let opt = FolderPickerOpenOptions()
                opt.Title <- "走査対象動画フォルダの選択"
                opt.AllowMultiple <- false
                let! results = sp.OpenFolderPickerAsync(opt)
                if results.Count > 0 then
                    let path = results.[0].Path.LocalPath
                    dispatch (TargetDirectoryChanged path)
            } |> ignore
        | _ -> ()

    let private copyToClipboard (text: string) =
        let desktop =
            if box Application.Current <> null then
                match Application.Current.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as d -> Some d
                | _ -> None
            else None
        match desktop with
        | Some d when box d.MainWindow <> null && box d.MainWindow.Clipboard <> null ->
            d.MainWindow.Clipboard.SetTextAsync(text) |> ignore
        | _ -> ()

    // ==========================================
    // 2. Configuration & Execution Bar (SECTION 2)
    // ==========================================
    let controlPanel (model: Model) (dispatch: Msg -> unit) =
        Border.create [
            DockPanel.dock Dock.Top
            Border.background (SolidColorBrush bgSurface)
            Border.borderBrush (SolidColorBrush borderFluent)
            Border.borderThickness 1.0
            Border.cornerRadius 8.0
            Border.padding 12.0
            Border.margin (0.0, 0.0, 0.0, 10.0)
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 10.0
                    StackPanel.children [
                        // Row 1: 対象フォルダ & 抽出基準 (一時変更・設定非保存)
                        WrapPanel.create [
                            WrapPanel.orientation Orientation.Horizontal
                            WrapPanel.children [
                                // 左側: フォルダ入力
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (0.0, 0.0, 16.0, 6.0)
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "対象フォルダ:"
                                            TextBlock.foreground (SolidColorBrush textSub)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontWeight FontWeight.SemiBold
                                            TextBlock.fontSize 12.0
                                        ]
                                        TextBox.create [
                                            TextBox.text model.Settings.TargetDirectory
                                            TextBox.height 30.0
                                            TextBox.width 220.0
                                            TextBox.verticalAlignment VerticalAlignment.Center
                                            TextBox.verticalContentAlignment VerticalAlignment.Center
                                            TextBox.background (SolidColorBrush bgInput)
                                            TextBox.foreground (SolidColorBrush textWhite)
                                            TextBox.fontFamily (FontFamily "Consolas, monospace")
                                            TextBox.borderBrush (SolidColorBrush borderFluent)
                                            TextBox.borderThickness 1.0
                                            TextBox.cornerRadius 4.0
                                            TextBox.padding (8.0, 4.0)
                                            TextBox.fontSize 11.0
                                            TextBox.onTextChanged (fun text -> dispatch (TargetDirectoryChanged text))
                                        ]
                                        Button.create [
                                            Button.content "参照..."
                                            Button.background (SolidColorBrush btnDark)
                                            Button.foreground (SolidColorBrush textWhite)
                                            Button.borderBrush (SolidColorBrush borderFluent)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (12.0, 4.0)
                                            Button.fontSize 11.0
                                            Button.onClick (fun _ -> pickFolder dispatch)
                                        ]
                                    ]
                                ]

                                // 右側: 抽出基準数値ボックス
                                Border.create [
                                    Border.background (SolidColorBrush bgInput)
                                    Border.borderBrush (SolidColorBrush borderZinc700)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding (8.0, 4.0)
                                    Border.margin (0.0, 0.0, 0.0, 6.0)
                                    Border.child (
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 5.0
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.children [
                                                TextBlock.create [
                                                    TextBlock.text "抽出基準 (パス長):"
                                                    TextBlock.foreground (SolidColorBrush textSub)
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                    TextBlock.fontWeight FontWeight.Medium
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "≧"
                                                    TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.fontSize 12.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                                TextBox.create [
                                                    TextBox.text (string model.CurrentThreshold)
                                                    TextBox.width 50.0
                                                    TextBox.height 26.0
                                                    TextBox.verticalAlignment VerticalAlignment.Center
                                                    TextBox.verticalContentAlignment VerticalAlignment.Center
                                                    TextBox.textAlignment TextAlignment.Center
                                                    TextBox.background (SolidColorBrush bgBlack)
                                                    TextBox.foreground (SolidColorBrush textAiComment)
                                                    TextBox.fontFamily (FontFamily "Consolas, monospace")
                                                    TextBox.fontWeight FontWeight.Bold
                                                    TextBox.borderBrush (SolidColorBrush borderZinc700)
                                                    TextBox.borderThickness 1.0
                                                    TextBox.cornerRadius 3.0
                                                    TextBox.padding (4.0, 0.0)
                                                    TextBox.fontSize 11.0
                                                    TextBox.onTextChanged (fun text ->
                                                        match Int32.TryParse(text) with
                                                        | true, v -> dispatch (ThresholdChanged v)
                                                        | _ -> ()
                                                    )
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "文字"
                                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "(※一時変更)"
                                                    TextBlock.foreground (SolidColorBrush textZinc500)
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                                // 該当件数
                                                TextBlock.create [
                                                    TextBlock.text $"| 該当: {model.Candidates.Length} 件"
                                                    TextBlock.foreground (SolidColorBrush textWhite)
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                    TextBlock.margin (6.0, 0.0, 0.0, 0.0)
                                                ]
                                            ]
                                        ]
                                    )
                                ]
                            ]
                        ]

                        // 区切り線
                        Border.create [
                            Border.height 1.0
                            Border.background (SolidColorBrush borderFluent)
                        ]

                        // Row 2: AIモデル, 命名規則 & PRIMARY ACTION BUTTON
                        WrapPanel.create [
                            WrapPanel.orientation Orientation.Horizontal
                            WrapPanel.children [
                                // モデル選択
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (0.0, 0.0, 14.0, 6.0)
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "AIモデル:"
                                            TextBlock.foreground (SolidColorBrush textSub)
                                            TextBlock.fontSize 11.0
                                            TextBlock.fontWeight FontWeight.SemiBold
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                        let standardModels = [
                                            "meta-llama/llama-3.3-70b-instruct:free"
                                            "google/gemini-2.0-flash-exp:free"
                                            "mistralai/mistral-small-24b-instruct-2501:free"
                                            "nvidia/nemotron-3-ultra-550b-a55b:free"
                                        ]
                                        let availableModels =
                                            let current = model.Settings.SelectedModel
                                            if not (String.IsNullOrWhiteSpace(current)) && not (List.contains current standardModels) then
                                                current :: standardModels
                                            else standardModels

                                        let selectedIdx =
                                            availableModels
                                            |> List.tryFindIndex (fun m -> m = model.Settings.SelectedModel)
                                            |> Option.defaultValue 0

                                        ComboBox.create [
                                            ComboBox.dataItems availableModels
                                            ComboBox.selectedIndex selectedIdx
                                            ComboBox.height 30.0
                                            ComboBox.fontSize 11.0
                                            ComboBox.minWidth 180.0
                                            ComboBox.maxWidth 320.0
                                            ComboBox.onSelectedIndexChanged (fun idx ->
                                                if idx >= 0 && idx < availableModels.Length then
                                                    dispatch (ModelSelected availableModels.[idx])
                                            )
                                        ]
                                    ]
                                ]

                                // 命名規則選択 & 管理ボタン
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (0.0, 0.0, 14.0, 6.0)
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "命名規則:"
                                            TextBlock.foreground (SolidColorBrush textSub)
                                            TextBlock.fontSize 11.0
                                            TextBlock.fontWeight FontWeight.SemiBold
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                        ComboBox.create [
                                            ComboBox.dataItems (
                                                model.Settings.Rules
                                                |> List.mapi (fun idx r -> if idx = 0 then $"★ {r.Name}" else r.Name)
                                            )
                                            ComboBox.selectedIndex (
                                                model.Settings.Rules
                                                |> List.tryFindIndex (fun r -> r.Id = model.SelectedRuleId)
                                                |> Option.defaultValue 0
                                            )
                                            ComboBox.height 30.0
                                            ComboBox.fontSize 11.0
                                            ComboBox.maxWidth 160.0
                                            ComboBox.onSelectedIndexChanged (fun idx ->
                                                if idx >= 0 && idx < model.Settings.Rules.Length then
                                                    dispatch (RuleSelected model.Settings.Rules.[idx].Id)
                                            )
                                        ]
                                        Button.create [
                                            Button.content "⚙ 管理..."
                                            Button.background (SolidColorBrush btnDark)
                                            Button.foreground (SolidColorBrush textWhite)
                                            Button.borderBrush (SolidColorBrush borderFluent)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (10.0, 4.0)
                                            Button.fontSize 11.0
                                            Button.onClick (fun _ -> dispatch OpenRuleManager)
                                        ]
                                    ]
                                ]

                                // メインアクションボタン
                                Button.create [
                                    let btnText =
                                        if model.Candidates.Length > 0 then "⚡ 再抽出 ＆ AI提案を再実行"
                                        else "🚀 リネーム対象抽出 ＆ AI提案を実行"
                                    Button.content btnText
                                    Button.isEnabled (not model.IsScanning && not model.IsRequestingAi && not model.IsRenaming)
                                    Button.background (SolidColorBrush btnPrimaryGradient)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.fontWeight FontWeight.Bold
                                    Button.fontSize 12.0
                                    Button.cornerRadius 4.0
                                    Button.padding (16.0, 7.0)
                                    Button.margin (0.0, 0.0, 0.0, 6.0)
                                    Button.onClick (fun _ -> dispatch ExecuteScanAndPropose)
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 3. BEFORE / AFTER Comparison Cards (SECTION 3)
    // ==========================================

    /// 上下並び (Vertical) - mockup.html MODE A
    let candidateCardVertical (index: int) (c: RenameProposal) (currentThreshold: int) (dispatch: Msg -> unit) =
        let isIncrease = c.ProposedLength > c.OriginalLength
        let isUnshortened = c.ProposedLength = c.OriginalLength
        let isDanger = c.ProposedLength >= currentThreshold
        let diff = c.OriginalLength - c.ProposedLength
        let reductionPercent =
            if c.OriginalLength > 0 then
                int (Math.Round((1.0 - (float c.ProposedLength / float c.OriginalLength)) * 100.0))
            else 0

        let badgeBg, badgeBorder, badgeText, badgeFg =
            if isIncrease then
                Color.Parse("#3f1d1d"), Color.Parse("#7f1d1d"), $"+{c.ProposedLength - c.OriginalLength}字 (増加)", Color.Parse("#fca5a5")
            elif isUnshortened then
                Color.Parse("#27272a"), Color.Parse("#3f3f46"), "±0字 (未短縮)", Color.Parse("#a1a1aa")
            else
                badgeBgAfter, borderAfter, $"-{diff}字 ({reductionPercent}%%短縮)", textAfterLabel

        let statusText, statusFg =
            if isIncrease then
                "⚠️ 文字数増加", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ 要短縮", Color.Parse("#f87171")
            elif isUnshortened then
                "⚠️ 未短縮", Color.Parse("#fbbf24")
            else
                "✓ 安全", textAfterLabel

        let afterBg = bgAfter

        let afterBorder =
            if isIncrease || isDanger then Color.Parse("#ef4444")
            else borderAfter

        let afterBadgeBg, afterBadgeBorder =
            if isIncrease || isDanger then Color.Parse("#450a0a"), Color.Parse("#b91c1c")
            else badgeBgAfter, borderAfter

        let afterLabelText, afterLabelFg =
            if isIncrease then
                (if isDanger then $"⚠️ AFTER ({c.ProposedLength}字 [危険 (増加)])" else $"⚠️ AFTER ({c.ProposedLength}字 [増加])"), Color.Parse("#f87171")
            elif isDanger then
                $"⚠️ AFTER ({c.ProposedLength}字 [危険])", Color.Parse("#f87171")
            elif isUnshortened && not c.IsAiProposed then
                $"AFTER ({c.ProposedLength}字 [未短縮])", Color.Parse("#a1a1aa")
            else
                $"AFTER ({c.ProposedLength}字 [安全])", textAfterLabel

        Border.create [
            Border.background (SolidColorBrush bgInput)
            Border.borderBrush (SolidColorBrush (if c.IsSelected then accentBlue else borderZinc800))
            Border.borderThickness (if c.IsSelected then 1.5 else 1.0)
            Border.cornerRadius 6.0
            Border.margin (2.0, 2.0, 10.0, 8.0)
            Border.padding 8.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 5.0
                    StackPanel.children [
                        // Line 1: Header / Checkbox / Folder & Metrics (完全DockPanel構成で親幅超過を根絶)
                        DockPanel.create [
                            DockPanel.children [
                                // 右側: 削減バッジ & ステータスバッジ
                                StackPanel.create [
                                    DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (8.0, 0.0, 0.0, 0.0)
                                    StackPanel.children [
                                        Border.create [
                                            Border.background (SolidColorBrush badgeBg)
                                            Border.borderBrush (SolidColorBrush badgeBorder)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 4.0
                                            Border.padding (6.0, 2.0)
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text badgeText
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontFamily (FontFamily "Yu Gothic UI, Segoe UI, sans-serif")
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush badgeFg)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        TextBlock.create [
                                            TextBlock.text statusText
                                            TextBlock.foreground (SolidColorBrush statusFg)
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.fontSize 10.0
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                    ]
                                ]

                                // 左側固定: チェックボックス
                                CheckBox.create [
                                    DockPanel.dock Dock.Left
                                    CheckBox.isChecked c.IsSelected
                                    CheckBox.onIsCheckedChanged (fun _ -> dispatch (ToggleCandidateSelect c.OriginalFullPath))
                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                    CheckBox.margin (0.0, 0.0, 8.0, 0.0)
                                ]

                                // 左側固定: #番号
                                TextBlock.create [
                                    DockPanel.dock Dock.Left
                                    TextBlock.text $"#{index + 1}"
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (0.0, 0.0, 8.0, 0.0)
                                ]

                                // 残余領域: フォルダパス (親幅内でトリミングされ親幅を突破しない)
                                SelectableTextBlock.create [
                                    TextBlock.text (c.DirectoryPath + "\\")
                                    TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground (SolidColorBrush textZinc500)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    TextBlock.textWrapping TextWrapping.NoWrap
                                ]
                            ]
                        ]

                        // Line 2: BEFORE (薄赤背景・行高さ 32px 統一・ラベル幅 140px 固定・親幅制約)
                        Border.create [
                            Border.background (SolidColorBrush bgBefore)
                            Border.borderBrush (SolidColorBrush borderBefore)
                            Border.borderThickness 1.0
                            Border.cornerRadius 4.0
                            Border.padding (6.0, 2.0)
                            Border.height 32.0
                            Border.clipToBounds true
                            Border.child (
                                DockPanel.create [
                                    DockPanel.children [
                                        // ラベル幅 160px 固定（AFTER行と垂直開始位置を一致させる）
                                        Border.create [
                                            DockPanel.dock Dock.Left
                                            Border.width 160.0
                                            Border.background (SolidColorBrush badgeBgBefore)
                                            Border.borderBrush (SolidColorBrush borderBefore)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.padding (6.0, 2.0)
                                            Border.margin (0.0, 0.0, 8.0, 0.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text $"BEFORE ({c.OriginalLength}字 [危険])"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        // BEFORE ファイル名: SelectableTextBlock で親幅残余領域に直接配置（親幅を突破しない）
                                        SelectableTextBlock.create [
                                            TextBlock.text c.OriginalFileName
                                            TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush textBeforeFile)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                            TextBlock.textWrapping TextWrapping.NoWrap
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // Line 3: AFTER (行高さ 32px 統一・ラベル幅 160px 固定・動的評価連動)
                        Border.create [
                            Border.background (SolidColorBrush afterBg)
                            Border.borderBrush (SolidColorBrush afterBorder)
                            Border.borderThickness (if isIncrease || isDanger then 1.5 else 1.0)
                            Border.cornerRadius 4.0
                            Border.padding (6.0, 2.0)
                            Border.height 32.0
                            Border.clipToBounds true
                            Border.child (
                                DockPanel.create [
                                    DockPanel.children [
                                        // ラベル幅 160px 固定（BEFORE行と垂直開始位置を一致させる）
                                        Border.create [
                                            DockPanel.dock Dock.Left
                                            Border.width 160.0
                                            Border.background (SolidColorBrush afterBadgeBg)
                                            Border.borderBrush (SolidColorBrush afterBadgeBorder)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.padding (6.0, 2.0)
                                            Border.margin (0.0, 0.0, 8.0, 0.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text afterLabelText
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush afterLabelFg)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        Border.create [
                                            DockPanel.dock Dock.Right
                                            Border.background (SolidColorBrush (if c.IsAiProposed then Color.Parse("#172554") else Color.Parse("#27272a")))
                                            Border.borderBrush (SolidColorBrush (if c.IsAiProposed then Color.Parse("#1e40af") else Color.Parse("#3f3f46")))
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.padding (6.0, 2.0)
                                            Border.margin (8.0, 0.0, 0.0, 0.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text (if c.IsAiProposed then "AI提案済" else "未提案")
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush (if c.IsAiProposed then Color.Parse("#60a5fa") else Color.Parse("#a1a1aa")))
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        // AFTER ファイル名: 編集可能 TextBox、等幅フォント、高さ 26px
                                        TextBox.create [
                                            TextBox.text c.ProposedFileName
                                            TextBox.height 26.0
                                            TextBox.verticalAlignment VerticalAlignment.Center
                                            TextBox.verticalContentAlignment VerticalAlignment.Center
                                            TextBox.background (SolidColorBrush bgBlack)
                                            TextBox.foreground (SolidColorBrush (if isIncrease || isDanger then textBeforeFile else textAfterFile))
                                            TextBox.fontFamily (FontFamily "Consolas, monospace")
                                            TextBox.borderBrush (SolidColorBrush (if isIncrease || isDanger then Color.Parse("#ef4444") else borderAfterInput))
                                            TextBox.borderThickness 1.0
                                            TextBox.cornerRadius 3.0
                                            TextBox.padding (6.0, 2.0)
                                            TextBox.fontSize 11.0
                                            TextBox.onTextChanged (fun newName ->
                                                dispatch (UpdateProposedName (c.OriginalFullPath, newName))
                                            )
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // Line 4: AI Comment Box (問題・補完があった時のみ表示。親幅超過を防ぐDockPanel構成)
                        match c.AiComment with
                        | Some comment when not (String.IsNullOrWhiteSpace(comment)) ->
                            Border.create [
                                Border.background (SolidColorBrush bgAiComment)
                                Border.borderBrush (SolidColorBrush borderAiComment)
                                Border.borderThickness 1.0
                                Border.cornerRadius 4.0
                                Border.padding (8.0, 4.0)
                                Border.clipToBounds true
                                Border.child (
                                    DockPanel.create [
                                        DockPanel.children [
                                            TextBlock.create [
                                                DockPanel.dock Dock.Left
                                                TextBlock.text "⚠️ AIコメント:"
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground (SolidColorBrush textAiComment)
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.margin (0.0, 0.0, 6.0, 0.0)
                                            ]
                                            SelectableTextBlock.create [
                                                TextBlock.text comment
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground (SolidColorBrush textAiComment)
                                                TextBlock.fontStyle FontStyle.Italic
                                                TextBlock.textWrapping TextWrapping.Wrap
                                            ]
                                        ]
                                    ]
                                )
                            ]
                        | _ -> ()
                    ]
                ]
            )
        ]

    /// 左右並び (Horizontal) - mockup.html MODE B
    let candidateCardHorizontal (index: int) (c: RenameProposal) (currentThreshold: int) (dispatch: Msg -> unit) =
        let isIncrease = c.ProposedLength > c.OriginalLength
        let isUnshortened = c.ProposedLength = c.OriginalLength
        let isDanger = c.ProposedLength >= currentThreshold
        let diff = c.OriginalLength - c.ProposedLength
        let reductionPercent =
            if c.OriginalLength > 0 then
                int (Math.Round((1.0 - (float c.ProposedLength / float c.OriginalLength)) * 100.0))
            else 0

        let badgeBg, badgeBorder, badgeText, badgeFg =
            if isIncrease then
                Color.Parse("#3f1d1d"), Color.Parse("#7f1d1d"), $"+{c.ProposedLength - c.OriginalLength}字 (増加)", Color.Parse("#fca5a5")
            elif isUnshortened then
                Color.Parse("#27272a"), Color.Parse("#3f3f46"), "±0字 (未短縮)", Color.Parse("#a1a1aa")
            else
                badgeBgAfter, borderAfter, $"-{diff}字 ({reductionPercent}%%短縮)", textAfterLabel

        let statusText, statusFg =
            if isIncrease then
                "⚠️ 文字数増加", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ 要短縮", Color.Parse("#f87171")
            elif isUnshortened then
                "⚠️ 未短縮", Color.Parse("#fbbf24")
            else
                "✓ 安全", textAfterLabel

        let afterBg = bgAfter

        let afterBorder =
            if isIncrease || isDanger then Color.Parse("#ef4444")
            else borderAfter

        let afterLabelTitle, afterLabelFg =
            if isIncrease then
                "⚠️ AFTER (文字数増加 / 編集可)", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ AFTER (要短縮 / 編集可)", Color.Parse("#f87171")
            elif isUnshortened && not c.IsAiProposed then
                "AFTER (未短縮 / 編集可)", Color.Parse("#a1a1aa")
            elif c.IsAiProposed then
                "✓ AFTER (AI提案 / 編集可)", textAfterLabel
            else
                "AFTER (未提案 / 編集可)", Color.Parse("#a1a1aa")

        let afterLengthText, afterLengthFg =
            if isIncrease then
                (if isDanger then $"{c.ProposedLength}字 [危険 (増加)]" else $"{c.ProposedLength}字 [増加]"), Color.Parse("#f87171")
            elif isDanger then
                $"{c.ProposedLength}字 [危険]", Color.Parse("#f87171")
            elif isUnshortened && not c.IsAiProposed then
                $"{c.ProposedLength}字 [未短縮]", Color.Parse("#a1a1aa")
            else
                $"{c.ProposedLength}字 [安全]", textAfterLabel

        Border.create [
            Border.background (SolidColorBrush bgInput)
            Border.borderBrush (SolidColorBrush (if c.IsSelected then accentBlue else borderZinc800))
            Border.borderThickness (if c.IsSelected then 1.5 else 1.0)
            Border.cornerRadius 6.0
            Border.margin (2.0, 2.0, 10.0, 8.0)
            Border.padding 8.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 6.0
                    StackPanel.children [
                        // Line 1: Header / Checkbox / Folder & Reduction (完全DockPanel構成)
                        DockPanel.create [
                            DockPanel.children [
                                StackPanel.create [
                                    DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.margin (8.0, 0.0, 0.0, 0.0)
                                    StackPanel.children [
                                        Border.create [
                                            Border.background (SolidColorBrush badgeBg)
                                            Border.borderBrush (SolidColorBrush badgeBorder)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 4.0
                                            Border.padding (6.0, 2.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text badgeText
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontFamily (FontFamily "Yu Gothic UI, Segoe UI, sans-serif")
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush badgeFg)
                                                ]
                                            )
                                        ]
                                        TextBlock.create [
                                            TextBlock.text statusText
                                            TextBlock.foreground (SolidColorBrush statusFg)
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.fontSize 10.0
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                    ]
                                ]
                                CheckBox.create [
                                    DockPanel.dock Dock.Left
                                    CheckBox.isChecked c.IsSelected
                                    CheckBox.onIsCheckedChanged (fun _ -> dispatch (ToggleCandidateSelect c.OriginalFullPath))
                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                    CheckBox.margin (0.0, 0.0, 8.0, 0.0)
                                ]
                                TextBlock.create [
                                    DockPanel.dock Dock.Left
                                    TextBlock.text $"#{index + 1}"
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.margin (0.0, 0.0, 8.0, 0.0)
                                ]
                                SelectableTextBlock.create [
                                    TextBlock.text c.DirectoryPath
                                    TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground (SolidColorBrush textZinc500)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    TextBlock.textWrapping TextWrapping.NoWrap
                                ]
                            ]
                        ]

                        // Line 2: 2列Grid (BEFORE vs AFTER)
                        Grid.create [
                            Grid.columnDefinitions "1*, 1*"
                            Grid.children [
                                // 左列: BEFORE
                                Border.create [
                                    Grid.column 0
                                    Border.background (SolidColorBrush bgBefore)
                                    Border.borderBrush (SolidColorBrush borderBefore)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding 8.0
                                    Border.margin (0.0, 0.0, 4.0, 0.0)
                                    Border.clipToBounds true
                                    Border.child (
                                        StackPanel.create [
                                            StackPanel.spacing 4.0
                                            StackPanel.children [
                                                DockPanel.create [
                                                    DockPanel.children [
                                                        TextBlock.create [
                                                            DockPanel.dock Dock.Left
                                                            TextBlock.text "● BEFORE (現ファイル)"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                        ]
                                                        TextBlock.create [
                                                            DockPanel.dock Dock.Right
                                                            TextBlock.text $"{c.OriginalLength}字 [危険]"
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                        ]
                                                    ]
                                                ]
                                                Border.create [
                                                    Border.background (SolidColorBrush bgBlack)
                                                    Border.cornerRadius 3.0
                                                    Border.padding 6.0
                                                    Border.child (
                                                        SelectableTextBlock.create [
                                                            TextBlock.text c.OriginalFileName
                                                            TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                                            TextBlock.fontSize 11.0
                                                            TextBlock.foreground (SolidColorBrush textBeforeFile)
                                                            TextBlock.textWrapping TextWrapping.Wrap
                                                        ]
                                                    )
                                                ]
                                            ]
                                        ]
                                    )
                                ]

                                // 右列: AFTER (動的評価連動)
                                Border.create [
                                    Grid.column 1
                                    Border.background (SolidColorBrush afterBg)
                                    Border.borderBrush (SolidColorBrush afterBorder)
                                    Border.borderThickness (if isIncrease || isDanger then 1.5 else 1.0)
                                    Border.cornerRadius 4.0
                                    Border.padding 8.0
                                    Border.margin (4.0, 0.0, 0.0, 0.0)
                                    Border.clipToBounds true
                                    Border.child (
                                        StackPanel.create [
                                            StackPanel.spacing 4.0
                                            StackPanel.children [
                                                DockPanel.create [
                                                    DockPanel.children [
                                                        TextBlock.create [
                                                            DockPanel.dock Dock.Left
                                                            TextBlock.text afterLabelTitle
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush afterLabelFg)
                                                        ]
                                                        TextBlock.create [
                                                            DockPanel.dock Dock.Right
                                                            TextBlock.text afterLengthText
                                                            TextBlock.fontSize 10.0
                                                            TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush afterLengthFg)
                                                        ]
                                                    ]
                                                ]
                                                TextBox.create [
                                                    TextBox.text c.ProposedFileName
                                                    TextBox.height 30.0
                                                    TextBox.background (SolidColorBrush bgBlack)
                                                    TextBox.foreground (SolidColorBrush (if isIncrease || isDanger then textBeforeFile else textAfterFile))
                                                    TextBox.fontFamily (FontFamily "Consolas, monospace")
                                                    TextBox.borderBrush (SolidColorBrush (if isIncrease || isDanger then Color.Parse("#ef4444") else borderAfterInput))
                                                    TextBox.borderThickness 1.0
                                                    TextBox.cornerRadius 3.0
                                                    TextBox.padding (6.0, 3.0)
                                                    TextBox.fontSize 11.0
                                                    TextBox.onTextChanged (fun newName ->
                                                        dispatch (UpdateProposedName (c.OriginalFullPath, newName))
                                                    )
                                                ]
                                                match c.AiComment with
                                                | Some comment when not (String.IsNullOrWhiteSpace(comment)) ->
                                                    Border.create [
                                                        Border.background (SolidColorBrush bgAiComment)
                                                        Border.borderBrush (SolidColorBrush borderAiComment)
                                                        Border.borderThickness 1.0
                                                        Border.cornerRadius 3.0
                                                        Border.padding (6.0, 3.0)
                                                        Border.clipToBounds true
                                                        Border.child (
                                                            DockPanel.create [
                                                                DockPanel.children [
                                                                    TextBlock.create [
                                                                        DockPanel.dock Dock.Left
                                                                        TextBlock.text "⚠️ AIコメント: "
                                                                        TextBlock.fontSize 10.0
                                                                        TextBlock.foreground (SolidColorBrush textAiComment)
                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                    ]
                                                                    SelectableTextBlock.create [
                                                                        TextBlock.text comment
                                                                        TextBlock.fontSize 10.0
                                                                        TextBlock.foreground (SolidColorBrush textAiComment)
                                                                        TextBlock.fontStyle FontStyle.Italic
                                                                        TextBlock.textWrapping TextWrapping.Wrap
                                                                    ]
                                                                ]
                                                            ]
                                                        )
                                                    ]
                                                | _ -> ()
                                            ]
                                        ]
                                    )
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 4. Comparison List Container (SECTION 3)
    // ==========================================
    let comparisonList (model: Model) (dispatch: Msg -> unit) =
        let selectedCount = model.Candidates |> List.filter (fun c -> c.IsSelected) |> List.length
        let allSelected = model.Candidates.Length > 0 && selectedCount = model.Candidates.Length

        Border.create [
            Border.background (SolidColorBrush bgSurface)
            Border.borderBrush (SolidColorBrush borderFluent)
            Border.borderThickness 1.0
            Border.cornerRadius 8.0
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        // ヘッダーツールバー
                        Border.create [
                            DockPanel.dock Dock.Top
                            Border.background (SolidColorBrush bgInput)
                            Border.borderBrush (SolidColorBrush borderFluent)
                            Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                            Border.padding (12.0, 8.0)
                            Border.child (
                                WrapPanel.create [
                                    WrapPanel.orientation Orientation.Horizontal
                                    WrapPanel.children [
                                        // 左側: 全選択チェックボックス + タイトル + 選択カウント
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 10.0
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.margin (0.0, 0.0, 16.0, 4.0)
                                            StackPanel.children [
                                                CheckBox.create [
                                                    CheckBox.isChecked allSelected
                                                    CheckBox.isEnabled (model.Candidates.Length > 0)
                                                    CheckBox.onIsCheckedChanged (fun _ ->
                                                        dispatch (SelectAllCandidates (not allSelected))
                                                    )
                                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                                ]
                                                TextBlock.create [
                                                    TextBlock.text "Before / After 対比確認"
                                                    TextBlock.foreground (SolidColorBrush textWhite)
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.fontSize 12.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                                TextBlock.create [
                                                    let countText =
                                                        if model.Candidates.Length > 0 then $"({selectedCount} / {model.Candidates.Length} 件選択中)"
                                                        else "(実行ボタンを押して抽出)"
                                                    TextBlock.text countText
                                                    TextBlock.foreground (SolidColorBrush textSub)
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            ]
                                        ]

                                        // 右側: ソート順指定 ＆ 表示形式切替 (上下並び / 左右並び)
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 12.0
                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                            StackPanel.margin (0.0, 0.0, 0.0, 4.0)
                                            StackPanel.children [
                                                // ソート順選択
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 6.0
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.children [
                                                        TextBlock.create [
                                                            TextBlock.text "ソート順:"
                                                            TextBlock.foreground (SolidColorBrush textSub)
                                                            TextBlock.fontSize 11.0
                                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                                        ]
                                                        let sortOptions = [
                                                            "パス長 (降順)", PathLengthDesc
                                                            "パス長 (昇順)", PathLengthAsc
                                                            "元ファイル名 (昇順)", FileNameAsc
                                                            "元ファイル名 (降順)", FileNameDesc
                                                            "更新日時 (新しい順)", LastModifiedDesc
                                                            "更新日時 (古い順)", LastModifiedAsc
                                                        ]
                                                        let currentSortText =
                                                            sortOptions
                                                            |> List.tryFind (fun (_, crit) -> crit = model.SortCriterion)
                                                            |> Option.map fst
                                                            |> Option.defaultValue "パス長 (降順)"

                                                        ComboBox.create [
                                                            ComboBox.dataItems (sortOptions |> List.map fst)
                                                            ComboBox.selectedItem currentSortText
                                                            ComboBox.onSelectedItemChanged (fun item ->
                                                                if item <> null then
                                                                    let itemStr = string item
                                                                    sortOptions
                                                                    |> List.tryFind (fun (name, _) -> name = itemStr)
                                                                    |> Option.iter (fun (_, crit) -> dispatch (ChangeSortCriterion crit))
                                                            )
                                                            ComboBox.fontSize 11.0
                                                            ComboBox.height 26.0
                                                            ComboBox.verticalAlignment VerticalAlignment.Center
                                                            ComboBox.background (SolidColorBrush btnDark)
                                                            ComboBox.foreground (SolidColorBrush textWhite)
                                                            ComboBox.borderBrush (SolidColorBrush borderZinc700)
                                                            ComboBox.borderThickness 1.0
                                                            ComboBox.cornerRadius 4.0
                                                            ComboBox.padding (6.0, 2.0)
                                                        ]
                                                    ]
                                                ]

                                                // 表示形式切替
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 6.0
                                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                                    StackPanel.children [
                                                        TextBlock.create [
                                                            TextBlock.text "表示形式:"
                                                            TextBlock.foreground (SolidColorBrush textSub)
                                                            TextBlock.fontSize 11.0
                                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                                        ]
                                                        Border.create [
                                                            Border.background (SolidColorBrush btnDark)
                                                            Border.borderBrush (SolidColorBrush borderZinc700)
                                                            Border.borderThickness 1.0
                                                            Border.cornerRadius 4.0
                                                            Border.padding 2.0
                                                            Border.child (
                                                                StackPanel.create [
                                                                    StackPanel.orientation Orientation.Horizontal
                                                                    StackPanel.spacing 2.0
                                                                    StackPanel.children [
                                                                        Button.create [
                                                                            Button.content "▤ 上下並び"
                                                                            Button.fontSize 10.0
                                                                            Button.padding (8.0, 3.0)
                                                                            Button.background (SolidColorBrush (if model.Layout = Vertical then accentBlue else Colors.Transparent))
                                                                            Button.foreground (SolidColorBrush (if model.Layout = Vertical then textWhite else textZinc400))
                                                                            Button.fontWeight (if model.Layout = Vertical then FontWeight.Bold else FontWeight.Normal)
                                                                            Button.cornerRadius 3.0
                                                                            Button.borderThickness 0.0
                                                                            Button.onClick (fun _ -> dispatch (SetLayoutMode Vertical))
                                                                        ]
                                                                        Button.create [
                                                                            Button.content "◫ 左右並び"
                                                                            Button.fontSize 10.0
                                                                            Button.padding (8.0, 3.0)
                                                                            Button.background (SolidColorBrush (if model.Layout = Horizontal then accentBlue else Colors.Transparent))
                                                                            Button.foreground (SolidColorBrush (if model.Layout = Horizontal then textWhite else textZinc400))
                                                                            Button.fontWeight (if model.Layout = Horizontal then FontWeight.Bold else FontWeight.Normal)
                                                                            Button.cornerRadius 3.0
                                                                            Button.borderThickness 0.0
                                                                            Button.onClick (fun _ -> dispatch (SetLayoutMode Horizontal))
                                                                        ]
                                                                    ]
                                                                ]
                                                            )
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            )
                        ]

                        // リスト本体 (ScrollViewer)
                        ScrollViewer.create [
                            ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                            ScrollViewer.padding 10.0
                            ScrollViewer.content (
                                if model.Candidates.IsEmpty then
                                    // 未実行時のプレースホルダー (mockup.html 準拠)
                                    StackPanel.create [
                                        StackPanel.horizontalAlignment HorizontalAlignment.Center
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.spacing 10.0
                                        StackPanel.margin (0.0, 40.0)
                                        StackPanel.children [
                                            TextBlock.create [
                                                TextBlock.text "📁"
                                                TextBlock.fontSize 40.0
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                TextBlock.foreground (SolidColorBrush textZinc500)
                                            ]
                                            TextBlock.create [
                                                TextBlock.text "上の「リネーム対象抽出 ＆ AI提案を実行」ボタンを押してください"
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground (SolidColorBrush textSub)
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center
                                            ]
                                            TextBlock.create [
                                                TextBlock.text $"設定済みのフォルダから指定文字数（初期値: {model.CurrentThreshold}字）以上の長パスファイルを検出し、AI提案を生成します"
                                                TextBlock.fontSize 11.0
                                                TextBlock.foreground (SolidColorBrush textZinc500)
                                                TextBlock.horizontalAlignment HorizontalAlignment.Center
                                            ]
                                        ]
                                    ]
                                else
                                    StackPanel.create [
                                        StackPanel.spacing 4.0
                                        StackPanel.children [
                                            for idx, c in List.indexed model.Candidates do
                                                match model.Layout with
                                                | Vertical -> candidateCardVertical idx c model.CurrentThreshold dispatch
                                                | Horizontal -> candidateCardHorizontal idx c model.CurrentThreshold dispatch
                                        ]
                                    ]
                            )
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 5. Footer Actions Bar (mockup.html 準拠)
    // ==========================================
    let footerActions (model: Model) (dispatch: Msg -> unit) =
        let selectedCount = model.Candidates |> List.filter (fun c -> c.IsSelected) |> List.length
        let hasUndo = not model.UndoStack.IsEmpty

        Border.create [
            DockPanel.dock Dock.Bottom
            Border.background (SolidColorBrush bgSurface)
            Border.borderBrush (SolidColorBrush borderFluent)
            Border.borderThickness 1.0
            Border.cornerRadius 8.0
            Border.padding (14.0, 10.0)
            Border.margin (0.0, 10.0, 0.0, 0.0)
            Border.child (
                WrapPanel.create [
                    WrapPanel.orientation Orientation.Horizontal
                    WrapPanel.children [
                        // 左側: 確定件数 & 安全バッジ & UNDOボタン
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.margin (0.0, 0.0, 16.0, 4.0)
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text "確定件数: "
                                    TextBlock.foreground (SolidColorBrush textSub)
                                    TextBlock.fontSize 12.0
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]
                                TextBlock.create [
                                    TextBlock.text $"{selectedCount} 件"
                                    TextBlock.foreground (SolidColorBrush textWhite)
                                    TextBlock.fontSize 14.0
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]
                                if model.Candidates.Length > 0 then
                                    let targets =
                                        let selected = model.Candidates |> List.filter (fun c -> c.IsSelected)
                                        if selected.IsEmpty then model.Candidates else selected
                                    let hasIncrease = targets |> List.exists (fun c -> c.ProposedLength > c.OriginalLength)
                                    let hasThresholdExceeded = targets |> List.exists (fun c -> c.ProposedLength >= model.CurrentThreshold)
                                    let hasUnshortened = targets |> List.exists (fun c -> c.ProposedLength = c.OriginalLength)

                                    let footerText, footerFg =
                                        if hasIncrease then
                                            "⚠️ 文字数増加ファイルが含まれています", Color.Parse("#f87171")
                                        elif hasThresholdExceeded then
                                            "⚠️ 要短縮ファイルが含まれています (基準超過)", Color.Parse("#f87171")
                                        elif hasUnshortened then
                                            "⚠️ 未短縮ファイルが含まれています", Color.Parse("#fbbf24")
                                        else
                                            "✓ 260文字制限を完全クリア (全件短縮済)", textAfterLabel

                                    TextBlock.create [
                                        TextBlock.text footerText
                                        TextBlock.foreground (SolidColorBrush footerFg)
                                        TextBlock.fontSize 11.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                    ]

                                // UNDO BUTTON (UndoStackがある時のみ表示)
                                if hasUndo then
                                    Button.create [
                                        Button.content $"↩ 直前のリネームを元に戻す (Undo: {model.UndoStack.Length}件)"
                                        Button.background (SolidColorBrush bgUndo)
                                        Button.borderBrush (SolidColorBrush borderUndo)
                                        Button.borderThickness 1.0
                                        Button.foreground (SolidColorBrush textUndo)
                                        Button.fontWeight FontWeight.Bold
                                        Button.fontSize 11.0
                                        Button.cornerRadius 4.0
                                        Button.padding (12.0, 6.0)
                                        Button.margin (8.0, 0.0, 0.0, 0.0)
                                        Button.onClick (fun _ -> dispatch RequestUndo)
                                    ]
                            ]
                        ]

                        // 右側: リネームボタン群
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 10.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.margin (0.0, 0.0, 0.0, 4.0)
                            StackPanel.children [
                                Button.create [
                                    Button.content "リネームのみ実行"
                                    Button.isEnabled (selectedCount > 0 && not model.IsRenaming)
                                    Button.background (SolidColorBrush btnDark)
                                    Button.borderBrush (SolidColorBrush borderFluent)
                                    Button.borderThickness 1.0
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.cornerRadius 4.0
                                    Button.padding (14.0, 7.0)
                                    Button.fontSize 11.0
                                    Button.fontWeight FontWeight.SemiBold
                                    Button.onClick (fun _ -> dispatch ExecuteRenameOnly)
                                ]
                                Button.create [
                                    Button.content "⚡ リネームしてコンテナ再起動"
                                    Button.isEnabled (selectedCount > 0 && not model.IsRenaming)
                                    Button.background (SolidColorBrush accentBlue)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.fontWeight FontWeight.Bold
                                    Button.cornerRadius 4.0
                                    Button.padding (16.0, 7.0)
                                    Button.fontSize 11.0
                                    Button.onClick (fun _ -> dispatch ExecuteRenameAndRestart)
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 6. エラーバナー & ダイアログ
    // ==========================================
    let errorBanner (model: Model) (dispatch: Msg -> unit) =
        match model.ErrorMessage with
        | Some msg ->
            let isInfo = msg.StartsWith("💡")
            let bannerBg = if isInfo then Color.Parse("#1e3a8a") else bgBefore
            let bannerBorder = if isInfo then Color.Parse("#3b82f6") else borderBefore
            let bannerFg = if isInfo then Color.Parse("#93c5fd") else textBeforeFile
            Border.create [
                DockPanel.dock Dock.Top
                Border.background (SolidColorBrush bannerBg)
                Border.borderBrush (SolidColorBrush bannerBorder)
                Border.borderThickness 1.0
                Border.cornerRadius 6.0
                Border.padding (12.0, 6.0)
                Border.margin (0.0, 0.0, 0.0, 8.0)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            StackPanel.create [
                                DockPanel.dock Dock.Right
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 6.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children [
                                    Button.create [
                                        Button.content "📋 コピー"
                                        Button.foreground (SolidColorBrush bannerFg)
                                        Button.background (SolidColorBrush (Color.FromArgb(50uy, 255uy, 255uy, 255uy)))
                                        Button.borderBrush (SolidColorBrush bannerBorder)
                                        Button.borderThickness 1.0
                                        Button.cornerRadius 4.0
                                        Button.padding (8.0, 2.0)
                                        Button.fontSize 10.0
                                        Button.fontWeight FontWeight.Medium
                                        Button.onClick (fun _ -> copyToClipboard msg)
                                    ]
                                    Button.create [
                                        Button.content "✕"
                                        Button.foreground (SolidColorBrush bannerFg)
                                        Button.background (SolidColorBrush Colors.Transparent)
                                        Button.borderThickness 0.0
                                        Button.padding (6.0, 2.0)
                                        Button.onClick (fun _ -> dispatch DismissError)
                                    ]
                                ]
                            ]
                            SelectableTextBlock.create [
                                TextBlock.text msg
                                TextBlock.foreground (SolidColorBrush bannerFg)
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.fontWeight FontWeight.Medium
                                TextBlock.fontSize 11.0
                                TextBlock.textWrapping TextWrapping.Wrap
                            ]
                        ]
                    ]
                )
            ]
        | None -> Border.create [ Border.isVisible false; DockPanel.dock Dock.Top ]

    let confirmDialog (model: Model) (dispatch: Msg -> unit) =
        match model.ConfirmDialog with
        | Some dialog ->
            Border.create [
                Border.background (SolidColorBrush (Color.FromArgb(180uy, 0uy, 0uy, 0uy)))
                Border.child (
                    Border.create [
                        Border.background (SolidColorBrush bgSurface)
                        Border.borderBrush (SolidColorBrush borderFluent)
                        Border.borderThickness 1.0
                        Border.cornerRadius 8.0
                        Border.padding 20.0
                        Border.width 440.0
                        Border.horizontalAlignment HorizontalAlignment.Center
                        Border.verticalAlignment VerticalAlignment.Center
                        Border.child (
                            StackPanel.create [
                                StackPanel.spacing 14.0
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text dialog.Title
                                        TextBlock.foreground (SolidColorBrush textWhite)
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.fontSize 14.0
                                    ]
                                    TextBlock.create [
                                        TextBlock.text dialog.Message
                                        TextBlock.foreground (SolidColorBrush textSub)
                                        TextBlock.fontSize 12.0
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.horizontalAlignment HorizontalAlignment.Right
                                        StackPanel.spacing 10.0
                                        StackPanel.children [
                                            Button.create [
                                                Button.content dialog.CancelText
                                                Button.background (SolidColorBrush btnDark)
                                                Button.borderBrush (SolidColorBrush borderFluent)
                                                Button.borderThickness 1.0
                                                Button.foreground (SolidColorBrush textWhite)
                                                Button.cornerRadius 4.0
                                                Button.padding (14.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.onClick (fun _ -> dispatch DismissConfirm)
                                            ]
                                            Button.create [
                                                Button.content dialog.ConfirmText
                                                Button.background (SolidColorBrush accentBlue)
                                                Button.foreground (SolidColorBrush textWhite)
                                                Button.cornerRadius 4.0
                                                Button.padding (14.0, 6.0)
                                                Button.fontWeight FontWeight.Bold
                                                Button.fontSize 11.0
                                                Button.onClick (fun _ -> dispatch dialog.OnConfirm)
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        )
                    ]
                )
            ]
        | None -> Border.create [ Border.isVisible false ]

    // ==========================================
    // 7. 命名規則マネージャーモーダル
    // ==========================================
    let ruleManagerModal (model: Model) (dispatch: Msg -> unit) =
        if not model.IsRuleManagerOpen then
            Border.create [ Border.isVisible false ]
        else
            Border.create [
                Border.background (SolidColorBrush (Color.FromArgb(180uy, 0uy, 0uy, 0uy)))
                Border.child (
                    Border.create [
                        Border.background (SolidColorBrush bgSurface)
                        Border.borderBrush (SolidColorBrush borderFluent)
                        Border.borderThickness 1.0
                        Border.cornerRadius 8.0
                        Border.padding 20.0
                        Border.width 640.0
                        Border.maxHeight 560.0
                        Border.horizontalAlignment HorizontalAlignment.Center
                        Border.verticalAlignment VerticalAlignment.Center
                        Border.child (
                            DockPanel.create [
                                DockPanel.children [
                                    // ヘッダー
                                    DockPanel.create [
                                        DockPanel.dock Dock.Top
                                        DockPanel.margin (0.0, 0.0, 0.0, 14.0)
                                        DockPanel.children [
                                            Button.create [
                                                DockPanel.dock Dock.Right
                                                Button.content "✕"
                                                Button.background (SolidColorBrush Colors.Transparent)
                                                Button.borderThickness 0.0
                                                Button.foreground (SolidColorBrush textSub)
                                                Button.fontSize 14.0
                                                Button.padding (6.0, 2.0)
                                                Button.onClick (fun _ -> dispatch CloseRuleManager)
                                            ]
                                            StackPanel.create [
                                                StackPanel.spacing 4.0
                                                StackPanel.children [
                                                    TextBlock.create [
                                                        TextBlock.text "⚙ 命名規則マネージャー"
                                                        TextBlock.foreground (SolidColorBrush textWhite)
                                                        TextBlock.fontWeight FontWeight.Bold
                                                        TextBlock.fontSize 15.0
                                                    ]
                                                    TextBlock.create [
                                                        TextBlock.text "AIが使用する命名プロンプト規則の優先度並び替え・追加・削除を行います。先頭のルールが既定値になります。"
                                                        TextBlock.foreground (SolidColorBrush textSub)
                                                        TextBlock.fontSize 11.0
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]

                                    // フッター: 閉じるボタン
                                    StackPanel.create [
                                        DockPanel.dock Dock.Bottom
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.horizontalAlignment HorizontalAlignment.Right
                                        StackPanel.margin (0.0, 14.0, 0.0, 0.0)
                                        StackPanel.children [
                                            Button.create [
                                                Button.content "閉じる"
                                                Button.background (SolidColorBrush btnDark)
                                                Button.borderBrush (SolidColorBrush borderFluent)
                                                Button.borderThickness 1.0
                                                Button.foreground (SolidColorBrush textWhite)
                                                Button.cornerRadius 4.0
                                                Button.padding (16.0, 6.0)
                                                Button.fontSize 11.0
                                                Button.fontWeight FontWeight.SemiBold
                                                Button.onClick (fun _ -> dispatch CloseRuleManager)
                                            ]
                                        ]
                                    ]

                                    // ルール一覧 (ScrollViewer)
                                    ScrollViewer.create [
                                        ScrollViewer.content (
                                            StackPanel.create [
                                                StackPanel.spacing 8.0
                                                StackPanel.children [
                                                    for idx, rule in List.indexed model.Settings.Rules do
                                                        Border.create [
                                                            Border.background (SolidColorBrush bgCard)
                                                            Border.borderBrush (SolidColorBrush (if idx = 0 then accentBlue else borderZinc700))
                                                            Border.borderThickness (if idx = 0 then 1.5 else 1.0)
                                                            Border.cornerRadius 6.0
                                                            Border.padding 10.0
                                                            Border.child (
                                                                DockPanel.create [
                                                                    DockPanel.children [
                                                                        // 操作ボタン群 (右側): 上へ / 下へ / 削除
                                                                        StackPanel.create [
                                                                            DockPanel.dock Dock.Right
                                                                            StackPanel.orientation Orientation.Horizontal
                                                                            StackPanel.spacing 6.0
                                                                            StackPanel.verticalAlignment VerticalAlignment.Center
                                                                            StackPanel.children [
                                                                                Button.create [
                                                                                    Button.content "▲"
                                                                                    Button.isEnabled (idx > 0)
                                                                                    Button.background (SolidColorBrush btnDark)
                                                                                    Button.borderBrush (SolidColorBrush borderZinc700)
                                                                                    Button.borderThickness 1.0
                                                                                    Button.foreground (SolidColorBrush textWhite)
                                                                                    Button.cornerRadius 3.0
                                                                                    Button.padding (8.0, 4.0)
                                                                                    Button.fontSize 10.0
                                                                                    Button.onClick (fun _ -> dispatch (MoveRuleOrder (rule.Id, -1)))
                                                                                ]
                                                                                Button.create [
                                                                                    Button.content "▼"
                                                                                    Button.isEnabled (idx < model.Settings.Rules.Length - 1)
                                                                                    Button.background (SolidColorBrush btnDark)
                                                                                    Button.borderBrush (SolidColorBrush borderZinc700)
                                                                                    Button.borderThickness 1.0
                                                                                    Button.foreground (SolidColorBrush textWhite)
                                                                                    Button.cornerRadius 3.0
                                                                                    Button.padding (8.0, 4.0)
                                                                                    Button.fontSize 10.0
                                                                                    Button.onClick (fun _ -> dispatch (MoveRuleOrder (rule.Id, 1)))
                                                                                ]
                                                                                Button.create [
                                                                                    Button.content "🗑"
                                                                                    Button.isEnabled (model.Settings.Rules.Length > 1)
                                                                                    Button.background (SolidColorBrush bgBefore)
                                                                                    Button.borderBrush (SolidColorBrush borderBefore)
                                                                                    Button.borderThickness 1.0
                                                                                    Button.foreground (SolidColorBrush textBeforeLabel)
                                                                                    Button.cornerRadius 3.0
                                                                                    Button.padding (8.0, 4.0)
                                                                                    Button.fontSize 11.0
                                                                                    Button.onClick (fun _ -> dispatch (DeleteRule rule.Id))
                                                                                ]
                                                                            ]
                                                                        ]

                                                                        // ルール情報 (左側)
                                                                        StackPanel.create [
                                                                            StackPanel.spacing 4.0
                                                                            StackPanel.children [
                                                                                StackPanel.create [
                                                                                    StackPanel.orientation Orientation.Horizontal
                                                                                    StackPanel.spacing 8.0
                                                                                    StackPanel.children [
                                                                                        if idx = 0 then
                                                                                            Border.create [
                                                                                                Border.background (SolidColorBrush (Color.Parse("#172554")))
                                                                                                Border.borderBrush (SolidColorBrush (Color.Parse("#1e40af")))
                                                                                                Border.borderThickness 1.0
                                                                                                Border.cornerRadius 3.0
                                                                                                Border.padding (6.0, 1.0)
                                                                                                Border.child (
                                                                                                    TextBlock.create [
                                                                                                        TextBlock.text "★ 既定ルール"
                                                                                                        TextBlock.fontSize 10.0
                                                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                                                        TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                                    ]
                                                                                                )
                                                                                            ]
                                                                                        TextBlock.create [
                                                                                            TextBlock.text rule.Name
                                                                                            TextBlock.foreground (SolidColorBrush textWhite)
                                                                                            TextBlock.fontWeight FontWeight.Bold
                                                                                            TextBlock.fontSize 12.0
                                                                                        ]
                                                                                    ]
                                                                                ]
                                                                                TextBlock.create [
                                                                                    TextBlock.text $"パターン: {rule.Pattern}"
                                                                                    TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                    TextBlock.fontFamily (FontFamily "Consolas, monospace")
                                                                                    TextBlock.fontSize 11.0
                                                                                ]
                                                                                TextBlock.create [
                                                                                    TextBlock.text rule.PromptInstruction
                                                                                    TextBlock.foreground (SolidColorBrush textSub)
                                                                                    TextBlock.fontSize 10.0
                                                                                    TextBlock.textWrapping TextWrapping.Wrap
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                ]
                                                            )
                                                        ]
                                                ]
                                            ]
                                        )
                                    ]
                                ]
                            ]
                        )
                    ]
                )
            ]

    // ==========================================
    // メインビュー (Windows 11 Fluent Dark レイアウト)
    // ==========================================
    let view (model: Model) (dispatch: Msg -> unit) =
        Grid.create [
            Grid.background (SolidColorBrush bgWindow)
            Grid.children [
                // メインコンテンツ領域
                Border.create [
                    Border.padding 12.0
                    Border.child (
                        DockPanel.create [
                            DockPanel.children [
                                errorBanner model dispatch
                                dockerStatusBar model dispatch
                                controlPanel model dispatch
                                footerActions model dispatch
                                comparisonList model dispatch
                            ]
                        ]
                    )
                ]
                // 命名規則マネージャーモーダル
                ruleManagerModal model dispatch
                // 確認ダイアログ
                confirmDialog model dispatch
            ]
        ]
