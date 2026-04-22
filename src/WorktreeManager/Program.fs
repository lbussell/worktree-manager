open System
open Spectre.Console

[<EntryPoint>]
let main _ =
    let shouldContinue =
        if Console.IsInputRedirected then
            AnsiConsole.MarkupLine("[grey]Non-interactive input detected; defaulting to no.[/]")
            false
        else
            AnsiConsole.Confirm("Would you like to continue?")

    if shouldContinue then
        AnsiConsole.MarkupLine("[green]Great! You answered yes.[/]")
    else
        AnsiConsole.MarkupLine("[yellow]Okay, you answered no.[/]")

    0
