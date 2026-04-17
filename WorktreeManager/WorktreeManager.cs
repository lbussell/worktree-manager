// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

AnsiConsole.WriteLine();

MenuOption[] menuOptions =
[
    new("Branches", ChooseBranch),
    new("Worktrees", ChooseWorktree),
    new("Cleanup", Cleanup),
    new("Exit", () => Task.FromResult(Result<string>.Success("Exiting"))),
];

await ChooseOne(menuOptions, o => o.Name)
    .BindAsync(option => option.Action())
    .Match(PrintOk, PrintError);

static async Task<Result<string>> ChooseBranch() =>
    await GetWorkingDirectory()
        .BindAsync(GetGitBranches)
        .Bind(branches => ChooseOne(branches, FormatBranchSpectreConsole, "Select a branch:"))
        .Map(branch => branch.Name);

static async Task<Result<string>> ChooseWorktree() =>
    await GetWorkingDirectory()
        .BindAsync(GetGitWorktrees)
        .Bind(worktrees => ChooseOne(worktrees, FormatWorktree, "Select a worktree:"))
        .Map(wt => wt.Path);

static async Task<Result<Branch[]>> ChooseBranches(string? title = null) =>
    await GetWorkingDirectory()
        .BindAsync(GetGitBranches)
        .Bind(branches =>
            ChooseOneOrMore(
                branches.Where(b => !b.IsCurrent).ToArray(),
                FormatBranchSpectreConsole,
                title ?? "Select branches:"
            )
        );

static async Task<Result<Worktree[]>> ChooseWorktrees(string? title = null) =>
    await GetWorkingDirectory()
        .BindAsync(GetGitWorktrees)
        .Bind(worktrees =>
            ChooseOneOrMore(worktrees, FormatWorktree, title ?? "Select worktrees:")
        );

static async Task<Result<string>> Cleanup() =>
    await ChooseOne<MenuOption>(
            [new("Branches", CleanupBranches), new("Worktrees", CleanupWorktrees)],
            o => o.Name,
            "Clean up:"
        )
        .BindAsync(option => option.Action());

static async Task<Result<string>> CleanupBranches() =>
    await ChooseBranches("Select branches to remove:")
        .BindEach(RemoveBranch)
        .Sequence()
        .Map(names => $"Removed {names.Length} branch(es): {string.Join(", ", names)}");

static async Task<Result<string>> CleanupWorktrees() =>
    await ChooseWorktrees("Select worktrees to remove:")
        .BindEach(RemoveWorktree)
        .Sequence()
        .Map(names => $"Removed {names.Length} worktree(s): {string.Join(", ", names)}");

static async Task<Result<string>> RemoveBranch(Branch branch) =>
    await GetWorkingDirectory()
        .BindAsync(async dir =>
        {
            try
            {
                await Cli.Wrap("git")
                    .WithWorkingDirectory(dir)
                    .WithArguments(["branch", "-d", branch.Name])
                    .ExecuteBufferedAsync();
                return Result<string>.Success(branch.Name);
            }
            catch (Exception ex)
            {
                return Result<string>.Failure($"{branch.Name}: {ex.Message}");
            }
        });

static async Task<Result<string>> RemoveWorktree(Worktree wt) =>
    await GetWorkingDirectory()
        .BindAsync(async dir =>
        {
            try
            {
                await Cli.Wrap("git")
                    .WithWorkingDirectory(dir)
                    .WithArguments(["worktree", "remove", wt.Path])
                    .ExecuteBufferedAsync();
                return Result<string>.Success(wt.Branch);
            }
            catch (Exception ex)
            {
                return Result<string>.Failure($"{wt.Branch}: {ex.Message}");
            }
        });

static Result<string> GetWorkingDirectory()
{
    var dir = Directory.GetCurrentDirectory();
    return string.IsNullOrEmpty(dir)
        ? Result<string>.Failure("Could not determine working directory")
        : Result<string>.Success(dir);
}

static async Task<Result<Branch[]>> GetGitBranches(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(
                [
                    "branch",
                    "--sort=-committerdate",
                    "--format=%(HEAD)|%(refname:short)|%(creatordate:relative)|%(subject)",
                ]
            )
            .ExecuteBufferedAsync();

        return GitParsing.ParseBranches(result.StandardOutput);
    }
    catch (Exception ex)
    {
        return Result<Branch[]>.Failure(ex.Message);
    }
}

static async Task<Result<Worktree[]>> GetGitWorktrees(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(["worktree", "list", "--porcelain"])
            .ExecuteBufferedAsync();

        return GitParsing.ParseWorktrees(result.StandardOutput);
    }
    catch (Exception ex)
    {
        return Result<Worktree[]>.Failure(ex.Message);
    }
}

static Result<T> ChooseOne<T>(T[] choices, Func<T, string> displayConverter, string? title = null)
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

static Result<T[]> ChooseOneOrMore<T>(
    T[] choices,
    Func<T, string> displayConverter,
    string? title = null
)
    where T : notnull
{
    if (choices.Length == 0)
        return Result<T[]>.Failure("No choices available");

    var prompt = new MultiSelectionPrompt<T>().UseConverter(displayConverter).AddChoices(choices);

    if (!string.IsNullOrEmpty(title))
        prompt.Title(title);

    var selected = AnsiConsole.Prompt(prompt);

    return selected.Count == 0
        ? Result<T[]>.Failure("Nothing selected")
        : Result<T[]>.Success([.. selected]);
}

static string FormatBranchSpectreConsole(Branch b) =>
    $"{(b.IsCurrent ? "*" : "")}({Markup.Escape(b.LastCommitDate)}) {Markup.Escape(b.Name)} [gray]{Spectre.Console.Markup.Escape(b.LastCommit)}[/]";

static string FormatWorktree(Worktree wt) =>
    $"{Markup.Escape(wt.Path)} [blue]{Markup.Escape(wt.Branch)}[/]";

static void PrintOk(string result) => AnsiConsole.MarkupLineInterpolated($"[green]OK[/]: {result}");

static void PrintError(string message) =>
    AnsiConsole.MarkupLineInterpolated($"[red]Error[/]: {message}");

public record Branch(string Name, bool IsCurrent, string LastCommit, string LastCommitDate);

public record Worktree(string Path, string Branch);

public record MenuOption(string Name, Func<Task<Result<string>>> Action);
