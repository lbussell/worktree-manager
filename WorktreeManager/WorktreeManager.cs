// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using Spectre.Console;
using static Interaction;

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
        .BindAsync(Git.GetBranches)
        .Bind(branches => ChooseOne(branches, FormatBranchSpectreConsole, "Select a branch:"))
        .Map(branch => branch.Name);

static async Task<Result<string>> ChooseWorktree() =>
    await GetWorkingDirectory()
        .BindAsync(Git.GetWorktrees)
        .Bind(worktrees => ChooseOne(worktrees, FormatWorktree, "Select a worktree:"))
        .Map(wt => wt.Path);

static async Task<Result<Branch[]>> ChooseBranches(string? title = null) =>
    await GetWorkingDirectory()
        .BindAsync(Git.GetBranches)
        .Bind(branches =>
            ChooseOneOrMore(
                branches.Where(b => !b.IsCurrent).ToArray(),
                FormatBranchSpectreConsole,
                title ?? "Select branches:"
            )
        );

static async Task<Result<Worktree[]>> ChooseWorktrees(string? title = null) =>
    await GetWorkingDirectory()
        .BindAsync(Git.GetWorktrees)
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
    await GetWorkingDirectory()
        .BindAsync(async dir =>
            await Git.GetBranches(dir)
                .Bind(branches =>
                    ChooseOneOrMore(
                        branches.Where(b => !b.IsCurrent).ToArray(),
                        FormatBranchSpectreConsole,
                        "Select branches to remove:"
                    )
                )
                .BindEach(branch => Git.RemoveBranch(dir, branch))
                .Sequence()
                .Map(names => $"Removed {names.Length} branch(es): {string.Join(", ", names)}")
        );

static async Task<Result<string>> CleanupWorktrees() =>
    await GetWorkingDirectory()
        .BindAsync(async dir =>
            await Git.GetWorktrees(dir)
                .Bind(worktrees =>
                    ChooseOneOrMore(worktrees, FormatWorktree, "Select worktrees to remove:")
                )
                .BindEach(wt => Git.RemoveWorktree(dir, wt))
                .Sequence()
                .Map(names => $"Removed {names.Length} worktree(s): {string.Join(", ", names)}")
        );

static Result<string> GetWorkingDirectory()
{
    var dir = Directory.GetCurrentDirectory();
    return string.IsNullOrEmpty(dir)
        ? Result<string>.Failure("Could not determine working directory")
        : Result<string>.Success(dir);
}

static string FormatBranchSpectreConsole(Branch b) =>
    $"{(b.IsCurrent ? "*" : "")}({Markup.Escape(b.LastCommitDate)}) {Markup.Escape(b.Name)} [gray]{Spectre.Console.Markup.Escape(b.LastCommit)}[/]";

static string FormatWorktree(Worktree wt) =>
    $"{Markup.Escape(wt.Path)} [blue]{Markup.Escape(wt.Branch)}[/]";

public record Branch(string Name, bool IsCurrent, string LastCommit, string LastCommitDate);

public record Worktree(string Path, string Branch);

public record MenuOption(string Name, Func<Task<Result<string>>> Action);
