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

        Program.mkProgram State.init State.update Views.view
        |> Program.withHost this
        |> Program.run

type App() =
    inherit Application()
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
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
