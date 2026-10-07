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
open Avalonia.Interactivity
open TagBasedVideoManager.Renamer

module Views =

    let private getCheckBoxValue (e: RoutedEventArgs) : bool option =
        match e.Source with
        | :? ToggleButton as tb ->
            Some (tb.IsChecked.HasValue && tb.IsChecked.Value)
        | _ -> None

    let private monoFontFamily = FontFamily("Cascadia Mono, Consolas, Meiryo UI, Yu Gothic UI, monospace")
    let private emojiFontFamily = FontFamily("Segoe UI Emoji, Segoe UI Symbol, Cascadia Mono, Meiryo UI, sans-serif")

    let private splitFileName (fileName: string) : string * string =
        if String.IsNullOrEmpty(fileName) then ("", "")
        else
            let ext = System.IO.Path.GetExtension(fileName)
            let baseName = System.IO.Path.GetFileNameWithoutExtension(fileName)
            (baseName, ext)

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
                                                // 縦区切り線
                                                Border.create [
                                                    Border.width 1.0
                                                    Border.height 14.0
                                                    Border.background (SolidColorBrush borderZinc700)
                                                    Border.margin (4.0, 0.0, 4.0, 0.0)
                                                    Border.verticalAlignment VerticalAlignment.Center
                                                ]
                                                // 日本語除外抽出チェックボックス
                                                CheckBox.create [
                                                    CheckBox.content "ファイル名に日本語を含まないもののみ抽出"
                                                    CheckBox.isChecked model.FilterNonJapaneseOnly
                                                    CheckBox.foreground (SolidColorBrush textSub)
                                                    CheckBox.fontSize 11.0
                                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                                    CheckBox.onIsCheckedChanged (fun e ->
                                                        match getCheckBoxValue e with
                                                        | Some isChecked when isChecked <> model.FilterNonJapaneseOnly ->
                                                            dispatch (ToggleFilterNonJapaneseOnly isChecked)
                                                        | _ -> ()
                                                    )
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
                                        let availableModels =
                                            if String.IsNullOrWhiteSpace(model.Settings.SelectedModel) then []
                                            else [ model.Settings.SelectedModel ]

                                        let selectedIdx = 0

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

                                // メインアクションボタン (AI提案中も常時活性化で新条件即時リスタート可能)
                                Button.create [
                                    let btnText =
                                        if model.IsRequestingAi then "⟳ 新条件で再実行"
                                        elif model.Candidates.Length > 0 then "⚡ 再抽出 ＆ AI提案を再実行"
                                        else "⚡ 抽出 ＆ AI提案を実行"
                                    Button.content btnText
                                    Button.isEnabled (not model.IsScanning && not model.IsRenaming)
                                    Button.background (SolidColorBrush btnPrimaryGradient)
                                    Button.foreground (SolidColorBrush textWhite)
                                    Button.fontWeight FontWeight.Bold
                                    Button.fontSize 12.0
                                    Button.cornerRadius 4.0
                                    Button.padding (16.0, 7.0)
                                    Button.margin (0.0, 0.0, 6.0, 6.0)
                                    Button.onClick (fun _ -> dispatch ExecuteScanAndPropose)
                                ]

                                // 中止ボタン (常時配置・AI提案中のみ活性化)
                                Button.create [
                                    Button.content "⏹ 中止"
                                    Button.isEnabled model.IsRequestingAi
                                    Button.background (SolidColorBrush (if model.IsRequestingAi then badgeBgBefore else btnDark))
                                    Button.borderBrush (SolidColorBrush (if model.IsRequestingAi then borderBefore else borderFluent))
                                    Button.borderThickness 1.0
                                    Button.foreground (SolidColorBrush (if model.IsRequestingAi then textBeforeLabel else textZinc500))
                                    Button.fontWeight FontWeight.Bold
                                    Button.fontSize 12.0
                                    Button.cornerRadius 4.0
                                    Button.padding (12.0, 7.0)
                                    Button.margin (0.0, 0.0, 0.0, 6.0)
                                    Button.onClick (fun _ -> dispatch CancelAiProposal)
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
            if c.IsAiProcessing then
                "🌐 検索中", Color.Parse("#60a5fa")
            elif isIncrease then
                "⚠️ 文字数増加", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ 要短縮", Color.Parse("#f87171")
            elif isUnshortened && not c.IsAiProposed then
                "⚠️ 未短縮", Color.Parse("#fbbf24")
            else
                "✓ 安全", textAfterLabel

        let afterBg = bgAfter

        let afterBorder =
            if isIncrease || isDanger then Color.Parse("#ef4444")
            else borderAfter

        let afterBadgeBg, afterBadgeBorder =
            if c.IsAiProcessing then Color.Parse("#172554"), Color.Parse("#1e40af")
            elif isIncrease || isDanger then Color.Parse("#450a0a"), Color.Parse("#b91c1c")
            elif not c.IsAiProposed then Color.Parse("#27272a"), Color.Parse("#3f3f46")
            else badgeBgAfter, borderAfter

        let afterLabelText, afterLabelFg =
            if c.IsAiProcessing then
                "AFTER (処理中...)", Color.Parse("#93c5fd")
            elif isIncrease then
                (if isDanger then $"AFTER ({c.ProposedLength}字 [危険])" else $"AFTER ({c.ProposedLength}字 [増加])"), Color.Parse("#f87171")
            elif isDanger then
                $"AFTER ({c.ProposedLength}字 [危険])", Color.Parse("#f87171")
            elif not c.IsAiProposed then
                $"AFTER ({c.ProposedLength}字 [未短縮])", Color.Parse("#a1a1aa")
            else
                $"AFTER ({c.ProposedLength}字 [安全])", textAfterLabel

        let rightBadgeText, rightBadgeBg, rightBadgeBorder, rightBadgeFg =
            if c.IsAiProcessing then
                "処理中", Color.Parse("#172554"), Color.Parse("#1e40af"), Color.Parse("#93c5fd")
            elif c.IsAiProposed then
                "AI提案済", Color.Parse("#172554"), Color.Parse("#1e40af"), Color.Parse("#60a5fa")
            else
                "未提案", Color.Parse("#27272a"), Color.Parse("#3f3f46"), Color.Parse("#a1a1aa")

        let commentBoxBg, commentBoxBorder, commentLabelText, commentFg, commentText =
            if c.IsAiProcessing then
                Color.Parse("#172554"), Color.Parse("#1e40af"), "🌐 AI COMMENT:", Color.Parse("#93c5fd"), "（DuckDuckGo (ddgs) より作品情報を検索中、またはLLMが命名理由を構成中...）"
            elif isDanger || isIncrease then
                let txt = c.AiComment |> Option.defaultValue "（文字数基準を超過しているため短縮が必要です）"
                bgAiComment, borderAiComment, "⚠️ AI COMMENT:", textAiComment, txt
            elif c.IsAiProposed then
                let txt = c.AiComment |> Option.defaultValue "（命名規則に基づき短縮しました）"
                Color.Parse("#18181b"), Color.Parse("#27272a"), "💬 AI COMMENT:", Color.Parse("#d4d4d8"), txt
            else
                let txt = c.AiComment |> Option.defaultValue "（AI提案の開始を待機しています...）"
                Color.Parse("#18181b"), Color.Parse("#27272a"), "💬 AI COMMENT:", Color.Parse("#a1a1aa"), txt

        let (origBaseName, origExt) = splitFileName c.OriginalFileName
        let (proposedBaseName, proposedExt) = splitFileName c.ProposedFileName
        let displayExt = if String.IsNullOrEmpty(proposedExt) then origExt else proposedExt

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
                                    CheckBox.onIsCheckedChanged (fun e ->
                                        match getCheckBoxValue e with
                                        | Some isChecked when isChecked <> c.IsSelected ->
                                            dispatch (SetCandidateSelect (c.OriginalFullPath, isChecked))
                                        | _ -> ()
                                    )
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
                                    TextBlock.fontFamily monoFontFamily
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground (SolidColorBrush textZinc500)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    TextBlock.textWrapping TextWrapping.NoWrap
                                ]
                            ]
                        ]

                        // Line 2: BEFORE (薄赤背景・行高さ 32px 統一・ラベル幅 160px 固定・親幅制約)
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
                                        // 右端スペーサー: AFTER行の「AI提案済/未提案」バッジ(width 64px + margin 8px)と幅を完全一致
                                        Border.create [
                                            DockPanel.dock Dock.Right
                                            Border.width 64.0
                                            Border.height 26.0
                                            Border.margin (8.0, 0.0, 0.0, 0.0)
                                        ]
                                        // 右側: 拡張子ラベル（ラベル項目として独立表示）
                                        Border.create [
                                            DockPanel.dock Dock.Right
                                            Border.background (SolidColorBrush (Color.Parse("#27272a")))
                                            Border.borderBrush (SolidColorBrush (Color.Parse("#3f3f46")))
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.height 26.0
                                            Border.padding (6.0, 0.0)
                                            Border.margin (6.0, 0.0, 0.0, 0.0)
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text origExt
                                                    TextBlock.fontFamily monoFontFamily
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        // 中央残余: BEFORE ファイル名本体（拡張子なし・黒背景枠・高さ26px・上下中央揃え）
                                        Border.create [
                                            Border.height 26.0
                                            Border.background (SolidColorBrush bgBlack)
                                            Border.borderBrush (SolidColorBrush borderZinc800)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.padding (6.0, 0.0)
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                SelectableTextBlock.create [
                                                    TextBlock.text origBaseName
                                                    TextBlock.fontFamily monoFontFamily
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.foreground (SolidColorBrush textBeforeFile)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                                    TextBlock.textWrapping TextWrapping.NoWrap
                                                ]
                                            )
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
                                        // 右端: AI提案済 / 未提案 バッジ (width 64px)
                                        Border.create [
                                            DockPanel.dock Dock.Right
                                            Border.width 64.0
                                            Border.height 26.0
                                            Border.background (SolidColorBrush rightBadgeBg)
                                            Border.borderBrush (SolidColorBrush rightBadgeBorder)
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.padding (4.0, 0.0)
                                            Border.margin (8.0, 0.0, 0.0, 0.0)
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text rightBadgeText
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush rightBadgeFg)
                                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        // 右側: 拡張子ラベル（ラベル項目として独立表示）
                                        Border.create [
                                            DockPanel.dock Dock.Right
                                            Border.background (SolidColorBrush (Color.Parse("#27272a")))
                                            Border.borderBrush (SolidColorBrush (Color.Parse("#3f3f46")))
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 3.0
                                            Border.height 26.0
                                            Border.padding (6.0, 0.0)
                                            Border.margin (6.0, 0.0, 0.0, 0.0)
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text displayExt
                                                    TextBlock.fontFamily monoFontFamily
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                ]
                                            )
                                        ]
                                        // 中央残余: 処理中アニメーションバー または AFTER ファイル名 TextBox
                                        if c.IsAiProcessing then
                                            Border.create [
                                                Border.height 26.0
                                                Border.background (SolidColorBrush (Color.Parse("#09090b")))
                                                Border.borderBrush (SolidColorBrush (Color.Parse("#3b82f6")))
                                                Border.borderThickness 1.0
                                                Border.cornerRadius 3.0
                                                Border.padding (8.0, 0.0)
                                                Border.verticalAlignment VerticalAlignment.Center
                                                Border.child (
                                                    StackPanel.create [
                                                        StackPanel.orientation Orientation.Horizontal
                                                        StackPanel.spacing 6.0
                                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                                        StackPanel.children [
                                                            TextBlock.create [
                                                                TextBlock.text "⠋"
                                                                TextBlock.fontSize 11.0
                                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                TextBlock.fontWeight FontWeight.Bold
                                                            ]
                                                            TextBlock.create [
                                                                TextBlock.text "🌐 DuckDuckGo (ddgs) 検索・LLM提案を実行中..."
                                                                TextBlock.fontFamily monoFontFamily
                                                                TextBlock.fontSize 11.0
                                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#93c5fd")))
                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                            ]
                                                        ]
                                                    ]
                                                )
                                            ]
                                        else
                                            TextBox.create [
                                                TextBox.text proposedBaseName
                                                TextBox.height 26.0
                                                TextBox.verticalAlignment VerticalAlignment.Center
                                                TextBox.verticalContentAlignment VerticalAlignment.Center
                                                TextBox.background (SolidColorBrush bgBlack)
                                                TextBox.foreground (SolidColorBrush (if isIncrease || isDanger then textBeforeFile else textAfterFile))
                                                TextBox.fontFamily monoFontFamily
                                                TextBox.borderBrush (SolidColorBrush (if isIncrease || isDanger then Color.Parse("#ef4444") else borderAfterInput))
                                                TextBox.borderThickness 1.0
                                                TextBox.cornerRadius 3.0
                                                TextBox.padding (6.0, 0.0)
                                                TextBox.fontSize 11.0
                                                TextBox.onTextChanged (fun newBaseName ->
                                                    if newBaseName <> proposedBaseName then
                                                        dispatch (UpdateProposedName (c.OriginalFullPath, newBaseName + displayExt))
                                                )
                                            ]
                                    ]
                                ]
                            )
                        ]

                        // Line 4: AI Comment Box (3行固定レイアウト・常時表示)
                        Border.create [
                            Border.background (SolidColorBrush commentBoxBg)
                            Border.borderBrush (SolidColorBrush commentBoxBorder)
                            Border.borderThickness 1.0
                            Border.cornerRadius 4.0
                            Border.padding (8.0, 4.0)
                            Border.clipToBounds true
                            Border.child (
                                DockPanel.create [
                                    DockPanel.children [
                                        TextBlock.create [
                                            DockPanel.dock Dock.Left
                                            TextBlock.text commentLabelText
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush commentFg)
                                            TextBlock.fontWeight FontWeight.Bold
                                            TextBlock.margin (0.0, 0.0, 6.0, 0.0)
                                        ]
                                        SelectableTextBlock.create [
                                            TextBlock.text commentText
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush commentFg)
                                            TextBlock.fontStyle FontStyle.Italic
                                            TextBlock.textWrapping TextWrapping.Wrap
                                        ]
                                    ]
                                ]
                            )
                        ]
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
            if c.IsAiProcessing then
                "🌐 検索中", Color.Parse("#60a5fa")
            elif isIncrease then
                "⚠️ 文字数増加", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ 要短縮", Color.Parse("#f87171")
            elif isUnshortened && not c.IsAiProposed then
                "⚠️ 未短縮", Color.Parse("#fbbf24")
            else
                "✓ 安全", textAfterLabel

        let afterBg = bgAfter

        let afterBorder =
            if isIncrease || isDanger then Color.Parse("#ef4444")
            else borderAfter

        let afterLabelTitle, afterLabelFg =
            if c.IsAiProcessing then
                "AFTER (処理中...)", Color.Parse("#93c5fd")
            elif isIncrease then
                "⚠️ AFTER (文字数増加 / 編集可)", Color.Parse("#f87171")
            elif isDanger then
                "⚠️ AFTER (要短縮 / 編集可)", Color.Parse("#f87171")
            elif not c.IsAiProposed then
                "AFTER (未提案 / 編集可)", Color.Parse("#a1a1aa")
            else
                "✓ AFTER (AI提案 / 編集可)", textAfterLabel

        let afterLengthText, afterLengthFg =
            if c.IsAiProcessing then
                "処理中...", Color.Parse("#93c5fd")
            elif isIncrease then
                (if isDanger then $"{c.ProposedLength}字 [危険 (増加)]" else $"{c.ProposedLength}字 [増加]"), Color.Parse("#f87171")
            elif isDanger then
                $"{c.ProposedLength}字 [危険]", Color.Parse("#f87171")
            elif not c.IsAiProposed then
                $"{c.ProposedLength}字 [未提案]", Color.Parse("#a1a1aa")
            else
                $"{c.ProposedLength}字 [安全]", textAfterLabel

        let (origBaseName, origExt) = splitFileName c.OriginalFileName
        let (proposedBaseName, proposedExt) = splitFileName c.ProposedFileName
        let displayExt = if String.IsNullOrEmpty(proposedExt) then origExt else proposedExt

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
                                    CheckBox.onIsCheckedChanged (fun e ->
                                        match getCheckBoxValue e with
                                        | Some isChecked when isChecked <> c.IsSelected ->
                                            dispatch (SetCandidateSelect (c.OriginalFullPath, isChecked))
                                        | _ -> ()
                                    )
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
                                    TextBlock.fontFamily monoFontFamily
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
                                                            TextBlock.fontFamily monoFontFamily
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                        ]
                                                    ]
                                                ]
                                                DockPanel.create [
                                                    DockPanel.children [
                                                        // 右側: 拡張子ラベル
                                                        Border.create [
                                                            DockPanel.dock Dock.Right
                                                            Border.background (SolidColorBrush (Color.Parse("#27272a")))
                                                            Border.borderBrush (SolidColorBrush (Color.Parse("#3f3f46")))
                                                            Border.borderThickness 1.0
                                                            Border.cornerRadius 3.0
                                                            Border.height 28.0
                                                            Border.padding (6.0, 0.0)
                                                            Border.margin (6.0, 0.0, 0.0, 0.0)
                                                            Border.verticalAlignment VerticalAlignment.Center
                                                            Border.child (
                                                                TextBlock.create [
                                                                    TextBlock.text origExt
                                                                    TextBlock.fontFamily monoFontFamily
                                                                    TextBlock.fontSize 11.0
                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                                ]
                                                            )
                                                        ]
                                                        // 残余: ファイル名本体
                                                        Border.create [
                                                            Border.height 28.0
                                                            Border.background (SolidColorBrush bgBlack)
                                                            Border.borderBrush (SolidColorBrush borderZinc800)
                                                            Border.borderThickness 1.0
                                                            Border.cornerRadius 3.0
                                                            Border.padding (6.0, 0.0)
                                                            Border.verticalAlignment VerticalAlignment.Center
                                                            Border.child (
                                                                SelectableTextBlock.create [
                                                                    TextBlock.text origBaseName
                                                                    TextBlock.fontFamily monoFontFamily
                                                                    TextBlock.fontSize 11.0
                                                                    TextBlock.foreground (SolidColorBrush textBeforeFile)
                                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                                                    TextBlock.textWrapping TextWrapping.NoWrap
                                                                ]
                                                            )
                                                        ]
                                                    ]
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
                                                            TextBlock.fontFamily monoFontFamily
                                                            TextBlock.fontWeight FontWeight.Bold
                                                            TextBlock.foreground (SolidColorBrush afterLengthFg)
                                                        ]
                                                    ]
                                                ]
                                                DockPanel.create [
                                                    DockPanel.children [
                                                        // 右側: 拡張子ラベル
                                                        Border.create [
                                                            DockPanel.dock Dock.Right
                                                            Border.background (SolidColorBrush (Color.Parse("#27272a")))
                                                            Border.borderBrush (SolidColorBrush (Color.Parse("#3f3f46")))
                                                            Border.borderThickness 1.0
                                                            Border.cornerRadius 3.0
                                                            Border.height 28.0
                                                            Border.padding (6.0, 0.0)
                                                            Border.margin (6.0, 0.0, 0.0, 0.0)
                                                            Border.verticalAlignment VerticalAlignment.Center
                                                            Border.child (
                                                                TextBlock.create [
                                                                    TextBlock.text displayExt
                                                                    TextBlock.fontFamily monoFontFamily
                                                                    TextBlock.fontSize 11.0
                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                    TextBlock.foreground (SolidColorBrush textZinc400)
                                                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                                                ]
                                                            )
                                                        ]
                                                        // 残余: 処理中アニメーションバー または ファイル名本体 TextBox
                                                        if c.IsAiProcessing then
                                                            Border.create [
                                                                Border.height 28.0
                                                                Border.background (SolidColorBrush (Color.Parse("#09090b")))
                                                                Border.borderBrush (SolidColorBrush (Color.Parse("#3b82f6")))
                                                                Border.borderThickness 1.0
                                                                Border.cornerRadius 3.0
                                                                Border.padding (8.0, 0.0)
                                                                Border.verticalAlignment VerticalAlignment.Center
                                                                Border.child (
                                                                    StackPanel.create [
                                                                        StackPanel.orientation Orientation.Horizontal
                                                                        StackPanel.spacing 6.0
                                                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                                                        StackPanel.children [
                                                                            TextBlock.create [
                                                                                TextBlock.text "⠋"
                                                                                TextBlock.fontSize 11.0
                                                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                TextBlock.fontWeight FontWeight.Bold
                                                                            ]
                                                                            TextBlock.create [
                                                                                TextBlock.text "🌐 DuckDuckGo (ddgs) 検索・LLM提案を実行中..."
                                                                                TextBlock.fontFamily monoFontFamily
                                                                                TextBlock.fontSize 11.0
                                                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#93c5fd")))
                                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                                            ]
                                                                        ]
                                                                    ]
                                                                )
                                                            ]
                                                        else
                                                            TextBox.create [
                                                                TextBox.text proposedBaseName
                                                                TextBox.height 28.0
                                                                TextBox.verticalAlignment VerticalAlignment.Center
                                                                TextBox.verticalContentAlignment VerticalAlignment.Center
                                                                TextBox.background (SolidColorBrush bgBlack)
                                                                TextBox.foreground (SolidColorBrush (if isIncrease || isDanger then textBeforeFile else textAfterFile))
                                                                TextBox.fontFamily monoFontFamily
                                                                TextBox.borderBrush (SolidColorBrush (if isIncrease || isDanger then Color.Parse("#ef4444") else borderAfterInput))
                                                                TextBox.borderThickness 1.0
                                                                TextBox.cornerRadius 3.0
                                                                TextBox.padding (6.0, 0.0)
                                                                TextBox.fontSize 11.0
                                                                TextBox.onTextChanged (fun newBaseName ->
                                                                    if newBaseName <> proposedBaseName then
                                                                        dispatch (UpdateProposedName (c.OriginalFullPath, newBaseName + displayExt))
                                                                )
                                                            ]
                                                    ]
                                                ]

                                                // AI Comment Box (常時表示)
                                                let hCommentBoxBg, hCommentBoxBorder, hCommentLabelText, hCommentFg, hCommentText =
                                                    if c.IsAiProcessing then
                                                        Color.Parse("#172554"), Color.Parse("#1e40af"), "🌐 AI COMMENT:", Color.Parse("#93c5fd"), "（DuckDuckGo (ddgs) より作品情報を検索中、またはLLMが命名理由を構成中...）"
                                                    elif isDanger || isIncrease then
                                                        let txt = c.AiComment |> Option.defaultValue "（文字数基準を超過しているため短縮が必要です）"
                                                        bgAiComment, borderAiComment, "⚠️ AI COMMENT:", textAiComment, txt
                                                    elif c.IsAiProposed then
                                                        let txt = c.AiComment |> Option.defaultValue "（命名規則に基づき短縮しました）"
                                                        Color.Parse("#18181b"), Color.Parse("#27272a"), "💬 AI COMMENT:", Color.Parse("#d4d4d8"), txt
                                                    else
                                                        let txt = c.AiComment |> Option.defaultValue "（AI提案の開始を待機しています...）"
                                                        Color.Parse("#18181b"), Color.Parse("#27272a"), "💬 AI COMMENT:", Color.Parse("#a1a1aa"), txt

                                                Border.create [
                                                    Border.background (SolidColorBrush hCommentBoxBg)
                                                    Border.borderBrush (SolidColorBrush hCommentBoxBorder)
                                                    Border.borderThickness 1.0
                                                    Border.cornerRadius 3.0
                                                    Border.padding (6.0, 3.0)
                                                    Border.clipToBounds true
                                                    Border.child (
                                                        DockPanel.create [
                                                            DockPanel.children [
                                                                TextBlock.create [
                                                                    DockPanel.dock Dock.Left
                                                                    TextBlock.text hCommentLabelText
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.foreground (SolidColorBrush hCommentFg)
                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                    TextBlock.margin (0.0, 0.0, 6.0, 0.0)
                                                                ]
                                                                SelectableTextBlock.create [
                                                                    TextBlock.text hCommentText
                                                                    TextBlock.fontSize 10.0
                                                                    TextBlock.foreground (SolidColorBrush hCommentFg)
                                                                    TextBlock.fontStyle FontStyle.Italic
                                                                    TextBlock.textWrapping TextWrapping.Wrap
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
                                                    CheckBox.onIsCheckedChanged (fun e ->
                                                        match getCheckBoxValue e with
                                                        | Some isChecked when isChecked <> allSelected ->
                                                            dispatch (SelectAllCandidates isChecked)
                                                        | _ -> ()
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
                                        StackPanel.margin (0.0, 0.0, 0.0, 36.0)
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
                                if model.IsRequestingAi then
                                    let completedCount = model.Candidates |> List.filter (fun c -> c.IsAiProposed) |> List.length
                                    Border.create [
                                        Border.background (SolidColorBrush (Color.Parse("#172554")))
                                        Border.borderBrush (SolidColorBrush (Color.Parse("#1e40af")))
                                        Border.borderThickness 1.0
                                        Border.cornerRadius 4.0
                                        Border.padding (8.0, 3.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (
                                            TextBlock.create [
                                                TextBlock.text $"🤖 AI提案中... ({completedCount}/{model.Candidates.Length}件完了)"
                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                TextBlock.fontSize 11.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                            ]
                                        )
                                    ]
                                elif model.Candidates.Length > 0 then
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

                        // 右側: リネームボタン群 (AI提案中は非活性)
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 10.0
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.margin (0.0, 0.0, 0.0, 4.0)
                            StackPanel.children [
                                let isRenameEnabled = selectedCount > 0 && not model.IsRenaming && not model.IsRequestingAi && not model.IsScanning
                                Button.create [
                                    Button.content "リネームのみ実行"
                                    Button.isEnabled isRenameEnabled
                                    Button.background (SolidColorBrush btnDark)
                                    Button.borderBrush (SolidColorBrush borderFluent)
                                    Button.borderThickness 1.0
                                    Button.foreground (SolidColorBrush (if isRenameEnabled then textWhite else textZinc500))
                                    Button.cornerRadius 4.0
                                    Button.padding (14.0, 7.0)
                                    Button.fontSize 11.0
                                    Button.fontWeight FontWeight.SemiBold
                                    Button.onClick (fun _ -> dispatch ExecuteRenameOnly)
                                ]
                                Button.create [
                                    Button.content "⚡ リネームしてコンテナ再起動"
                                    Button.isEnabled isRenameEnabled
                                    Button.background (SolidColorBrush (if isRenameEnabled then accentBlue else btnDark))
                                    Button.borderBrush (SolidColorBrush (if isRenameEnabled then accentBlue else borderFluent))
                                    Button.foreground (SolidColorBrush (if isRenameEnabled then textWhite else textZinc500))
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
    // 7. 命名規則マネージャーモーダル (左右2ペイン構成)
    // ==========================================
    let ruleManagerModal (model: Model) (dispatch: Msg -> unit) =
        if not model.IsRuleManagerOpen then
            Border.create [ Border.isVisible false ]
        else
            let curRule =
                model.EditingRule
                |> Option.defaultValue {
                    Id = Guid.NewGuid().ToString("N")
                    Name = ""
                    Pattern = "{Code}_{Summary}_{Actor}"
                    PromptInstruction = ""
                    Order = model.Settings.Rules.Length
                    EnableWebSearch = true
                }

            let isEditing =
                match model.EditingRule with
                | Some r -> model.Settings.Rules |> List.exists (fun existing -> existing.Id = r.Id)
                | None -> false

            Border.create [
                Border.background (SolidColorBrush (Color.FromArgb(180uy, 0uy, 0uy, 0uy)))
                Border.child (
                    Border.create [
                        Border.background (SolidColorBrush bgSurface)
                        Border.borderBrush (SolidColorBrush borderFluent)
                        Border.borderThickness 1.0
                        Border.cornerRadius 10.0
                        Border.width 960.0
                        Border.height 680.0
                        Border.maxWidth 960.0
                        Border.maxHeight 680.0
                        Border.horizontalAlignment HorizontalAlignment.Center
                        Border.verticalAlignment VerticalAlignment.Center
                        Border.child (
                            DockPanel.create [
                                DockPanel.children [
                                    // ヘッダー (Dock.Top)
                                    DockPanel.create [
                                        DockPanel.dock Dock.Top
                                        DockPanel.margin (16.0, 14.0, 16.0, 12.0)
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
                                                    StackPanel.create [
                                                        StackPanel.orientation Orientation.Horizontal
                                                        StackPanel.spacing 8.0
                                                        StackPanel.children [
                                                            TextBlock.create [
                                                                TextBlock.text "⚙ 命名規則マネージャー"
                                                                TextBlock.foreground (SolidColorBrush textWhite)
                                                                TextBlock.fontWeight FontWeight.Bold
                                                                TextBlock.fontSize 15.0
                                                            ]
                                                            Border.create [
                                                                Border.background (SolidColorBrush (Color.Parse("#172554")))
                                                                Border.borderBrush (SolidColorBrush (Color.Parse("#1e40af")))
                                                                Border.borderThickness 1.0
                                                                Border.cornerRadius 4.0
                                                                Border.padding (6.0, 1.0)
                                                                Border.child (
                                                                    TextBlock.create [
                                                                        TextBlock.text "左右2ペイン構成"
                                                                        TextBlock.fontSize 10.0
                                                                        TextBlock.fontWeight FontWeight.SemiBold
                                                                        TextBlock.foreground (SolidColorBrush (Color.Parse("#93c5fd")))
                                                                    ]
                                                                )
                                                            ]
                                                        ]
                                                    ]
                                                    TextBlock.create [
                                                        TextBlock.text "AIが使用する命名プロンプト規則の並び替え・追加・編集を行います。先頭のルールが既定値になります。"
                                                        TextBlock.foreground (SolidColorBrush textSub)
                                                        TextBlock.fontSize 11.0
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]

                                    // 全体フッター (Dock.Bottom): 閉じるボタン
                                    Border.create [
                                        DockPanel.dock Dock.Bottom
                                        Border.borderBrush (SolidColorBrush borderZinc800)
                                        Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                                        Border.padding (16.0, 10.0)
                                        Border.child (
                                            StackPanel.create [
                                                StackPanel.orientation Orientation.Horizontal
                                                StackPanel.horizontalAlignment HorizontalAlignment.Right
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
                                        )
                                    ]

                                    // ボディ (左右2ペイン Grid: "360, *")
                                    Grid.create [
                                        Grid.columnDefinitions "360, *"
                                        Grid.children [
                                            // ----------------------------------------------------
                                            // 左ペイン: ルール一覧
                                            // ----------------------------------------------------
                                            Border.create [
                                                Grid.column 0
                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                Border.borderThickness (0.0, 1.0, 1.0, 0.0)
                                                Border.background (SolidColorBrush (Color.Parse("#131315")))
                                                Border.child (
                                                    DockPanel.create [
                                                        DockPanel.children [
                                                            // 左ペインヘッダー
                                                            Border.create [
                                                                DockPanel.dock Dock.Top
                                                                Border.padding (12.0, 8.0)
                                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                                Border.background (SolidColorBrush bgSurface)
                                                                Border.child (
                                                                    DockPanel.create [
                                                                        DockPanel.children [
                                                                            TextBlock.create [
                                                                                DockPanel.dock Dock.Right
                                                                                TextBlock.text "▲▼で優先度変更"
                                                                                TextBlock.foreground (SolidColorBrush textZinc500)
                                                                                TextBlock.fontSize 10.0
                                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                                            ]
                                                                            TextBlock.create [
                                                                                TextBlock.text $"登録済みルール ({model.Settings.Rules.Length}件)"
                                                                                TextBlock.foreground (SolidColorBrush textZinc400)
                                                                                TextBlock.fontSize 11.0
                                                                                TextBlock.fontWeight FontWeight.SemiBold
                                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                                            ]
                                                                        ]
                                                                    ]
                                                                )
                                                            ]

                                                            // 左ペイン下部: 新規作成ボタン
                                                            Border.create [
                                                                DockPanel.dock Dock.Bottom
                                                                Border.padding 12.0
                                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                                Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                                                                Border.background (SolidColorBrush bgSurface)
                                                                Border.child (
                                                                    Button.create [
                                                                        Button.content (
                                                                            StackPanel.create [
                                                                                StackPanel.orientation Orientation.Horizontal
                                                                                StackPanel.spacing 6.0
                                                                                StackPanel.horizontalAlignment HorizontalAlignment.Center
                                                                                StackPanel.children [
                                                                                    TextBlock.create [ TextBlock.text "➕"; TextBlock.fontSize 11.0 ]
                                                                                    TextBlock.create [
                                                                                        TextBlock.text (if isEditing then "新規ルールを作成" else "新規ルールを作成中")
                                                                                        TextBlock.fontWeight FontWeight.SemiBold
                                                                                        TextBlock.fontSize 11.0
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                        )
                                                                        Button.horizontalAlignment HorizontalAlignment.Stretch
                                                                        Button.horizontalContentAlignment HorizontalAlignment.Center
                                                                        Button.background (SolidColorBrush (if isEditing then btnDark else accentBlue))
                                                                        Button.borderBrush (SolidColorBrush (if isEditing then borderZinc700 else borderFluent))
                                                                        Button.borderThickness 1.0
                                                                        Button.foreground (SolidColorBrush textWhite)
                                                                        Button.cornerRadius 6.0
                                                                        Button.padding (0.0, 8.0)
                                                                        Button.onClick (fun _ -> dispatch CancelEditRule)
                                                                    ]
                                                                )
                                                            ]

                                                            // 中央: ルール一覧 (ScrollViewer)
                                                            ScrollViewer.create [
                                                                ScrollViewer.content (
                                                                    StackPanel.create [
                                                                        StackPanel.spacing 8.0
                                                                        StackPanel.margin 12.0
                                                                        StackPanel.children [
                                                                            for idx, rule in List.indexed model.Settings.Rules do
                                                                                let isThisEditing =
                                                                                    match model.EditingRule with
                                                                                    | Some r -> r.Id = rule.Id
                                                                                    | None -> false

                                                                                Border.create [
                                                                                    Border.background (SolidColorBrush (if isThisEditing then Color.Parse("#172554") else bgCard))
                                                                                    Border.borderBrush (SolidColorBrush (if isThisEditing then accentBlue else borderZinc700))
                                                                                    Border.borderThickness (if isThisEditing then 1.5 else 1.0)
                                                                                    Border.cornerRadius 6.0
                                                                                    Border.padding 10.0
                                                                                    Border.child (
                                                                                        DockPanel.create [
                                                                                            DockPanel.children [
                                                                                                // 右側ボタン群 (▲ / ▼ / ✏️ / 🗑)
                                                                                                StackPanel.create [
                                                                                                    DockPanel.dock Dock.Right
                                                                                                    StackPanel.orientation Orientation.Horizontal
                                                                                                    StackPanel.spacing 4.0
                                                                                                    StackPanel.verticalAlignment VerticalAlignment.Top
                                                                                                    StackPanel.children [
                                                                                                        Button.create [
                                                                                                            Button.content "▲"
                                                                                                            Button.isEnabled (idx > 0)
                                                                                                            Button.background (SolidColorBrush btnDark)
                                                                                                            Button.borderBrush (SolidColorBrush borderZinc700)
                                                                                                            Button.borderThickness 1.0
                                                                                                            Button.foreground (SolidColorBrush textWhite)
                                                                                                            Button.cornerRadius 3.0
                                                                                                            Button.padding (6.0, 3.0)
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
                                                                                                            Button.padding (6.0, 3.0)
                                                                                                            Button.fontSize 10.0
                                                                                                            Button.onClick (fun _ -> dispatch (MoveRuleOrder (rule.Id, 1)))
                                                                                                        ]
                                                                                                        Button.create [
                                                                                                            Button.content "✏"
                                                                                                            Button.fontFamily emojiFontFamily
                                                                                                            Button.background (SolidColorBrush (if isThisEditing then Color.Parse("#d97706") else btnDark))
                                                                                                            Button.borderBrush (SolidColorBrush (if isThisEditing then Color.Parse("#f59e0b") else borderZinc700))
                                                                                                            Button.borderThickness 1.0
                                                                                                            Button.foreground (SolidColorBrush textWhite)
                                                                                                            Button.cornerRadius 3.0
                                                                                                            Button.padding (6.0, 3.0)
                                                                                                            Button.fontSize 10.0
                                                                                                            Button.onClick (fun _ -> dispatch (StartEditRule rule.Id))
                                                                                                        ]
                                                                                                        Button.create [
                                                                                                            Button.content "🗑"
                                                                                                            Button.isEnabled (model.Settings.Rules.Length > 1)
                                                                                                            Button.background (SolidColorBrush bgBefore)
                                                                                                            Button.borderBrush (SolidColorBrush borderBefore)
                                                                                                            Button.borderThickness 1.0
                                                                                                            Button.foreground (SolidColorBrush textBeforeLabel)
                                                                                                            Button.cornerRadius 3.0
                                                                                                            Button.padding (6.0, 3.0)
                                                                                                            Button.fontSize 10.0
                                                                                                            Button.onClick (fun _ -> dispatch (RequestDeleteRule rule.Id))
                                                                                                        ]
                                                                                                    ]
                                                                                                ]

                                                                                                // 左側ルール情報
                                                                                                StackPanel.create [
                                                                                                    StackPanel.spacing 3.0
                                                                                                    StackPanel.margin (0.0, 0.0, 8.0, 0.0)
                                                                                                    StackPanel.children [
                                                                                                        if idx = 0 || isThisEditing then
                                                                                                            StackPanel.create [
                                                                                                                StackPanel.orientation Orientation.Horizontal
                                                                                                                StackPanel.spacing 6.0
                                                                                                                StackPanel.children [
                                                                                                                    if idx = 0 then
                                                                                                                        Border.create [
                                                                                                                            Border.background (SolidColorBrush (Color.Parse("#172554")))
                                                                                                                            Border.borderBrush (SolidColorBrush (Color.Parse("#1e40af")))
                                                                                                                            Border.borderThickness 1.0
                                                                                                                            Border.cornerRadius 3.0
                                                                                                                            Border.padding (4.0, 1.0)
                                                                                                                            Border.child (
                                                                                                                                TextBlock.create [
                                                                                                                                    TextBlock.text "★既定"
                                                                                                                                    TextBlock.fontSize 9.0
                                                                                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                                                                                    TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                                                                ]
                                                                                                                            )
                                                                                                                        ]
                                                                                                                    if isThisEditing then
                                                                                                                        Border.create [
                                                                                                                            Border.background (SolidColorBrush (Color.Parse("#451a03")))
                                                                                                                            Border.borderBrush (SolidColorBrush (Color.Parse("#92400e")))
                                                                                                                            Border.borderThickness 1.0
                                                                                                                            Border.cornerRadius 3.0
                                                                                                                            Border.padding (4.0, 1.0)
                                                                                                                            Border.child (
                                                                                                                                TextBlock.create [
                                                                                                                                    TextBlock.text "✏ 編集中"
                                                                                                                                    TextBlock.fontSize 9.0
                                                                                                                                    TextBlock.fontWeight FontWeight.Bold
                                                                                                                                    TextBlock.foreground (SolidColorBrush (Color.Parse("#fcd34d")))
                                                                                                                                ]
                                                                                                                            )
                                                                                                                        ]
                                                                                                                ]
                                                                                                            ]
                                                                                                        TextBlock.create [
                                                                                                            TextBlock.text rule.Name
                                                                                                            TextBlock.foreground (SolidColorBrush textWhite)
                                                                                                            TextBlock.fontWeight FontWeight.Bold
                                                                                                            TextBlock.fontSize 12.0
                                                                                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                                                                                            TextBlock.maxLines 1
                                                                                                        ]
                                                                                                        TextBlock.create [
                                                                                                            TextBlock.text $"パターン: {rule.Pattern}"
                                                                                                            TextBlock.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                                            TextBlock.fontFamily monoFontFamily
                                                                                                            TextBlock.fontSize 10.0
                                                                                                        ]
                                                                                                        DockPanel.create [
                                                                                                            DockPanel.margin (0.0, 2.0, 0.0, 0.0)
                                                                                                            DockPanel.children [
                                                                                                                if rule.EnableWebSearch then
                                                                                                                    Border.create [
                                                                                                                        DockPanel.dock Dock.Left
                                                                                                                        Border.background (SolidColorBrush (Color.Parse("#172554")))
                                                                                                                        Border.borderBrush (SolidColorBrush (Color.Parse("#1e40af")))
                                                                                                                        Border.borderThickness 1.0
                                                                                                                        Border.cornerRadius 3.0
                                                                                                                        Border.padding (4.0, 1.0)
                                                                                                                        Border.margin (0.0, 0.0, 6.0, 0.0)
                                                                                                                        Border.child (
                                                                                                                            TextBlock.create [
                                                                                                                                TextBlock.text "🌐 Web検索有効"
                                                                                                                                TextBlock.fontSize 9.0
                                                                                                                                TextBlock.foreground (SolidColorBrush (Color.Parse("#93c5fd")))
                                                                                                                            ]
                                                                                                                        )
                                                                                                                    ]
                                                                                                                else
                                                                                                                    TextBlock.create [
                                                                                                                        DockPanel.dock Dock.Left
                                                                                                                        TextBlock.text "検索無効"
                                                                                                                        TextBlock.fontSize 9.0
                                                                                                                        TextBlock.foreground (SolidColorBrush textZinc500)
                                                                                                                        TextBlock.margin (0.0, 0.0, 6.0, 0.0)
                                                                                                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                                                                                                    ]
                                                                                                                TextBlock.create [
                                                                                                                    TextBlock.text rule.PromptInstruction
                                                                                                                    TextBlock.foreground (SolidColorBrush textSub)
                                                                                                                    TextBlock.fontSize 9.0
                                                                                                                    TextBlock.textWrapping TextWrapping.NoWrap
                                                                                                                    TextBlock.maxLines 1
                                                                                                                ]
                                                                                                            ]
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

                                            // ----------------------------------------------------
                                            // 右ペイン: ルール詳細・編集フォーム
                                            // ----------------------------------------------------
                                            Border.create [
                                                Grid.column 1
                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                                                Border.background (SolidColorBrush bgSurface)
                                                Border.padding 20.0
                                                Border.child (
                                                    DockPanel.create [
                                                        DockPanel.children [
                                                            // 右ペインヘッダー
                                                            Border.create [
                                                                DockPanel.dock Dock.Top
                                                                Border.padding (0.0, 0.0, 0.0, 12.0)
                                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                                                Border.child (
                                                                    DockPanel.create [
                                                                        DockPanel.children [
                                                                            if isEditing then
                                                                                Button.create [
                                                                                    DockPanel.dock Dock.Right
                                                                                    Button.content "✕ 編集をキャンセル"
                                                                                    Button.background (SolidColorBrush btnDark)
                                                                                    Button.borderBrush (SolidColorBrush borderZinc700)
                                                                                    Button.borderThickness 1.0
                                                                                    Button.foreground (SolidColorBrush textSub)
                                                                                    Button.fontSize 11.0
                                                                                    Button.padding (10.0, 4.0)
                                                                                    Button.cornerRadius 4.0
                                                                                    Button.onClick (fun _ -> dispatch CancelEditRule)
                                                                                ]
                                                                            StackPanel.create [
                                                                                StackPanel.orientation Orientation.Horizontal
                                                                                StackPanel.spacing 8.0
                                                                                StackPanel.verticalAlignment VerticalAlignment.Center
                                                                                StackPanel.children [
                                                                                    TextBlock.create [
                                                                                        TextBlock.text (if isEditing then "✏️ ルールの編集" else "➕ 新規ルールの作成")
                                                                                        TextBlock.foreground (SolidColorBrush textWhite)
                                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                                        TextBlock.fontSize 14.0
                                                                                    ]
                                                                                    Border.create [
                                                                                        Border.background (SolidColorBrush (if isEditing then Color.Parse("#451a03") else Color.Parse("#172554")))
                                                                                        Border.borderBrush (SolidColorBrush (if isEditing then Color.Parse("#92400e") else Color.Parse("#1e40af")))
                                                                                        Border.borderThickness 1.0
                                                                                        Border.cornerRadius 3.0
                                                                                        Border.padding (6.0, 2.0)
                                                                                        Border.child (
                                                                                            TextBlock.create [
                                                                                                TextBlock.text (if isEditing then $"既存ルール更新モード: {curRule.Name}" else "新規追加モード (入力後に保存してください)")
                                                                                                TextBlock.fontSize 10.0
                                                                                                TextBlock.fontWeight FontWeight.SemiBold
                                                                                                TextBlock.foreground (SolidColorBrush (if isEditing then Color.Parse("#fcd34d") else Color.Parse("#93c5fd")))
                                                                                            ]
                                                                                        )
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                )
                                                            ]

                                                            // 右ペインフッター: アクションボタン
                                                            Border.create [
                                                                DockPanel.dock Dock.Bottom
                                                                Border.margin (0.0, 12.0, 0.0, 0.0)
                                                                Border.padding (0.0, 12.0, 0.0, 0.0)
                                                                Border.borderBrush (SolidColorBrush borderZinc800)
                                                                Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                                                                Border.child (
                                                                    DockPanel.create [
                                                                        DockPanel.children [
                                                                            TextBlock.create [
                                                                                DockPanel.dock Dock.Left
                                                                                TextBlock.text (if String.IsNullOrWhiteSpace curRule.Name then "ルール名を入力してください" else $"「{curRule.Name}」を保存します")
                                                                                TextBlock.foreground (SolidColorBrush textSub)
                                                                                TextBlock.fontSize 11.0
                                                                                TextBlock.verticalAlignment VerticalAlignment.Center
                                                                            ]
                                                                            StackPanel.create [
                                                                                DockPanel.dock Dock.Right
                                                                                StackPanel.orientation Orientation.Horizontal
                                                                                StackPanel.spacing 8.0
                                                                                StackPanel.children [
                                                                                    if isEditing then
                                                                                        Button.create [
                                                                                            Button.content "キャンセル"
                                                                                            Button.background (SolidColorBrush btnDark)
                                                                                            Button.borderBrush (SolidColorBrush borderZinc700)
                                                                                            Button.borderThickness 1.0
                                                                                            Button.foreground (SolidColorBrush textSub)
                                                                                            Button.fontSize 11.0
                                                                                            Button.padding (12.0, 6.0)
                                                                                            Button.cornerRadius 4.0
                                                                                            Button.onClick (fun _ -> dispatch CancelEditRule)
                                                                                        ]
                                                                                    Button.create [
                                                                                        Button.content (if isEditing then "💾 この内容で更新・保存" else "💾 この命名規則を追加する")
                                                                                        Button.isEnabled (not (String.IsNullOrWhiteSpace curRule.Name))
                                                                                        Button.background (SolidColorBrush accentBlue)
                                                                                        Button.foreground (SolidColorBrush textWhite)
                                                                                        Button.fontWeight FontWeight.Bold
                                                                                        Button.fontSize 11.0
                                                                                        Button.cornerRadius 4.0
                                                                                        Button.padding (16.0, 6.0)
                                                                                        Button.onClick (fun _ -> dispatch SaveEditingRule)
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                )
                                                            ]

                                                            // フォーム中央コンテンツ
                                                            StackPanel.create [
                                                                StackPanel.spacing 12.0
                                                                StackPanel.margin (0.0, 12.0, 0.0, 0.0)
                                                                StackPanel.children [
                                                                    // ルール名 & 命名パターン (2列 Grid)
                                                                    Grid.create [
                                                                        Grid.columnDefinitions "*, *"
                                                                        Grid.children [
                                                                            // ルール名
                                                                            StackPanel.create [
                                                                                Grid.column 0
                                                                                StackPanel.spacing 4.0
                                                                                StackPanel.margin (0.0, 0.0, 8.0, 0.0)
                                                                                StackPanel.children [
                                                                                    StackPanel.create [
                                                                                        StackPanel.orientation Orientation.Horizontal
                                                                                        StackPanel.spacing 4.0
                                                                                        StackPanel.children [
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "ルール名 (必須):"
                                                                                                TextBlock.foreground (SolidColorBrush textSub)
                                                                                                TextBlock.fontSize 11.0
                                                                                                TextBlock.fontWeight FontWeight.SemiBold
                                                                                            ]
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "*"
                                                                                                TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                                                                TextBlock.fontSize 11.0
                                                                                            ]
                                                                                        ]
                                                                                    ]
                                                                                    TextBox.create [
                                                                                        TextBox.text curRule.Name
                                                                                        TextBox.height 28.0
                                                                                        TextBox.fontSize 11.0
                                                                                        TextBox.background (SolidColorBrush bgInput)
                                                                                        TextBox.foreground (SolidColorBrush textWhite)
                                                                                        TextBox.borderBrush (SolidColorBrush borderZinc700)
                                                                                        TextBox.cornerRadius 4.0
                                                                                        TextBox.padding (8.0, 4.0)
                                                                                        TextBox.watermark "例: 品番_要約_出演者(推奨)"
                                                                                        TextBox.onTextChanged (fun t -> dispatch (UpdateEditingRuleName t))
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                            // 命名パターン
                                                                            StackPanel.create [
                                                                                Grid.column 1
                                                                                StackPanel.spacing 4.0
                                                                                StackPanel.margin (8.0, 0.0, 0.0, 0.0)
                                                                                StackPanel.children [
                                                                                    StackPanel.create [
                                                                                        StackPanel.orientation Orientation.Horizontal
                                                                                        StackPanel.spacing 6.0
                                                                                        StackPanel.children [
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "命名パターン (テンプレート):"
                                                                                                TextBlock.foreground (SolidColorBrush textSub)
                                                                                                TextBlock.fontSize 11.0
                                                                                                TextBlock.fontWeight FontWeight.SemiBold
                                                                                            ]
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "※拡張子は不要"
                                                                                                TextBlock.foreground (SolidColorBrush textZinc500)
                                                                                                TextBlock.fontSize 10.0
                                                                                            ]
                                                                                        ]
                                                                                    ]
                                                                                    TextBox.create [
                                                                                        TextBox.text curRule.Pattern
                                                                                        TextBox.height 28.0
                                                                                        TextBox.fontSize 11.0
                                                                                        TextBox.fontFamily monoFontFamily
                                                                                        TextBox.background (SolidColorBrush bgInput)
                                                                                        TextBox.foreground (SolidColorBrush (Color.Parse("#60a5fa")))
                                                                                        TextBox.borderBrush (SolidColorBrush borderZinc700)
                                                                                        TextBox.cornerRadius 4.0
                                                                                        TextBox.padding (8.0, 4.0)
                                                                                        TextBox.watermark "例: {Code}_{Summary}_{Actor}"
                                                                                        TextBox.onTextChanged (fun t -> dispatch (UpdateEditingRulePattern t))
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]

                                                                    // Web検索トグル
                                                                    CheckBox.create [
                                                                        CheckBox.content "🌐 DuckDuckGo Web検索 (ddgs) を有効にする (作品タイトルや出演者をWebから取得してプロンプトに注入します)"
                                                                        CheckBox.isChecked curRule.EnableWebSearch
                                                                        CheckBox.foreground (SolidColorBrush textWhite)
                                                                        CheckBox.fontSize 11.0
                                                                        CheckBox.verticalAlignment VerticalAlignment.Center
                                                                        CheckBox.onIsCheckedChanged (fun e ->
                                                                            match getCheckBoxValue e with
                                                                            | Some isChecked when isChecked <> curRule.EnableWebSearch ->
                                                                                dispatch (UpdateEditingRuleWebSearch isChecked)
                                                                            | _ -> ()
                                                                        )
                                                                    ]

                                                                    // プロンプト指示文 (AIへの詳細命名指示) - 広域テキストエリア
                                                                    StackPanel.create [
                                                                        StackPanel.spacing 4.0
                                                                        StackPanel.children [
                                                                            DockPanel.create [
                                                                                DockPanel.children [
                                                                                    TextBlock.create [
                                                                                        DockPanel.dock Dock.Right
                                                                                        TextBlock.text "※縦領域を広く活用して長文も確認・編集可能です"
                                                                                        TextBlock.foreground (SolidColorBrush textZinc500)
                                                                                        TextBlock.fontSize 10.0
                                                                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                                                                    ]
                                                                                    StackPanel.create [
                                                                                        StackPanel.orientation Orientation.Horizontal
                                                                                        StackPanel.spacing 4.0
                                                                                        StackPanel.children [
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "プロンプト指示文 (AIへの詳細命名指示):"
                                                                                                TextBlock.foreground (SolidColorBrush textSub)
                                                                                                TextBlock.fontSize 11.0
                                                                                                TextBlock.fontWeight FontWeight.SemiBold
                                                                                            ]
                                                                                            TextBlock.create [
                                                                                                TextBlock.text "*"
                                                                                                TextBlock.foreground (SolidColorBrush textBeforeLabel)
                                                                                                TextBlock.fontSize 11.0
                                                                                            ]
                                                                                        ]
                                                                                    ]
                                                                                ]
                                                                            ]
                                                                            TextBox.create [
                                                                                TextBox.text curRule.PromptInstruction
                                                                                TextBox.height 280.0
                                                                                TextBox.fontSize 11.0
                                                                                TextBox.acceptsReturn true
                                                                                TextBox.textWrapping TextWrapping.Wrap
                                                                                TextBox.background (SolidColorBrush bgInput)
                                                                                TextBox.foreground (SolidColorBrush textWhite)
                                                                                TextBox.borderBrush (SolidColorBrush borderZinc700)
                                                                                TextBox.cornerRadius 4.0
                                                                                TextBox.padding (8.0, 8.0)
                                                                                TextBox.watermark "例: 既存のファイル名から品番、タイトルの要約、出演者を抽出しアンダースコア繋ぎで命名（短すぎても内容が把握しずらくなるので日本語で200文字前後となるように）してください。日本語タイトルや出演者が不明な場合は品番をWeb検索してそれぞれFANZAから日本語を取得してください。"
                                                                                TextBox.onTextChanged (fun t -> dispatch (UpdateEditingRulePrompt t))
                                                                            ]
                                                                        ]
                                                                    ]
                                                                ]
                                                            ]
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
