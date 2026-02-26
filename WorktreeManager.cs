// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

// Manages git worktrees across repositories in a source directory.
//
// Usage:
//   wt add <repodir>              Register a repo for worktree management
//   wt list                       List managed repos and their worktrees
//   wt create <repo> <name>       Create a new worktree (--branch/-b, --from/-f)
//   wt create <repo> --pr <num>   Create a worktree and check out a PR
//   wt remove <ref>               Remove a worktree or untrack a repo
//   wt dir <ref>                  Print the path to a repo or worktree
//
// Useful shell functions (defined in .zshrc):
//   wtcd <id>                     pushd to the repo or worktree directory
//   wtcp <id>                     pushd and start copilot --yolo
//   wtcp <id> "<prompt>"          pushd and run copilot --yolo -i "<prompt>"
//
//   alias wt="/path/to/WorktreeManager"
//   wtcd() { pushd "$(wt d "$1")" }
//   wtcp() { pushd "$(wt d "$1")" && if [[ -n "$2" ]]; then copilot --yolo -i "$2"; else copilot --yolo; fi }
//
// All commands accept short IDs (shown in 'wt list') in place of full names.
// Short IDs are an FNV-1a 32-bit hash of the repo/worktree name, so they are unique and stable.
// Configuration: change SourceRoot in the Config class below.

#:package ConsoleAppFramework@*
#:package CliWrap@*
#:package Spectre.Console@*

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleAppFramework;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using Spectre.Console.Rendering;

var app = ConsoleApp.Create();
app.Add<WorktreeCommands>();
app.Run(args);

static class Config
{
    // ---- Change this to your source directory ----
    const string SourceRoot = "~/src";

    public static readonly string SourceRootPath =
        SourceRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static readonly string WorktreesFilePath =
        Path.Combine(SourceRootPath, "worktrees.txt");
}

/// <summary>Manage git worktrees across repositories.</summary>
public class WorktreeCommands
{
    static readonly CliWrapper Git = new("git");
    static readonly CliWrapper GitHubCli = new("gh");

    /// <summary>Register a repository for worktree management.</summary>
    /// <param name="repoDirectory">Repository directory relative to the source root.</param>
    [Command("add|a")]
    public async Task Add([Argument] string repoDirectory)
    {
        string repositoryPath = Path.Combine(Config.SourceRootPath, repoDirectory);

        if (!Directory.Exists(repositoryPath))
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Directory does not exist: [dim]{Markup.Escape(repositoryPath)}[/]\n"));
            Environment.ExitCode = 1;
            return;
        }

        BufferedCommandResult gitResult = await Git.RunAsync(
            ["rev-parse", "--git-dir"],
            workingDirectory: repositoryPath,
            silent: true);

