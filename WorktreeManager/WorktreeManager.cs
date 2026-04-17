// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using CliWrap;
using Spectre.Console;
using static Interaction;

AnsiConsole.WriteLine();

var pwd = Directory.GetCurrentDirectory();
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
if (string.IsNullOrEmpty(pwd))
{
    PrintError("Could not determine working directory");
    return;
}

// Fetch branches, worktrees, and PRs concurrently
var branchesTask = Git.GetBranches(pwd);
var worktreesTask = Git.GetWorktrees(pwd);
var prsTask = GitHub.GetPullRequests(pwd);

var branchesResult = await branchesTask;
if (branchesResult is Result<Branch[]>.Error(var err))
{
    PrintError(err);
    return;
}
var branches = ((Result<Branch[]>.Ok)branchesResult).Value;

var worktrees = (await worktreesTask).UnwrapOr([]);
worktrees = await Git.EnrichWithDirtyState(worktrees);

var prs = await prsTask;

// Compose work items
var workItems = WorkItem.Compose(branches, worktrees, prs);
if (workItems.Length == 0)
{
    PrintError("No branches found");
    return;
}

// Select a work item
var selectedResult = ChooseOne(workItems, FormatWorkItem, "My Work:");
if (selectedResult is Result<WorkItem>.Error)
    return;
var selected = ((Result<WorkItem>.Ok)selectedResult).Value;

// Show context-aware actions
var actions = BuildActions(pwd, selected);
if (actions.Length == 0)
    return;

await ChooseOne(actions, a => a.Name, "Action:")
    .BindAsync(a => a.Action())
    .Match(PrintOk, PrintError);

string FormatWorkItem(WorkItem item)
{
    var parts = new List<string>();

    var marker = item.Branch.IsCurrent ? "[green]*[/]" : " ";
    parts.Add($"{marker}[gray]({Markup.Escape(item.Branch.LastCommitDate)})[/]");
    parts.Add($"[bold]{Markup.Escape(item.Branch.Name)}[/]");

    if (item.Worktree is { } wt)
    {
        parts.Add($"[blue]{Markup.Escape(ShortenPath(wt.Path))}[/]");
        if (wt.IsDirty)
            parts.Add("[yellow][[dirty]][/]");
    }

    if (item.PullRequest is { } pr)
    {
        var (color, label) = pr.State switch
        {
            PullRequestState.Open => ("green", "OPEN"),
            PullRequestState.Merged => ("purple", "MERGED"),
            PullRequestState.Closed => ("red", "CLOSED"),
            _ => ("gray", "UNKNOWN"),
        };
        parts.Add($"[{color}]#{pr.Number} {Markup.Escape(pr.Title)} ({label})[/]");
    }

    if (item.Branch.UpstreamTrack is { } track)
        parts.Add($"[gray]{Markup.Escape(track)}[/]");
    else if (item.Branch.Upstream is not null)
        parts.Add($"[gray]≡ {Markup.Escape(item.Branch.Upstream)}[/]");

    return string.Join("  ", parts);
}

static MenuOption[] BuildActions(string pwd, WorkItem item)
{
    var actions = new List<MenuOption>();

    actions.Add(new("Copy branch name", () => CopyToClipboard(item.Branch.Name)));

    if (item.Worktree is { } wt)
    {
        actions.Add(new("Copy worktree path", () => CopyToClipboard(wt.Path)));
        actions.Add(new("Open in VS Code", () => RunCommand("code", wt.Path)));
        actions.Add(new("Open in file browser", () => RunCommand("open", wt.Path)));

        if (!item.Branch.IsCurrent)
        {
            actions.Add(new("Remove worktree (keep branch)", () => Git.RemoveWorktree(pwd, wt)));
            actions.Add(
                new(
                    "Remove worktree + delete branch",
                    async () =>
                    {
                        var removeResult = await Git.RemoveWorktree(pwd, wt);
                        if (removeResult is Result<string>.Error)
                            return removeResult;
                        return await Git.RemoveBranch(pwd, item.Branch);
                    }
                )
            );
        }
    }
    else if (!item.Branch.IsCurrent)
    {
        actions.Add(new("Switch to branch", () => Git.SwitchBranch(pwd, item.Branch.Name)));
    }

    if (item.PullRequest is { } pr)
    {
        actions.Add(new("Open PR in browser", () => RunCommand("open", pr.Url)));
        actions.Add(new("Copy PR URL", () => CopyToClipboard(pr.Url)));
    }

    if (!item.Branch.IsCurrent && item.Worktree is null)
    {
        actions.Add(new("Delete branch", () => Git.RemoveBranch(pwd, item.Branch)));
    }

    actions.Add(new("Exit", () => Task.FromResult(Result<string>.Success("Exiting"))));

    return [.. actions];
}

static async Task<Result<string>> CopyToClipboard(string text)
{
    try
    {
        await Cli.Wrap("pbcopy").WithStandardInputPipe(PipeSource.FromString(text)).ExecuteAsync();
        return Result<string>.Success($"Copied: {text}");
    }
    catch (Exception ex)
    {
        return Result<string>.Failure(ex.Message);
    }
}

static async Task<Result<string>> RunCommand(string app, string target)
{
    try
    {
        await Cli.Wrap(app).WithArguments([target]).ExecuteAsync();
        return Result<string>.Success($"Opened: {target}");
    }
    catch (Exception ex)
    {
        return Result<string>.Failure(ex.Message);
    }
}

string ShortenPath(string path)
{
    return !string.IsNullOrEmpty(home) && path.StartsWith(home) ? "~" + path[home.Length..] : path;
}

public record Branch(
    string Name,
    bool IsCurrent,
    string LastCommit,
    string LastCommitDate,
    string? Upstream = null,
    string? UpstreamTrack = null
);

public record Worktree(string Path, string Branch, bool IsDirty = false);

public enum PullRequestState
{
    Open,
    Merged,
    Closed,
}

public record PullRequest(
    int Number,
    string Title,
    PullRequestState State,
    string HeadBranch,
    string Url
);

public record WorkItem(Branch Branch, Worktree? Worktree, PullRequest? PullRequest)
{
    public static WorkItem[] Compose(
        Branch[] branches,
        Worktree[] worktrees,
        PullRequest[] pullRequests
    )
    {
        var worktreeByBranch = worktrees
            .Where(wt => !string.IsNullOrEmpty(wt.Branch))
            .GroupBy(wt => wt.Branch)
            .ToDictionary(g => g.Key, g => g.First());

        // When multiple PRs exist for the same branch, prefer the open one
        var prByBranch = pullRequests
            .GroupBy(pr => pr.HeadBranch)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(pr => pr.State == PullRequestState.Open ? 0 : 1).First()
            );

        return branches
            .Select(branch => new WorkItem(
                Branch: branch,
                Worktree: worktreeByBranch.GetValueOrDefault(branch.Name),
                PullRequest: prByBranch.GetValueOrDefault(branch.Name)
            ))
            .ToArray();
    }
}

public record MenuOption(string Name, Func<Task<Result<string>>> Action);
