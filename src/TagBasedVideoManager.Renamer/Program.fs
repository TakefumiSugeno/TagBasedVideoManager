namespace TagBasedVideoManager.Renamer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Elmish
open Elmish

type MainWindow() as this =
    inherit HostWindow()
    do
        base.Title <- "TagBasedVideoManager - AI File Renamer"
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
    [<EntryPoint>]
    let main args =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args)