        if (gitResult.ExitCode != 0)
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Not a git repository: [dim]{Markup.Escape(repositoryPath)}[/]\n"));
            Environment.ExitCode = 1;
            return;
        }

        IReadOnlyList<Worktree> managedRepositories = ReadWorktreesFile();
        if (managedRepositories.Any(repository => repository.RepositoryDirectory == repoDirectory))
        {
            var existingWorktree = new Worktree(repoDirectory);
            AnsiConsole.Write(new Markup($"Already registered: [purple]{existingWorktree.Id}[/] {Markup.Escape(repoDirectory)}\n"));
            return;
        }

        File.AppendAllLines(Config.WorktreesFilePath, [repoDirectory]);

        var worktree = new Worktree(repoDirectory);
        Directory.CreateDirectory(worktree.WorktreesDirectoryPath);

        AnsiConsole.Write(new Markup($"Added [purple]{worktree.Id}[/] {Markup.Escape(repoDirectory)}\n"));
    }

    /// <summary>List all managed repositories and their worktrees.</summary>
    [Command("list|l")]
    public async Task List()
    {
        IReadOnlyList<Worktree> managedRepositories = ReadWorktreesFile();

        if (managedRepositories.Count == 0)
        {
            AnsiConsole.Write(new Markup("[yellow]No repositories registered.[/] Use 'add' to register one.\n"));
            return;
        }

        // Collect all directories and kick off status queries concurrently
        var directories = managedRepositories.SelectMany(repository =>
            new[] { new DirectoryEntry(repository.RepositoryDirectory, WorktreeName: null, DirectoryPath: repository.FullPath) }
                .Concat(!Directory.Exists(repository.WorktreesDirectoryPath) ? [] :
                    Directory.GetDirectories(repository.WorktreesDirectoryPath)
                        .Select(directory => new DirectoryEntry(repository.RepositoryDirectory, Path.GetFileName(directory), directory))))
            .ToList();

        var statusTasks = directories.ToDictionary(
            directory => directory,
            directory => GetGitStatusAsync(directory.DirectoryPath));
        var pullRequestTasks = managedRepositories.ToDictionary(
            repository => repository.RepositoryDirectory,
            repository => GetPullRequestsAsync(repository.FullPath));
        await Task.WhenAll(statusTasks.Values.Cast<Task>().Concat(pullRequestTasks.Values));

        // Compose view declaratively
        var renderedRows = new List<IRenderable>();
        foreach (Worktree repository in managedRepositories)
        {
            var pullRequests = pullRequestTasks[repository.RepositoryDirectory].Result;
            var repositoryKey = directories.First(
                directory => directory.RepositoryDirectory == repository.RepositoryDirectory && directory.WorktreeName is null);

            var worktreeStatuses = new List<WorktreeStatus>();

            if (Directory.Exists(repository.WorktreesDirectoryPath))
            {
                foreach (string worktreeDirectory in Directory.GetDirectories(repository.WorktreesDirectoryPath))
                {
                    string worktreeName = Path.GetFileName(worktreeDirectory);
                    var worktreeKey = directories.First(
                        directory => directory.RepositoryDirectory == repository.RepositoryDirectory && directory.WorktreeName == worktreeName);
                    worktreeStatuses.Add(new WorktreeStatus(worktreeName, statusTasks[worktreeKey].Result));
                }
            }

            renderedRows.Add(ListRenderer.RenderRepositoryEntry(
                repository, statusTasks[repositoryKey].Result, worktreeStatuses, pullRequests));
        }

        AnsiConsole.Write(new Rows(renderedRows));
    }

    /// <summary>Create a new worktree for a managed repository.</summary>
    /// <param name="repoReference">Repo name or repo ID.</param>
    /// <param name="worktreeName">Name for the new worktree. Auto-generated when using --pr.</param>
    /// <param name="createBranch">-b, Create a new branch matching the worktree name.</param>
    /// <param name="baseRef">-f, Base ref for the new branch (requires --createBranch).</param>
    /// <param name="pullRequestNumber">-p, PR number to check out in the new worktree.</param>
    [Command("create|new|n")]
    public async Task Create(
        [Argument] string repoReference,
        [Argument] string? worktreeName = null,
        bool createBranch = false,
        string? baseRef = null,
        int? pullRequestNumber = null)
    {
        if (baseRef is not null && !createBranch)
        {
            Console.Error.WriteLine("Error: --baseRef/-f requires --createBranch/-b.");
            Environment.ExitCode = 1;
            return;
        }

        if (pullRequestNumber is not null && (createBranch || baseRef is not null))
        {
            Console.Error.WriteLine("Error: --pullRequestNumber/-p cannot be combined with --createBranch/-b or --baseRef/-f.");
            Environment.ExitCode = 1;
            return;
        }

        if (worktreeName is null && pullRequestNumber is null)
        {
            Console.Error.WriteLine("Error: worktreeName is required unless --pullRequestNumber/-p is specified.");
            Environment.ExitCode = 1;
            return;
        }

        worktreeName ??= $"pr-{pullRequestNumber}";

        IReadOnlyList<Worktree> managedRepositories = ReadWorktreesFile();

        string repositoryDirectory;
        if (TryResolveRepositoryReference(repoReference, managedRepositories, out string resolvedDirectory))
            repositoryDirectory = resolvedDirectory;
        else
            repositoryDirectory = repoReference;

        if (!managedRepositories.Any(repository => repository.RepositoryDirectory == repositoryDirectory))
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Repository not managed: [dim]{Markup.Escape(repoReference)}[/]. Use 'add' first.\n"));
            Environment.ExitCode = 1;
            return;
        }

        var worktree = new Worktree(repositoryDirectory);
        string worktreePath = Path.Combine(worktree.WorktreesDirectoryPath, worktreeName);

        List<string> gitArguments = ["worktree", "add"];

        if (createBranch)
        {
            gitArguments.AddRange(["-b", worktreeName, worktreePath]);
            if (baseRef is not null)
                gitArguments.Add(baseRef);
        }
        else
        {
            gitArguments.AddRange(["--detach", worktreePath]);
        }

        BufferedCommandResult gitResult = await Git.RunAsync(
            [.. gitArguments],
            workingDirectory: worktree.FullPath);

        if (gitResult.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.Write(new Markup($"Created worktree: [blue]{worktree.ComputeWorktreeId(worktreeName)}[/] {Markup.Escape(worktreePath)}\n"));

        if (pullRequestNumber is not null)
        {
            BufferedCommandResult checkoutResult = await GitHubCli.RunAsync(
                ["pr", "checkout", pullRequestNumber.Value.ToString()],
                workingDirectory: worktreePath);

            if (checkoutResult.ExitCode != 0)
            {
                if (AnsiConsole.Confirm("PR checkout failed (branches may have diverged). Force checkout?", defaultValue: false))
                {
                    checkoutResult = await GitHubCli.RunAsync(
                        ["pr", "checkout", pullRequestNumber.Value.ToString(), "--force"],
                        workingDirectory: worktreePath);

                    if (checkoutResult.ExitCode != 0)
                    {
                        Environment.ExitCode = 1;
                        return;
                    }

                    AnsiConsole.Write(new Markup($"Created worktree: [blue]{worktree.ComputeWorktreeId(worktreeName)}[/] {Markup.Escape(worktreePath)}\n"));
                }
                else
                {
                    AnsiConsole.Write(new Markup("Worktree created but PR not checked out.\n"));
                    Environment.ExitCode = 1;
                    return;
                }
            }
        }
    }

    /// <summary>Print the path to a repo or worktree.</summary>
    /// <param name="reference">Repo name, repo ID, or worktree ID.</param>
    [Command("dir|d")]
    public void Dir([Argument] string reference)
    {
        IReadOnlyList<Worktree> managedRepositories = ReadWorktreesFile();

        if (!TryResolveReference(reference, managedRepositories, out ResolvedRef resolved))
        {
            Console.Error.WriteLine($"Error: Could not resolve ref: {reference}.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine(resolved.FullPath);
    }

    /// <summary>Remove a worktree, or untrack a repository.</summary>
    /// <param name="repoReference">Repo name, repo ID, or worktree ID.</param>
    /// <param name="worktreeName">Worktree to remove. If omitted, ref is resolved automatically.</param>
    [Command("remove|rm")]
    public async Task Remove([Argument] string repoReference, [Argument] string? worktreeName = null)
    {
        IReadOnlyList<Worktree> managedRepositories = ReadWorktreesFile();

        ResolvedRef resolved;

        if (worktreeName is not null)
        {
            // Two args: first is repo ref, second is worktree name
            string repositoryDirectory;
            if (TryResolveRepositoryReference(repoReference, managedRepositories, out string resolvedDirectory))
                repositoryDirectory = resolvedDirectory;
            else
                repositoryDirectory = repoReference;

            if (!managedRepositories.Any(repository => repository.RepositoryDirectory == repositoryDirectory))
            {
                Console.Error.WriteLine($"Error: Repository not managed: {repoReference}.");
                Environment.ExitCode = 1;
                return;
            }

            resolved = new ResolvedRef(repositoryDirectory, worktreeName);
        }
        else
        {
            // Single arg: resolve as worktree ID, repo ID, or repo name
            if (!TryResolveReference(repoReference, managedRepositories, out resolved))
            {
                Console.Error.WriteLine($"Error: Could not resolve ref: {repoReference}.");
                Environment.ExitCode = 1;
                return;
            }
        }

        if (resolved.IsRepository)
        {
            // Remove repo from tracking
            string[] remainingDirectories = managedRepositories
                .Where(repository => repository.RepositoryDirectory != resolved.RepositoryDirectory)
                .Select(repository => repository.RepositoryDirectory)
                .ToArray();
            File.WriteAllLines(Config.WorktreesFilePath, remainingDirectories);
            var removedWorktree = new Worktree(resolved.RepositoryDirectory);
            AnsiConsole.Write(new Markup($"Removed from tracking: [purple]{removedWorktree.Id}[/] {Markup.Escape(resolved.RepositoryDirectory)}\n"));
            return;
        }

        // Remove individual worktree
        var worktree = new Worktree(resolved.RepositoryDirectory);
        string worktreePath = resolved.FullPath;

        if (!Directory.Exists(worktreePath))
        {
            Console.Error.WriteLine($"Error: Worktree does not exist: {worktreePath}");
            Environment.ExitCode = 1;
            return;
        }

        BufferedCommandResult gitResult = await Git.RunAsync(
            ["worktree", "remove", worktreePath],
            workingDirectory: worktree.FullPath);

        if (gitResult.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.Write(new Markup($"Removed worktree: [blue]{worktree.ComputeWorktreeId(resolved.WorktreeName!)}[/] {Markup.Escape(worktreePath)}\n"));
    }

    static async Task<GitStatus?> GetGitStatusAsync(string workingDirectory)
    {
        BufferedCommandResult statusResult = await Git.RunAsync(
            ["status", "-b", "--porcelain"],
            workingDirectory: workingDirectory,
            silent: true);

        if (statusResult.ExitCode != 0)
            return null;

        string[] statusLines = statusResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (statusLines.Length == 0)
            return null;

        // Parse branch line: ## branch...upstream [ahead N, behind M]
        string branchLine = statusLines[0];
        string branchName = "HEAD";
        int ahead = 0, behind = 0;
        bool hasUpstream = false;

        if (branchLine.StartsWith("## ", StringComparison.Ordinal))
        {
            string branchInfo = branchLine[3..];
            int bracketIndex = branchInfo.IndexOf('[');
            string branchSegment = bracketIndex >= 0 ? branchInfo[..bracketIndex].Trim() : branchInfo.Trim();

            int separatorIndex = branchSegment.IndexOf("...", StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                branchName = branchSegment[..separatorIndex];
                hasUpstream = true;
            }
            else
            {
                branchName = branchSegment;
            }

            if (bracketIndex >= 0)
            {
                int closeBracketIndex = branchInfo.IndexOf(']', bracketIndex);
                if (closeBracketIndex >= 0)
                {
                    string trackingInfo = branchInfo[(bracketIndex + 1)..closeBracketIndex];
                    foreach (string trackingPart in trackingInfo.Split(',', StringSplitOptions.TrimEntries))
                    {
                        if (trackingPart.StartsWith("ahead ", StringComparison.Ordinal)
                            && int.TryParse(trackingPart[6..], out int aheadCount))
                            ahead = aheadCount;
                        else if (trackingPart.StartsWith("behind ", StringComparison.Ordinal)
                            && int.TryParse(trackingPart[7..], out int behindCount))
                            behind = behindCount;
                    }
                }
            }
        }

        int untrackedFiles = statusLines.Skip(1).Count(line => line.StartsWith("??", StringComparison.Ordinal));

        int additions = 0, deletions = 0;
        BufferedCommandResult diffResult = await Git.RunAsync(
            ["diff", "HEAD", "--numstat"],
            workingDirectory: workingDirectory,
            silent: true);

        if (diffResult.ExitCode == 0)
        {
            foreach (string diffLine in diffResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] columns = diffLine.Split('\t');
                if (columns.Length >= 2
                    && int.TryParse(columns[0], out int addedLines)
                    && int.TryParse(columns[1], out int deletedLines))
                {
                    additions += addedLines;
                    deletions += deletedLines;
                }
            }
        }

        return new GitStatus(branchName, ahead, behind, hasUpstream, additions, deletions, untrackedFiles);
    }

    static async Task<Dictionary<string, PullRequestInfo>> GetPullRequestsAsync(string workingDirectory)
    {
        BufferedCommandResult commandResult = await GitHubCli.RunAsync(
            ["pr", "list", "--state", "all", "--json", "number,title,headRefName,state"],
            workingDirectory: workingDirectory,
            silent: true);

        if (commandResult.ExitCode != 0)
            return new Dictionary<string, PullRequestInfo>(StringComparer.Ordinal);

        GitHubPullRequestDto[] pullRequestDtos;
        try
        {
            pullRequestDtos = JsonSerializer.Deserialize(commandResult.StandardOutput, GitHubJsonContext.Default.GitHubPullRequestDtoArray) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, PullRequestInfo>(StringComparer.Ordinal);
        }

        var pullRequestsByBranch = new Dictionary<string, PullRequestInfo>(StringComparer.Ordinal);
        foreach (GitHubPullRequestDto pullRequestDto in pullRequestDtos)
        {
            pullRequestsByBranch.TryAdd(
                pullRequestDto.HeadRefName,
                new PullRequestInfo(pullRequestDto.Number, pullRequestDto.Title, pullRequestDto.State));
        }
        return pullRequestsByBranch;
    }

    static IReadOnlyList<Worktree> ReadWorktreesFile()
    {
        if (!File.Exists(Config.WorktreesFilePath))
            return [];

        return File.ReadAllLines(Config.WorktreesFilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new Worktree(line))
            .ToArray();
    }

    /// <summary>
    /// Resolves a ref (short ID or exact name) to a (repo, worktree?) pair.
    /// Checks worktree IDs first, then repo IDs, then exact repo names.
    /// </summary>
    static bool TryResolveReference(
        string reference, IReadOnlyList<Worktree> managedRepositories, out ResolvedRef resolvedReference)
    {
        // Check worktree IDs
        foreach (Worktree repository in managedRepositories)
        {
            if (!Directory.Exists(repository.WorktreesDirectoryPath)) continue;
            foreach (string worktreeDirectory in Directory.GetDirectories(repository.WorktreesDirectoryPath))
            {
                string worktreeName = Path.GetFileName(worktreeDirectory);
                if (repository.ComputeWorktreeId(worktreeName) == reference)
                {
                    resolvedReference = new ResolvedRef(repository.RepositoryDirectory, worktreeName);
                    return true;
                }
            }
        }

        // Check repo IDs
        foreach (Worktree repository in managedRepositories)
        {
            if (repository.Id == reference)
            {
                resolvedReference = new ResolvedRef(repository.RepositoryDirectory, WorktreeName: null);
                return true;
            }
        }

        // Check exact repo names
        if (managedRepositories.Any(repository => repository.RepositoryDirectory == reference))
        {
            resolvedReference = new ResolvedRef(reference, WorktreeName: null);
            return true;
        }

        resolvedReference = default;
        return false;
    }

    /// <summary>Resolves a ref to a repo name only (ID or exact name).</summary>
    static bool TryResolveRepositoryReference(
        string reference, IReadOnlyList<Worktree> managedRepositories, out string repositoryDirectory)
    {
        foreach (Worktree repository in managedRepositories)
        {
            if (repository.Id == reference)
            {
                repositoryDirectory = repository.RepositoryDirectory;
                return true;
            }
        }

        if (managedRepositories.Any(repository => repository.RepositoryDirectory == reference))
        {
            repositoryDirectory = reference;
            return true;
        }

        repositoryDirectory = "";
        return false;
    }
}

static class ListRenderer
{
    const int NameColumnWidth = 40;

    public static IRenderable RenderRepositoryEntry(
        Worktree repository,
        GitStatus? repositoryStatus,
        List<WorktreeStatus> worktreeStatuses,
        Dictionary<string, PullRequestInfo> pullRequests)
    {
        var renderedLines = new List<IRenderable>
        {
            RenderRepositoryLine(repository, repositoryStatus, pullRequests)
        };
        foreach (WorktreeStatus worktreeStatus in worktreeStatuses)
            renderedLines.Add(RenderWorktreeLine(repository, worktreeStatus.Name, worktreeStatus.Status, pullRequests));
        return new Rows(renderedLines);
    }

    static Grid RenderRepositoryLine(
        Worktree repository, GitStatus? gitStatus, Dictionary<string, PullRequestInfo> pullRequests)
    {
        string formattedBranch = TruncateBranch(gitStatus?.Branch, 3 + 1 + repository.RepositoryDirectory.Length);
        var nameColumn = new Markup($"[purple]{repository.Id}[/] {Markup.Escape(repository.RepositoryDirectory)}{formattedBranch}");

        string gitIndicators = FormatGitIndicators(gitStatus);
        int statusColumnWidth = AnsiConsole.Profile.Width - NameColumnWidth;
        string pullRequestText = FormatPullRequestInfo(gitStatus?.Branch, pullRequests, statusColumnWidth - Markup.Remove(gitIndicators).Length);
        var statusColumn = new Markup($"{gitIndicators}{pullRequestText}");

        return new Grid()
            .AddColumn(new GridColumn { Width = NameColumnWidth })
            .AddColumn(new GridColumn { NoWrap = true })
            .AddRow(nameColumn, statusColumn);
    }

    static Grid RenderWorktreeLine(
        Worktree repository, string worktreeName, GitStatus? gitStatus, Dictionary<string, PullRequestInfo> pullRequests)
    {
        string formattedBranch = TruncateBranch(gitStatus?.Branch, 4 + 3 + 1 + worktreeName.Length);
        var nameColumn = new Markup($"    [blue]{repository.ComputeWorktreeId(worktreeName)}[/] {Markup.Escape(worktreeName)}{formattedBranch}");

        string gitIndicators = FormatGitIndicators(gitStatus);
        int statusColumnWidth = AnsiConsole.Profile.Width - NameColumnWidth;
        string pullRequestText = FormatPullRequestInfo(gitStatus?.Branch, pullRequests, statusColumnWidth - Markup.Remove(gitIndicators).Length);
        var statusColumn = new Markup($"{gitIndicators}{pullRequestText}");

        return new Grid()
            .AddColumn(new GridColumn { Width = NameColumnWidth })
            .AddColumn(new GridColumn { NoWrap = true })
            .AddRow(nameColumn, statusColumn);
    }

    static string TruncateBranch(string? branchName, int prefixLength)
    {
        if (branchName is null)
            return "";

        // -1 for the space before branch, -1 for Grid column padding
        int available = NameColumnWidth - prefixLength - 1 - 1;
        if (branchName.Length > available && available > 1)
            return $" [dim]{Markup.Escape(string.Concat(branchName.AsSpan(0, available - 1), "…"))}[/]";

        return $" [dim]{Markup.Escape(branchName)}[/]";
    }

    static string FormatPullRequestInfo(
        string? branchName, Dictionary<string, PullRequestInfo> pullRequests, int availableWidth)
    {
        if (branchName is null || !pullRequests.TryGetValue(branchName, out PullRequestInfo? pullRequest))
            return "";

        string numberMarkup = pullRequest.State switch
        {
            "OPEN" => $"[green]#{pullRequest.Number}[/]",
            "MERGED" => $"[purple]#{pullRequest.Number}[/]",
            "CLOSED" => $"[red]#{pullRequest.Number}[/]",
            _ => $"[dim]#{pullRequest.Number}[/]",
        };
        int numberLength = Markup.Remove(numberMarkup).Length;

        // " #42 " overhead
        int overhead = 1 + numberLength + 1;
        int availableForTitle = availableWidth - overhead;

        string displayTitle = pullRequest.Title;
        if (availableForTitle <= 0)
            displayTitle = "";
        else if (displayTitle.Length > availableForTitle)
            displayTitle = string.Concat(displayTitle.AsSpan(0, availableForTitle - 1), "…");

        return $" {numberMarkup} {Markup.Escape(displayTitle)}";
    }

    static string FormatGitIndicators(GitStatus? gitStatus)
    {
        if (gitStatus is null)
            return "";

        var builder = new StringBuilder();

        // Ahead/behind
        if (!gitStatus.HasUpstream)
        {
            builder.Append("[dim]~[/]");
        }
        else if (gitStatus.Ahead == 0 && gitStatus.Behind == 0)
        {
            builder.Append("[green]✓[/]");
        }
        else
        {
            if (gitStatus.Ahead > 0) builder.Append($"[yellow]↑{gitStatus.Ahead}[/]");
            if (gitStatus.Behind > 0) builder.Append($"[yellow]↓{gitStatus.Behind}[/]");
        }

        // Changes: +N/-M or -/-
        if (gitStatus.Additions == 0 && gitStatus.Deletions == 0)
        {
            builder.Append(" [dim]-[/]/[dim]-[/]");
        }
        else
        {
            builder.Append($" [green]+{gitStatus.Additions}[/]/[red]-{gitStatus.Deletions}[/]");
        }

        // Untracked files
        if (gitStatus.UntrackedFiles > 0)
        {
            builder.Append($"/{gitStatus.UntrackedFiles}[green]u[/]");
        }

        return builder.ToString();
    }
}

record GitStatus(string Branch, int Ahead, int Behind, bool HasUpstream, int Additions, int Deletions, int UntrackedFiles);

record PullRequestInfo(int Number, string Title, string State);

record Worktree(string RepositoryDirectory)
{
    public string Id => ComputeShortId($"repo:{RepositoryDirectory}");
    public string FullPath => Path.Combine(Config.SourceRootPath, RepositoryDirectory);
    public string WorktreesDirectoryPath => Path.Combine(Config.SourceRootPath, $"{RepositoryDirectory}.worktrees");

    public string ComputeWorktreeId(string worktreeName) => ComputeShortId($"wt:{RepositoryDirectory}/{worktreeName}");

    // FNV-1a 32-bit hash → 3 hex chars (12 bits)
    static string ComputeShortId(string identifier)
    {
        uint hash = 2166136261;
        foreach (char character in identifier)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return (hash & 0xFFF).ToString("x3");
    }
}

record struct DirectoryEntry(string RepositoryDirectory, string? WorktreeName, string DirectoryPath);

record struct WorktreeStatus(string Name, GitStatus? Status);

record struct ResolvedRef(string RepositoryDirectory, string? WorktreeName)
{
    public readonly bool IsRepository => WorktreeName is null;
    public readonly bool IsWorktree => WorktreeName is not null;

    public readonly string FullPath
    {
        get
        {
            var worktree = new Worktree(RepositoryDirectory);
            return IsWorktree
                ? Path.Combine(worktree.WorktreesDirectoryPath, WorktreeName!)
                : worktree.FullPath;
        }
    }
}

[JsonSerializable(typeof(GitHubPullRequestDto[]))]
partial class GitHubJsonContext : JsonSerializerContext;

record struct GitHubPullRequestDto(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("headRefName")] string HeadRefName,
    [property: JsonPropertyName("state")] string State);

internal class CliWrapper(string executableName)
{
    readonly PipeTarget _standardOutputPipe =
        PipeTarget.ToDelegate(line => AnsiConsole.Write(new Markup($"[dim][[stdout]] {Markup.Escape(line)}[/]\n")));
    readonly PipeTarget _standardErrorPipe =
        PipeTarget.ToDelegate(line => AnsiConsole.Write(new Markup($"[dim][[stderr]] {Markup.Escape(line)}[/]\n")));

    public async Task<BufferedCommandResult> RunAsync(
        string[] arguments,
        string? workingDirectory = null,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        Command cliCommand = Cli.Wrap(executableName)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None);

        if (workingDirectory is not null)
            cliCommand = cliCommand.WithWorkingDirectory(workingDirectory);

        if (!silent)
        {
            cliCommand = cliCommand.WithStandardOutputPipe(_standardOutputPipe).WithStandardErrorPipe(_standardErrorPipe);
            string commandString = Markup.Escape($"{executableName} {string.Join(' ', arguments)}");
            AnsiConsole.Write(new Markup($"[blue][[exec]] {commandString}[/]\n"));
        }

        return await cliCommand.ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8, cancellationToken);
    }
}
