// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using Spectre.Console;
using static Interaction;

AnsiConsole.WriteLine();

var dir = Directory.GetCurrentDirectory();
if (string.IsNullOrEmpty(dir))
{
    PrintError("Could not determine working directory");
    return;
}

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

async Task<Result<string>> ChooseBranch() =>
    await Git.GetBranches(dir)
        .Bind(branches => ChooseOne(branches, FormatBranchSpectreConsole, "Select a branch:"))
        .Map(branch => branch.Name);

async Task<Result<string>> ChooseWorktree() =>
    await Git.GetWorktrees(dir)
        .Bind(worktrees => ChooseOne(worktrees, FormatWorktree, "Select a worktree:"))
        .Map(wt => wt.Path);

async Task<Result<Branch[]>> ChooseBranches(string? title = null) =>
    await Git.GetBranches(dir)
        .Bind(branches =>
            ChooseOneOrMore(
                branches.Where(b => !b.IsCurrent).ToArray(),
                FormatBranchSpectreConsole,
                title ?? "Select branches:"
            )
        );

async Task<Result<Worktree[]>> ChooseWorktrees(string? title = null) =>
    await Git.GetWorktrees(dir)
        .Bind(worktrees =>
            ChooseOneOrMore(worktrees, FormatWorktree, title ?? "Select worktrees:")
        );

async Task<Result<string>> Cleanup() =>
    await ChooseOne<MenuOption>(
            [new("Branches", CleanupBranches), new("Worktrees", CleanupWorktrees)],
            o => o.Name,
            "Clean up:"
        )
        .BindAsync(option => option.Action());

async Task<Result<string>> CleanupBranches() =>
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
        .Map(names => $"Removed {names.Length} branch(es): {string.Join(", ", names)}");

async Task<Result<string>> CleanupWorktrees() =>
    await Git.GetWorktrees(dir)
        .Bind(worktrees =>
            ChooseOneOrMore(worktrees, FormatWorktree, "Select worktrees to remove:")
        )
        .BindEach(wt => Git.RemoveWorktree(dir, wt))
        .Sequence()
        .Map(names => $"Removed {names.Length} worktree(s): {string.Join(", ", names)}");

static string FormatBranchSpectreConsole(Branch b) =>
    $"{(b.IsCurrent ? "*" : "")}({Markup.Escape(b.LastCommitDate)}) {Markup.Escape(b.Name)} [gray]{Spectre.Console.Markup.Escape(b.LastCommit)}[/]";

static string FormatWorktree(Worktree wt) =>
    $"{Markup.Escape(wt.Path)} [blue]{Markup.Escape(wt.Branch)}[/]";

public record Branch(string Name, bool IsCurrent, string LastCommit, string LastCommitDate);

public record Worktree(string Path, string Branch);

public record MenuOption(string Name, Func<Task<Result<string>>> Action);
