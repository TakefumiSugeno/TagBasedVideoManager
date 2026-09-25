namespace TagBasedVideoManager.Renamer

open System
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.Layout
open Avalonia.Media
open TagBasedVideoManager.Renamer

module Views =

    // ==========================================
    // カラー・スタイル定数
    // ==========================================
    let private primaryColor = Color.Parse("#2563EB")
    let private successColor = Color.Parse("#16A34A")
    let private warningColor = Color.Parse("#D97706")
    let private dangerColor = Color.Parse("#DC2626")
    let private bgHeader = Color.Parse("#1E293B")
    let private textLight = Color.Parse("#F8FAFC")
    let private textDark = Color.Parse("#0F172A")
    let private borderGray = Color.Parse("#CBD5E1")
    let private bgPanel = Color.Parse("#F8FAFC")

    // ==========================================
    // 1. Docker コントローラーバー (Task 5.2)
    // ==========================================
    let dockerStatusBar (model: Model) (dispatch: Msg -> unit) =
        let stateText, badgeColor =
            match model.Docker.State with
            | Running -> "RUNNING", successColor
            | Stopped -> "STOPPED", Color.Parse("#64748B")
            | Restarting -> "RESTARTING", warningColor
            | Unhealthy -> "UNHEALTHY", dangerColor
            | NotFound -> "NOT FOUND", dangerColor

        let portText, portColor =
            if model.Docker.IsPortAccessible then "Port 5620: OK", successColor
            else "Port 5620: NG", dangerColor

        DockPanel.create [
            DockPanel.dock Dock.Top
            DockPanel.background (SolidColorBrush bgHeader)
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Left
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 12.0
                    StackPanel.margin 12.0
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.text "🐳 TagBasedVideoManager (Docker):"
                            TextBlock.foreground (SolidColorBrush textLight)
                            TextBlock.verticalAlignment VerticalAlignment.Center
                            TextBlock.fontWeight FontWeight.Bold
                        ]
                        Border.create [
                            Border.background (SolidColorBrush badgeColor)
                            Border.cornerRadius 4.0
                            Border.padding (8.0, 3.0)
                            Border.child (
                                TextBlock.create [
                                    TextBlock.text stateText
                                    TextBlock.foreground (SolidColorBrush textLight)
                                    TextBlock.fontWeight FontWeight.Bold
                                    TextBlock.fontSize 12.0
                                ]
                            )
                        ]
                        Border.create [
                            Border.background (SolidColorBrush (Color.FromArgb(50uy, portColor.R, portColor.G, portColor.B)))
                            Border.borderBrush (SolidColorBrush portColor)
                            Border.borderThickness 1.0
                            Border.cornerRadius 4.0
                            Border.padding (8.0, 3.0)
                            Border.child (
                                TextBlock.create [
                                    TextBlock.text portText
                                    TextBlock.foreground (SolidColorBrush textLight)
                                    TextBlock.fontSize 12.0
                                ]
                            )
                        ]
                    ]
                ]

                StackPanel.create [
                    DockPanel.dock Dock.Right
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.margin 12.0
                    StackPanel.children [
                        Button.create [
                            Button.content "▶ Start"
                            Button.isEnabled (not model.IsDockerBusy && model.Docker.State <> Running)
                            Button.background (SolidColorBrush (Color.Parse("#334155")))
                            Button.foreground (SolidColorBrush textLight)
                            Button.cornerRadius 4.0
                            Button.padding (10.0, 4.0)
                            Button.onClick (fun _ -> dispatch (ExecuteDockerAction "up -d"))
                        ]
                        Button.create [
                            Button.content "■ Stop"
                            Button.isEnabled (not model.IsDockerBusy && model.Docker.State = Running)
                            Button.background (SolidColorBrush (Color.Parse("#334155")))
                            Button.foreground (SolidColorBrush textLight)
                            Button.cornerRadius 4.0
                            Button.padding (10.0, 4.0)
                            Button.onClick (fun _ -> dispatch (ExecuteDockerAction "stop"))
                        ]
                        Button.create [
                            Button.content "🔄 Restart"
                            Button.isEnabled (not model.IsDockerBusy)
                            Button.background (SolidColorBrush (Color.Parse("#334155")))
                            Button.foreground (SolidColorBrush textLight)
                            Button.cornerRadius 4.0
                            Button.padding (10.0, 4.0)
                            Button.onClick (fun _ -> dispatch (ExecuteDockerAction "restart tag-based-video-manager"))
                        ]
                        Button.create [
                            Button.content "Check"
                            Button.isEnabled (not model.IsDockerBusy)
                            Button.background (SolidColorBrush (Color.Parse("#334155")))
                            Button.foreground (SolidColorBrush textLight)
                            Button.cornerRadius 4.0
                            Button.padding (10.0, 4.0)
                            Button.onClick (fun _ -> dispatch CheckDockerStatus)
                        ]
                    ]
                ]
            ]
        ]

    // ==========================================
    // 2. コントロールパネル (Task 5.3)
    // ==========================================
    let controlPanel (model: Model) (dispatch: Msg -> unit) =
        Border.create [
            DockPanel.dock Dock.Top
            Border.background (SolidColorBrush bgPanel)
            Border.borderBrush (SolidColorBrush borderGray)
            Border.borderThickness (0.0, 1.0, 0.0, 1.0)
            Border.padding 12.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 10.0
                    StackPanel.children [
                        // 行1: フォルダ指定
                        DockPanel.create [
                            DockPanel.children [
                                TextBlock.create [
                                    DockPanel.dock Dock.Left
                                    TextBlock.text "走査対象フォルダ:"
                                    TextBlock.foreground (SolidColorBrush textDark)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.width 130.0
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBox.create [
                                    TextBox.text model.Settings.TargetDirectory
                                    TextBox.height 32.0
                                    TextBox.verticalAlignment VerticalAlignment.Center
                                    TextBox.verticalContentAlignment VerticalAlignment.Center
                                    TextBox.background (SolidColorBrush Colors.White)
                                    TextBox.foreground (SolidColorBrush textDark)
                                    TextBox.borderBrush (SolidColorBrush borderGray)
                                    TextBox.borderThickness 1.0
                                    TextBox.cornerRadius 4.0
                                    TextBox.padding (8.0, 4.0)
                                    TextBox.onTextChanged (fun text -> dispatch (TargetDirectoryChanged text))
                                ]
                            ]
                        ]

                        // 行2: 抽出基準数値 (セッション内一時変更) & モデル & ルール
                        WrapPanel.create [
                            WrapPanel.children [
                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.margin (0.0, 0.0, 20.0, 0.0)
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "抽出基準:"
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "パス長 ≧"
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                        TextBox.create [
                                            TextBox.text (string model.CurrentThreshold)
                                            TextBox.width 60.0
                                            TextBox.height 32.0
                                            TextBox.verticalAlignment VerticalAlignment.Center
                                            TextBox.verticalContentAlignment VerticalAlignment.Center
                                            TextBox.background (SolidColorBrush Colors.White)
                                            TextBox.foreground (SolidColorBrush textDark)
                                            TextBox.borderBrush (SolidColorBrush borderGray)
                                            TextBox.borderThickness 1.0
                                            TextBox.cornerRadius 4.0
                                            TextBox.padding (6.0, 4.0)
                                            TextBox.onTextChanged (fun text ->
                                                match Int32.TryParse(text) with
                                                | true, v -> dispatch (ThresholdChanged v)
                                                | _ -> ()
                                            )
                                        ]
                                        TextBlock.create [
                                            TextBlock.text "文字 (※セッション内一時変更・設定非保存)"
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush (Color.Parse("#64748B")))
                                        ]
                                    ]
                                ]

                                StackPanel.create [
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "命名規則:"
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                        ComboBox.create [
                                            ComboBox.dataItems (model.Settings.Rules |> List.map (fun r -> r.Name))
                                            ComboBox.selectedIndex (
                                                model.Settings.Rules
                                                |> List.tryFindIndex (fun r -> r.Id = model.SelectedRuleId)
                                                |> Option.defaultValue 0
                                            )
                                            ComboBox.onSelectedIndexChanged (fun idx ->
                                                if idx >= 0 && idx < model.Settings.Rules.Length then
                                                    dispatch (RuleSelected model.Settings.Rules.[idx].Id)
                                            )
                                        ]
                                        Button.create [
                                            Button.content "⚙ 管理..."
                                            Button.background (SolidColorBrush Colors.White)
                                            Button.foreground (SolidColorBrush textDark)
                                            Button.borderBrush (SolidColorBrush borderGray)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (8.0, 4.0)
                                            Button.onClick (fun _ -> dispatch OpenRuleManager)
                                        ]
                                    ]
                                ]
                            ]
                        ]

                        // 行3: メインワンアクションボタン
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.children [
                                Button.create [
                                    Button.content "🚀 リネーム対象抽出 ＆ AI提案を実行 (ワンアクション)"
                                    Button.background (SolidColorBrush primaryColor)
                                    Button.foreground (SolidColorBrush textLight)
                                    Button.fontWeight FontWeight.Bold
                                    Button.cornerRadius 6.0
                                    Button.padding (16.0, 8.0)
                                    Button.isEnabled (not model.IsScanning && not model.IsRequestingAi && not model.IsRenaming)
                                    Button.onClick (fun _ -> dispatch ExecuteScanAndPropose)
                                ]

                                if model.IsScanning then
                                    TextBlock.create [
                                        TextBlock.text "🔍 長パスファイルを走査中..."
                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                        TextBlock.foreground (SolidColorBrush primaryColor)
                                        TextBlock.fontWeight FontWeight.Bold
                                    ]
                                elif model.IsRequestingAi then
                                    TextBlock.create [
                                        TextBlock.text "🤖 AI短縮リネーム提案を生成中..."
                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                        TextBlock.foreground (SolidColorBrush warningColor)
                                        TextBlock.fontWeight FontWeight.Bold
                                    ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 3. Before / After 対比ビュー (Task 5.4)
    // ==========================================
    let candidateCardVertical (c: RenameProposal) (dispatch: Msg -> unit) =
        let isOverLimit = c.OriginalLength >= 240
        let origBadgeColor = if isOverLimit then dangerColor else warningColor
        let reduction = c.OriginalLength - c.ProposedLength

        Border.create [
            Border.background (SolidColorBrush Colors.White)
            Border.borderBrush (SolidColorBrush (if c.IsSelected then primaryColor else borderGray))
            Border.borderThickness (if c.IsSelected then 2.0 else 1.0)
            Border.cornerRadius 6.0
            Border.margin (0.0, 2.0, 0.0, 6.0)
            Border.padding 10.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 6.0
                    StackPanel.children [
                        // 上行: BEFORE
                        DockPanel.create [
                            DockPanel.children [
                                CheckBox.create [
                                    DockPanel.dock Dock.Left
                                    CheckBox.isChecked c.IsSelected
                                    CheckBox.onIsCheckedChanged (fun _ -> dispatch (ToggleCandidateSelect c.OriginalFullPath))
                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                    CheckBox.margin (0.0, 0.0, 4.0, 0.0)
                                ]
                                Border.create [
                                    DockPanel.dock Dock.Left
                                    Border.background (SolidColorBrush origBadgeColor)
                                    Border.cornerRadius 3.0
                                    Border.padding (6.0, 2.0)
                                    Border.margin (0.0, 0.0, 8.0, 0.0)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"BEFORE: {c.OriginalLength}字"
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush textLight)
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                                TextBlock.create [
                                    TextBlock.text c.OriginalFileName
                                    TextBlock.foreground (SolidColorBrush textDark)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                ]
                            ]
                        ]

                        // 下行: AFTER (インライン編集 TextBox)
                        DockPanel.create [
                            DockPanel.margin (28.0, 0.0, 0.0, 0.0)
                            DockPanel.children [
                                Border.create [
                                    DockPanel.dock Dock.Left
                                    Border.background (SolidColorBrush successColor)
                                    Border.cornerRadius 3.0
                                    Border.padding (6.0, 2.0)
                                    Border.margin (0.0, 0.0, 8.0, 0.0)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"AFTER: {c.ProposedLength}字"
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush textLight)
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                                Border.create [
                                    DockPanel.dock Dock.Right
                                    Border.background (SolidColorBrush (Color.Parse("#E0F2FE")))
                                    Border.cornerRadius 3.0
                                    Border.padding (6.0, 2.0)
                                    Border.margin (8.0, 0.0, 0.0, 0.0)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"{reduction}字削減"
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush primaryColor)
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                                TextBox.create [
                                    TextBox.text c.ProposedFileName
                                    TextBox.height 30.0
                                    TextBox.verticalAlignment VerticalAlignment.Center
                                    TextBox.verticalContentAlignment VerticalAlignment.Center
                                    TextBox.background (SolidColorBrush Colors.White)
                                    TextBox.foreground (SolidColorBrush textDark)
                                    TextBox.borderBrush (SolidColorBrush (Color.Parse("#93C5FD")))
                                    TextBox.borderThickness 1.0
                                    TextBox.cornerRadius 4.0
                                    TextBox.padding (8.0, 2.0)
                                    TextBox.onTextChanged (fun newName ->
                                        dispatch (UpdateProposedName (c.OriginalFullPath, newName))
                                    )
                                ]
                            ]
                        ]

                        // AIコメント (問題発生時のみ表示)
                        match c.AiComment with
                        | Some comment when not (String.IsNullOrWhiteSpace(comment)) ->
                            Border.create [
                                Border.margin (28.0, 2.0, 0.0, 0.0)
                                Border.background (SolidColorBrush (Color.Parse("#FEF3C7")))
                                Border.borderBrush (SolidColorBrush (Color.Parse("#F59E0B")))
                                Border.borderThickness 1.0
                                Border.cornerRadius 4.0
                                Border.padding (8.0, 4.0)
                                Border.child (
                                    TextBlock.create [
                                        TextBlock.text $"⚠️ AIコメント: {comment}"
                                        TextBlock.fontSize 11.0
                                        TextBlock.foreground (SolidColorBrush (Color.Parse("#92400E")))
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                )
                            ]
                        | _ -> ()
                    ]
                ]
            )
        ]

    let candidateCardHorizontal (c: RenameProposal) (dispatch: Msg -> unit) =
        let reduction = c.OriginalLength - c.ProposedLength
        Border.create [
            Border.background (SolidColorBrush Colors.White)
            Border.borderBrush (SolidColorBrush (if c.IsSelected then primaryColor else borderGray))
            Border.borderThickness (if c.IsSelected then 2.0 else 1.0)
            Border.cornerRadius 6.0
            Border.margin (0.0, 2.0, 0.0, 6.0)
            Border.padding 10.0
            Border.child (
                StackPanel.create [
                    StackPanel.spacing 4.0
                    StackPanel.children [
                        Grid.create [
                            Grid.columnDefinitions "Auto, 1*, 1*, Auto"
                            Grid.children [
                                CheckBox.create [
                                    Grid.column 0
                                    CheckBox.isChecked c.IsSelected
                                    CheckBox.onIsCheckedChanged (fun _ -> dispatch (ToggleCandidateSelect c.OriginalFullPath))
                                    CheckBox.verticalAlignment VerticalAlignment.Center
                                    CheckBox.margin (0.0, 0.0, 8.0, 0.0)
                                ]
                                StackPanel.create [
                                    Grid.column 1
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children [
                                        Border.create [
                                            Border.background (SolidColorBrush dangerColor)
                                            Border.cornerRadius 3.0
                                            Border.padding (4.0, 2.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text $"{c.OriginalLength}字"
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.foreground (SolidColorBrush textLight)
                                                    TextBlock.fontWeight FontWeight.Bold
                                                ]
                                            )
                                        ]
                                        TextBlock.create [
                                            TextBlock.text c.OriginalFileName
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                        ]
                                    ]
                                ]
                                TextBox.create [
                                    Grid.column 2
                                    TextBox.text c.ProposedFileName
                                    TextBox.height 30.0
                                    TextBox.margin (8.0, 0.0, 8.0, 0.0)
                                    TextBox.verticalAlignment VerticalAlignment.Center
                                    TextBox.verticalContentAlignment VerticalAlignment.Center
                                    TextBox.background (SolidColorBrush Colors.White)
                                    TextBox.foreground (SolidColorBrush textDark)
                                    TextBox.borderBrush (SolidColorBrush (Color.Parse("#93C5FD")))
                                    TextBox.borderThickness 1.0
                                    TextBox.cornerRadius 4.0
                                    TextBox.padding (8.0, 2.0)
                                    TextBox.onTextChanged (fun newName ->
                                        dispatch (UpdateProposedName (c.OriginalFullPath, newName))
                                    )
                                ]
                                Border.create [
                                    Grid.column 3
                                    Border.background (SolidColorBrush successColor)
                                    Border.cornerRadius 3.0
                                    Border.padding (6.0, 2.0)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"{c.ProposedLength}字 (-{reduction})"
                                            TextBlock.fontSize 11.0
                                            TextBlock.foreground (SolidColorBrush textLight)
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    )
                                ]
                            ]
                        ]
                        match c.AiComment with
                        | Some comment when not (String.IsNullOrWhiteSpace(comment)) ->
                            Border.create [
                                Border.margin (28.0, 2.0, 0.0, 0.0)
                                Border.background (SolidColorBrush (Color.Parse("#FEF3C7")))
                                Border.borderBrush (SolidColorBrush (Color.Parse("#F59E0B")))
                                Border.borderThickness 1.0
                                Border.cornerRadius 4.0
                                Border.padding (8.0, 4.0)
                                Border.child (
                                    TextBlock.create [
                                        TextBlock.text $"⚠️ AIコメント: {comment}"
                                        TextBlock.fontSize 11.0
                                        TextBlock.foreground (SolidColorBrush (Color.Parse("#92400E")))
                                        TextBlock.fontWeight FontWeight.SemiBold
                                    ]
                                )
                            ]
                        | _ -> ()
                    ]
                ]
            )
        ]

    let comparisonList (model: Model) (dispatch: Msg -> unit) =
        DockPanel.create [
            DockPanel.children [
                // リストヘッダー
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background (SolidColorBrush (Color.Parse("#F1F5F9")))
                    Border.borderBrush (SolidColorBrush borderGray)
                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                    Border.padding (12.0, 6.0)
                    Border.child (
                        DockPanel.create [
                            DockPanel.children [
                                StackPanel.create [
                                    DockPanel.dock Dock.Left
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.children [
                                        Button.create [
                                            Button.content "全選択"
                                            Button.background (SolidColorBrush Colors.White)
                                            Button.foreground (SolidColorBrush textDark)
                                            Button.borderBrush (SolidColorBrush borderGray)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (10.0, 4.0)
                                            Button.onClick (fun _ -> dispatch (SelectAllCandidates true))
                                        ]
                                        Button.create [
                                            Button.content "全解除"
                                            Button.background (SolidColorBrush Colors.White)
                                            Button.foreground (SolidColorBrush textDark)
                                            Button.borderBrush (SolidColorBrush borderGray)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (10.0, 4.0)
                                            Button.onClick (fun _ -> dispatch (SelectAllCandidates false))
                                        ]
                                        TextBlock.create [
                                            TextBlock.text $"対象ファイル: {model.Candidates.Length} 件"
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                    ]
                                ]
                                StackPanel.create [
                                    DockPanel.dock Dock.Right
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text "表示形式:"
                                            TextBlock.foreground (SolidColorBrush textDark)
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                            TextBlock.fontSize 12.0
                                        ]
                                        Button.create [
                                            Button.content "▤ 上下並び"
                                            Button.background (if model.Layout = Vertical then SolidColorBrush primaryColor else SolidColorBrush Colors.White)
                                            Button.foreground (if model.Layout = Vertical then SolidColorBrush textLight else SolidColorBrush textDark)
                                            Button.borderBrush (SolidColorBrush borderGray)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (8.0, 4.0)
                                            Button.onClick (fun _ -> dispatch (SetLayoutMode Vertical))
                                        ]
                                        Button.create [
                                            Button.content "◫ 左右並び"
                                            Button.background (if model.Layout = Horizontal then SolidColorBrush primaryColor else SolidColorBrush Colors.White)
                                            Button.foreground (if model.Layout = Horizontal then SolidColorBrush textLight else SolidColorBrush textDark)
                                            Button.borderBrush (SolidColorBrush borderGray)
                                            Button.borderThickness 1.0
                                            Button.cornerRadius 4.0
                                            Button.padding (8.0, 4.0)
                                            Button.onClick (fun _ -> dispatch (SetLayoutMode Horizontal))
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    )
                ]

                // リスト本体
                ScrollViewer.create [
                    ScrollViewer.padding 12.0
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.children [
                                for c in model.Candidates do
                                    if model.Layout = Vertical then
                                        candidateCardVertical c dispatch
                                    else
                                        candidateCardHorizontal c dispatch
                            ]
                        ]
                    )
                ]
            ]
        ]

    // ==========================================
    // 4. フッターアクション (Task 5.5)
    // ==========================================
    let footerActions (model: Model) (dispatch: Msg -> unit) =
        let selectedCount = model.Candidates |> List.filter (fun c -> c.IsSelected) |> List.length
        let hasUndo = not (List.isEmpty model.UndoStack)

        Border.create [
            DockPanel.dock Dock.Bottom
            Border.background (SolidColorBrush (Color.Parse("#F1F5F9")))
            Border.borderBrush (SolidColorBrush borderGray)
            Border.borderThickness (0.0, 1.0, 0.0, 0.0)
            Border.padding 12.0
            Border.child (
                DockPanel.create [
                    DockPanel.children [
                        StackPanel.create [
                            DockPanel.dock Dock.Left
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.children [
                                Button.create [
                                    Button.content "↩ 直前のリネームを元に戻す (Undo)"
                                    Button.isEnabled (hasUndo && not model.IsRenaming)
                                    Button.background (SolidColorBrush Colors.White)
                                    Button.foreground (SolidColorBrush textDark)
                                    Button.borderBrush (SolidColorBrush (Color.Parse("#94A3B8")))
                                    Button.borderThickness 1.0
                                    Button.cornerRadius 4.0
                                    Button.padding (12.0, 8.0)
                                    Button.fontWeight FontWeight.Bold
                                    Button.onClick (fun _ -> dispatch RequestUndo)
                                ]
                                TextBlock.create [
                                    TextBlock.text $"選択中: {selectedCount} / {model.Candidates.Length} 件"
                                    TextBlock.foreground (SolidColorBrush textDark)
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                            ]
                        ]

                        StackPanel.create [
                            DockPanel.dock Dock.Right
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.children [
                                Button.create [
                                    Button.content "リネームのみ実行"
                                    Button.isEnabled (selectedCount > 0 && not model.IsRenaming)
                                    Button.background (SolidColorBrush (Color.Parse("#0284C7")))
                                    Button.foreground (SolidColorBrush textLight)
                                    Button.cornerRadius 4.0
                                    Button.padding (12.0, 8.0)
                                    Button.fontWeight FontWeight.Bold
                                    Button.onClick (fun _ -> dispatch ExecuteRenameOnly)
                                ]
                                Button.create [
                                    Button.content "⚡ リネームしてコンテナ再起動 (復旧)"
                                    Button.background (SolidColorBrush successColor)
                                    Button.foreground (SolidColorBrush textLight)
                                    Button.fontWeight FontWeight.Bold
                                    Button.cornerRadius 4.0
                                    Button.padding (14.0, 8.0)
                                    Button.isEnabled (selectedCount > 0 && not model.IsRenaming)
                                    Button.onClick (fun _ -> dispatch ExecuteRenameAndRestart)
                                ]
                            ]
                        ]
                    ]
                ]
            )
        ]

    // ==========================================
    // 5. ダイアログ / エラーバナー
    // ==========================================
    let errorBanner (model: Model) (dispatch: Msg -> unit) =
        match model.ErrorMessage with
        | Some msg ->
            let isInfo = msg.StartsWith("💡")
            let bannerBg = if isInfo then Color.Parse("#0284C7") else dangerColor
            Border.create [
                DockPanel.dock Dock.Top
                Border.background (SolidColorBrush bannerBg)
                Border.padding (12.0, 8.0)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            Button.create [
                                DockPanel.dock Dock.Right
                                Button.content "✕"
                                Button.foreground (SolidColorBrush textLight)
                                Button.background (SolidColorBrush Colors.Transparent)
                                Button.borderThickness 0.0
                                Button.onClick (fun _ -> dispatch DismissError)
                            ]
                            TextBlock.create [
                                TextBlock.text msg
                                TextBlock.foreground (SolidColorBrush textLight)
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.fontWeight FontWeight.SemiBold
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
                Border.background (SolidColorBrush (Color.FromArgb(160uy, 0uy, 0uy, 0uy)))
                Border.child (
                    Border.create [
                        Border.background (SolidColorBrush Colors.White)
                        Border.cornerRadius 8.0
                        Border.padding 20.0
                        Border.width 420.0
                        Border.horizontalAlignment HorizontalAlignment.Center
                        Border.verticalAlignment VerticalAlignment.Center
                        Border.child (
                            StackPanel.create [
                                StackPanel.spacing 16.0
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text dialog.Title
                                        TextBlock.foreground (SolidColorBrush textDark)
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.fontSize 16.0
                                    ]
                                    TextBlock.create [
                                        TextBlock.text dialog.Message
                                        TextBlock.foreground (SolidColorBrush textDark)
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.horizontalAlignment HorizontalAlignment.Right
                                        StackPanel.spacing 10.0
                                        StackPanel.children [
                                            Button.create [
                                                Button.content dialog.CancelText
                                                Button.background (SolidColorBrush Colors.White)
                                                Button.foreground (SolidColorBrush textDark)
                                                Button.borderBrush (SolidColorBrush borderGray)
                                                Button.borderThickness 1.0
                                                Button.cornerRadius 4.0
                                                Button.padding (12.0, 6.0)
                                                Button.onClick (fun _ -> dispatch DismissConfirm)
                                            ]
                                            Button.create [
                                                Button.content dialog.ConfirmText
                                                Button.background (SolidColorBrush primaryColor)
                                                Button.foreground (SolidColorBrush textLight)
                                                Button.cornerRadius 4.0
                                                Button.padding (12.0, 6.0)
                                                Button.fontWeight FontWeight.Bold
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
    // メインビュー
    // ==========================================
    let view (model: Model) (dispatch: Msg -> unit) =
        Grid.create [
            Grid.background (SolidColorBrush Colors.White)
            Grid.children [
                DockPanel.create [
                    DockPanel.children [
                        errorBanner model dispatch
                        dockerStatusBar model dispatch
                        controlPanel model dispatch
                        footerActions model dispatch
                        comparisonList model dispatch
                    ]
                ]
                confirmDialog model dispatch
            ]
        ]
