// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using Spectre.Console;

public static class Interaction
{
    public static Result<T> ChooseOne<T>(
        T[] choices,
        Func<T, string> displayConverter,
        string? title = null
    )
        where T : notnull
    {
        if (choices.Length == 0)
            return Result<T>.Failure("No choices available");

        var prompt = new SelectionPrompt<T>().UseConverter(displayConverter).AddChoices(choices);

        if (!string.IsNullOrEmpty(title))
            prompt.Title(title);

        var selected = AnsiConsole.Prompt(prompt);

        return Result<T>.Success(selected);
    }

    public static Result<T[]> ChooseOneOrMore<T>(
        T[] choices,
        Func<T, string> displayConverter,
        string? title = null
    )
        where T : notnull
    {
        if (choices.Length == 0)
            return Result<T[]>.Failure("No choices available");

        var prompt = new MultiSelectionPrompt<T>()
            .UseConverter(displayConverter)
            .AddChoices(choices);

        if (!string.IsNullOrEmpty(title))
            prompt.Title(title);

        var selected = AnsiConsole.Prompt(prompt);

        return selected.Count == 0
            ? Result<T[]>.Failure("Nothing selected")
            : Result<T[]>.Success([.. selected]);
    }

    public static void PrintOk(string result) =>
        AnsiConsole.MarkupLineInterpolated($"[green]OK[/]: {result}");

    public static void PrintError(string message) =>
        AnsiConsole.MarkupLineInterpolated($"[red]Error[/]: {message}");
}
