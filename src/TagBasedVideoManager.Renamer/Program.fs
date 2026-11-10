namespace TagBasedVideoManager.Renamer

open System
open System.Reflection
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Elmish
open Elmish

/// <summary>
/// タイトルバー文字列の生成およびアセンブリバージョン解決を行うモジュール。
/// </summary>
module TitleFormatter =

    /// <summary>
    /// Version オブジェクトからウィンドウタイトル文字列を生成する純粋関数。
    /// 未設定 (None) の場合は安全なデフォルト "TagBasedVideoManager - AI File Renamer v0.0.0" を返却する。
    /// </summary>
    let formatTitle (versionOpt: Version option) : string =
        match versionOpt with
        | Some v -> sprintf "TagBasedVideoManager - AI File Renamer v%d.%d.%d" v.Major v.Minor (max 0 v.Build)
        | None -> "TagBasedVideoManager - AI File Renamer v0.0.0"

    /// <summary>
    /// 実行中のアセンブリからタイトル文字列を取得する。
    /// </summary>
    let getWindowTitle () : string =
        try
            let asm = Assembly.GetExecutingAssembly()
            let ver = asm.GetName().Version
            formatTitle (Option.ofObj ver)
        with _ ->
            formatTitle None

type MainWindow() as this =
    inherit HostWindow()
    do
        let title = TitleFormatter.getWindowTitle ()
        base.Title <- title
        printfn "%s started" title

        base.Width <- 1100.0
        base.Height <- 720.0
        base.MinWidth <- 450.0
        base.MinHeight <- 400.0
        base.Background <- Media.SolidColorBrush(Media.Color.Parse("#1a1a1a"))

        Program.mkProgram State.init State.update Views.view
        |> Program.withHost this
        |> Program.runWithAvaloniaSyncDispatch ()

type App() =
    inherit Application()
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

module Program =

    let formatTitle = TitleFormatter.formatTitle
    let getWindowTitle = TitleFormatter.getWindowTitle

    [<EntryPoint>]
    let main args =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args)
