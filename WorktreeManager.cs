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
// Configuration: change SrcRoot in the Config class below.

#:package ConsoleAppFramework@*
#:package CliWrap@*
#:package Spectre.Console@*

using System.Text;
using System.Text.Json;
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
    const string SrcRoot = "~/src";

    public static readonly string SrcRootPath =
        SrcRoot.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static readonly string WorktreesFilePath =
        Path.Combine(SrcRootPath, "worktrees.txt");
}

/// <summary>Manage git worktrees across repositories.</summary>
public class WorktreeCommands
{
    static readonly CliWrapper Git = new("git");
    static readonly CliWrapper Gh = new("gh");

    /// <summary>Register a repository for worktree management.</summary>
    /// <param name="repodir">Repository directory relative to the source root.</param>
    [Command("add|a")]
    public async Task Add([Argument] string repodir)
    {
        string repoPath = Path.Combine(Config.SrcRootPath, repodir);

        if (!Directory.Exists(repoPath))
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Directory does not exist: [dim]{Markup.Escape(repoPath)}[/]\n"));
            Environment.ExitCode = 1;
            return;
        }

        BufferedCommandResult result = await Git.RunAsync(
            ["rev-parse", "--git-dir"],
            workingDirectory: repoPath,
            silent: true);

        if (result.ExitCode != 0)
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Not a git repository: [dim]{Markup.Escape(repoPath)}[/]\n"));
            Environment.ExitCode = 1;
            return;
        }

        string[] entries = ReadWorktreesFile();
        if (entries.Contains(repodir, StringComparer.Ordinal))
        {
            AnsiConsole.Write(new Markup($"Already registered: [purple]{RepoId(repodir)}[/] {Markup.Escape(repodir)}\n"));
            return;
        }

        File.AppendAllLines(Config.WorktreesFilePath, [repodir]);

        string worktreesDir = Path.Combine(Config.SrcRootPath, $"{repodir}.worktrees");
        Directory.CreateDirectory(worktreesDir);

        AnsiConsole.Write(new Markup($"Added [purple]{RepoId(repodir)}[/] {Markup.Escape(repodir)}\n"));
    }

    /// <summary>List all managed repositories and their worktrees.</summary>
    [Command("list|l")]
    public async Task List()
    {
        string[] entries = ReadWorktreesFile();

        if (entries.Length == 0)
        {
            AnsiConsole.Write(new Markup("[yellow]No repositories registered.[/] Use 'add' to register one.\n"));
            return;
        }

        // Collect all directories and kick off status queries concurrently
        var items = new List<(string Entry, string? Worktree, string Dir)>();
        foreach (string entry in entries)
        {
            items.Add((entry, null, Path.Combine(Config.SrcRootPath, entry)));

            string worktreesDir = Path.Combine(Config.SrcRootPath, $"{entry}.worktrees");
            if (!Directory.Exists(worktreesDir))
                continue;

            foreach (string dir in Directory.GetDirectories(worktreesDir))
                items.Add((entry, Path.GetFileName(dir), dir));
        }

        var statusTasks = items.ToDictionary(i => i, i => GetGitStatusAsync(i.Dir));
        var prTasks = entries.ToDictionary(
            e => e,
            e => GetOpenPrsAsync(Path.Combine(Config.SrcRootPath, e)));
        await Task.WhenAll(statusTasks.Values.Cast<Task>().Concat(prTasks.Values));

        // Compose view declaratively
        var renderables = new List<IRenderable>();
        foreach (string entry in entries)
        {
            var prs = prTasks[entry].Result;
            var repoKey = items.First(i => i.Entry == entry && i.Worktree is null);

            var worktrees = new List<(string Wt, GitStatus? Status)>();
            string worktreesDir = Path.Combine(Config.SrcRootPath, $"{entry}.worktrees");
            if (Directory.Exists(worktreesDir))
            {
                foreach (string dir in Directory.GetDirectories(worktreesDir))
                {
                    string wt = Path.GetFileName(dir);
                    var wtKey = items.First(i => i.Entry == entry && i.Worktree == wt);
                    worktrees.Add((wt, statusTasks[wtKey].Result));
                }
            }

            renderables.Add(RenderRepoEntry(entry, statusTasks[repoKey].Result, worktrees, prs));
        }

        AnsiConsole.Write(new Rows(renderables));
    }

    /// <summary>Create a new worktree for a managed repository.</summary>
    /// <param name="reporef">Repo name or repo ID.</param>
    /// <param name="worktreename">Name for the new worktree. Auto-generated when using --pr.</param>
    /// <param name="branch">-b, Create a new branch matching the worktree name.</param>
    /// <param name="from">-f, Base ref for the new branch (requires --branch).</param>
    /// <param name="pr">-p, PR number to check out in the new worktree.</param>
    [Command("create|new|n")]
    public async Task Create(
        [Argument] string reporef,
        [Argument] string? worktreename = null,
        bool branch = false,
        string? from = null,
        int? pr = null)
    {
        if (from is not null && !branch)
        {
            Console.Error.WriteLine("Error: --from/-f requires --branch/-b.");
            Environment.ExitCode = 1;
            return;
        }

        if (pr is not null && (branch || from is not null))
        {
            Console.Error.WriteLine("Error: --pr/-p cannot be combined with --branch/-b or --from/-f.");
            Environment.ExitCode = 1;
            return;
        }

        if (worktreename is null && pr is null)
        {
            Console.Error.WriteLine("Error: worktreename is required unless --pr/-p is specified.");
            Environment.ExitCode = 1;
            return;
        }

        worktreename ??= $"pr-{pr}";

        string[] entries = ReadWorktreesFile();
        string repodir = ResolveRepoRef(reporef, entries) ?? reporef;
        if (!entries.Contains(repodir, StringComparer.Ordinal))
        {
            AnsiConsole.Write(new Markup($"[red]Error:[/] Repository not managed: [dim]{Markup.Escape(reporef)}[/]. Use 'add' first.\n"));
            Environment.ExitCode = 1;
            return;
        }

        string repoPath = Path.Combine(Config.SrcRootPath, repodir);
        string worktreePath = Path.Combine(Config.SrcRootPath, $"{repodir}.worktrees", worktreename);

        List<string> gitArgs = ["worktree", "add"];

        if (branch)
        {
            gitArgs.AddRange(["-b", worktreename, worktreePath]);
            if (from is not null)
                gitArgs.Add(from);
        }
        else
        {
            gitArgs.AddRange(["--detach", worktreePath]);
        }

        BufferedCommandResult result = await Git.RunAsync(
            [.. gitArgs],
            workingDirectory: repoPath);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.Write(new Markup($"Created worktree: [blue]{WorktreeId(repodir, worktreename)}[/] {Markup.Escape(worktreePath)}\n"));

        if (pr is not null)
        {
            result = await Gh.RunAsync(
                ["pr", "checkout", pr.Value.ToString()],
                workingDirectory: worktreePath);

            if (result.ExitCode != 0)
            {
                if (AnsiConsole.Confirm("PR checkout failed (branches may have diverged). Force checkout?", defaultValue: false))
                {
                    result = await Gh.RunAsync(
                        ["pr", "checkout", pr.Value.ToString(), "--force"],
                        workingDirectory: worktreePath);

                    if (result.ExitCode != 0)
                    {
                        Environment.ExitCode = 1;
                        return;
                    }

                    AnsiConsole.Write(new Markup($"Created worktree: [blue]{WorktreeId(repodir, worktreename)}[/] {Markup.Escape(worktreePath)}\n"));
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
    /// <param name="id">Repo name, repo ID, or worktree ID.</param>
    [Command("dir|d")]
    public void Dir([Argument] string id)
    {
        string[] entries = ReadWorktreesFile();
        (string Repo, string? Worktree)? resolved = ResolveRef(id, entries);
        if (resolved is null)
        {
            Console.Error.WriteLine($"Error: Could not resolve ref: {id}.");
            Environment.ExitCode = 1;
            return;
        }

        (string? repo, string? worktree) = resolved.Value;
        string path = worktree is not null
            ? Path.Combine(Config.SrcRootPath, $"{repo}.worktrees", worktree)
            : Path.Combine(Config.SrcRootPath, repo);

        Console.WriteLine(path);
    }

    /// <summary>Remove a worktree, or untrack a repository.</summary>
    /// <param name="reporef">Repo name, repo ID, or worktree ID.</param>
    /// <param name="worktreename">Worktree to remove. If omitted, ref is resolved automatically.</param>
    [Command("remove|rm")]
    public async Task Remove([Argument] string reporef, [Argument] string? worktreename = null)
    {
        string[] entries = ReadWorktreesFile();

        string repodir;

        if (worktreename is not null)
        {
            // Two args: first is repo ref, second is worktree name
            repodir = ResolveRepoRef(reporef, entries) ?? reporef;
            if (!entries.Contains(repodir, StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"Error: Repository not managed: {reporef}.");
                Environment.ExitCode = 1;
                return;
            }
        }
        else
        {
            // Single arg: resolve as worktree ID, repo ID, or repo name
            var resolved = ResolveRef(reporef, entries);
            if (resolved is null)
            {
                Console.Error.WriteLine($"Error: Could not resolve ref: {reporef}.");
                Environment.ExitCode = 1;
                return;
            }
            (repodir, worktreename) = resolved.Value;
        }

        if (worktreename is null)
        {
            // Remove repo from tracking
            string[] updated = entries.Where(e => e != repodir).ToArray();
            File.WriteAllLines(Config.WorktreesFilePath, updated);
            AnsiConsole.Write(new Markup($"Removed from tracking: [purple]{RepoId(repodir)}[/] {Markup.Escape(repodir)}\n"));
            return;
        }

        // Remove individual worktree
        string repoPath = Path.Combine(Config.SrcRootPath, repodir);
        string worktreePath = Path.Combine(Config.SrcRootPath, $"{repodir}.worktrees", worktreename);

        if (!Directory.Exists(worktreePath))
        {
            Console.Error.WriteLine($"Error: Worktree does not exist: {worktreePath}");
            Environment.ExitCode = 1;
            return;
        }

        BufferedCommandResult result = await Git.RunAsync(
            ["worktree", "remove", worktreePath],
            workingDirectory: repoPath);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.Write(new Markup($"Removed worktree: [blue]{WorktreeId(repodir, worktreename)}[/] {Markup.Escape(worktreePath)}\n"));
    }

    static async Task<GitStatus?> GetGitStatusAsync(string workingDir)
    {
        // Get branch, ahead/behind, and untracked count
        BufferedCommandResult statusResult = await Git.RunAsync(
            ["status", "-b", "--porcelain"],
            workingDirectory: workingDir,
            silent: true);

        if (statusResult.ExitCode != 0)
            return null;

        string[] lines = statusResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return null;

        // Parse branch line: ## branch...upstream [ahead N, behind M]
        string branchLine = lines[0];
        string branch = "HEAD";
        int ahead = 0, behind = 0;
        bool hasUpstream = false;

        if (branchLine.StartsWith("## ", StringComparison.Ordinal))
        {
            string rest = branchLine[3..];
            int bracketIdx = rest.IndexOf('[');
            string branchPart = bracketIdx >= 0 ? rest[..bracketIdx].Trim() : rest.Trim();

            int dotIdx = branchPart.IndexOf("...", StringComparison.Ordinal);
            if (dotIdx >= 0)
            {
                branch = branchPart[..dotIdx];
                hasUpstream = true;
            }
            else
            {
                branch = branchPart;
            }

            if (bracketIdx >= 0)
            {
                int closeBracket = rest.IndexOf(']', bracketIdx);
                if (closeBracket >= 0)
                {
                    string tracking = rest[(bracketIdx + 1)..closeBracket];
                    foreach (string part in tracking.Split(',', StringSplitOptions.TrimEntries))
                    {
                        if (part.StartsWith("ahead ", StringComparison.Ordinal)
                            && int.TryParse(part[6..], out int a))
                            ahead = a;
                        else if (part.StartsWith("behind ", StringComparison.Ordinal)
                            && int.TryParse(part[7..], out int b))
                            behind = b;
                    }
                }
            }
        }

        // Count untracked files (lines starting with ??)
        int untrackedFiles = lines.Skip(1).Count(l => l.StartsWith("??", StringComparison.Ordinal));

        // Get total line additions/deletions (staged + unstaged vs HEAD)
        int additions = 0, deletions = 0;
        BufferedCommandResult diffResult = await Git.RunAsync(
            ["diff", "HEAD", "--numstat"],
            workingDirectory: workingDir,
            silent: true);

        if (diffResult.ExitCode == 0)
        {
            foreach (string diffLine in diffResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = diffLine.Split('\t');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out int add)
                    && int.TryParse(parts[1], out int del))
                {
                    additions += add;
                    deletions += del;
                }
            }
        }

        return new GitStatus(branch, ahead, behind, hasUpstream, additions, deletions, untrackedFiles);
    }

    static IRenderable RenderRepoEntry(
        string entry,
        GitStatus? repoStatus,
        List<(string Wt, GitStatus? Status)> worktrees,
        Dictionary<string, PullRequestInfo> prs)
    {
        var lines = new List<IRenderable>
        {
            RenderRepoLine(entry, repoStatus, prs)
        };
        foreach (var (wt, status) in worktrees)
            lines.Add(RenderWorktreeLine(entry, wt, status, prs));
        return new Rows(lines);
    }

    static Markup RenderRepoLine(
        string entry, GitStatus? status, Dictionary<string, PullRequestInfo> prs)
    {
        string statusMarkup = FormatGitStatus(status);
        string prefix = $"[purple]{RepoId(entry)}[/] {Markup.Escape(entry)}{statusMarkup}";
        return new Markup(AppendPrInfo(prefix, status?.Branch, prs));
    }

    static Markup RenderWorktreeLine(
        string entry, string wt, GitStatus? status, Dictionary<string, PullRequestInfo> prs)
    {
        string wtStatus = FormatGitStatus(status);
        string prefix = $"    [blue]{WorktreeId(entry, wt)}[/] {Markup.Escape(wt)}{wtStatus}";
        return new Markup(AppendPrInfo(prefix, status?.Branch, prs));
    }

    static string AppendPrInfo(
        string prefixMarkup, string? branch, Dictionary<string, PullRequestInfo> prs)
    {
        if (branch is null || !prs.TryGetValue(branch, out PullRequestInfo? pr))
            return prefixMarkup;

        int width = AnsiConsole.Profile.Width;
        int prefixLen = Markup.Remove(prefixMarkup).Length;
        string stateMarkup = FormatPrState(pr.State);
        int stateLen = Markup.Remove(stateMarkup).Length;

        // " [open] #42 " overhead
        int overhead = 1 + stateLen + 1 + 1 + pr.Number.ToString().Length + 1;
        int availableForTitle = width - prefixLen - overhead;

        string title = pr.Title;
        if (availableForTitle <= 0)
            title = "";
        else if (title.Length > availableForTitle)
            title = string.Concat(title.AsSpan(0, availableForTitle - 1), "…");

        return $"{prefixMarkup} {stateMarkup} [cyan]#{pr.Number}[/] {Markup.Escape(title)}";
    }

    static string FormatPrState(string state) => state switch
    {
        "OPEN" => "[green][[open]][/]",
        "MERGED" => "[purple][[merged]][/]",
        "CLOSED" => "[red][[closed]][/]",
        _ => $"[dim][[{Markup.Escape(state.ToLowerInvariant())}]][/]",
    };

    static async Task<Dictionary<string, PullRequestInfo>> GetOpenPrsAsync(string workingDir)
    {
        var result = new Dictionary<string, PullRequestInfo>(StringComparer.Ordinal);

        BufferedCommandResult ghResult = await Gh.RunAsync(
            ["pr", "list", "--state", "all", "--json", "number,title,headRefName,state"],
            workingDirectory: workingDir,
            silent: true);

        if (ghResult.ExitCode != 0)
            return result;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(ghResult.StandardOutput);
            foreach (JsonElement pr in doc.RootElement.EnumerateArray())
            {
                string branch = pr.GetProperty("headRefName").GetString() ?? "";
                int number = pr.GetProperty("number").GetInt32();
                string title = pr.GetProperty("title").GetString() ?? "";
                string state = pr.GetProperty("state").GetString() ?? "OPEN";
                result.TryAdd(branch, new PullRequestInfo(number, title, state));
            }
        }
        catch (JsonException)
        {
            // Graceful degradation: ignore malformed output
        }

        return result;
    }

    static string FormatGitStatus(GitStatus? status)
    {
        if (status is null)
            return "";

        var sb = new StringBuilder();

        // Branch name (dim)
        sb.Append($" [dim]{Markup.Escape(status.Branch)}[/]");

        // Ahead/behind
        if (!status.HasUpstream)
        {
            sb.Append(" [dim]~[/]");
        }
        else if (status.Ahead == 0 && status.Behind == 0)
        {
            sb.Append(" [green]✓[/]");
        }
        else
        {
            sb.Append(' ');
            if (status.Ahead > 0) sb.Append($"[yellow]↑{status.Ahead}[/]");
            if (status.Behind > 0) sb.Append($"[yellow]↓{status.Behind}[/]");
        }

        // Changes: +N/-M or -/-
        if (status.Additions == 0 && status.Deletions == 0)
        {
            sb.Append(" [dim]-[/]/[dim]-[/]");
        }
        else
        {
            sb.Append($" [green]+{status.Additions}[/]/[red]-{status.Deletions}[/]");
        }

        // Untracked files
        if (status.UntrackedFiles > 0)
        {
            sb.Append($"/{status.UntrackedFiles}[green]u[/]");
        }

        return sb.ToString();
    }

    static string[] ReadWorktreesFile()
    {
        if (!File.Exists(Config.WorktreesFilePath))
            return [];

        return File.ReadAllLines(Config.WorktreesFilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    // FNV-1a 32-bit hash → 3 hex chars (12 bits)
    static string ShortId(string input)
    {
        uint hash = 2166136261;
        foreach (char c in input)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return (hash & 0xFFF).ToString("x3");
    }

    static string RepoId(string repodir) => ShortId($"repo:{repodir}");
    static string WorktreeId(string repodir, string worktree) => ShortId($"wt:{repodir}/{worktree}");

    /// <summary>
    /// Resolves a ref (short ID or exact name) to a (repo, worktree?) pair.
    /// Checks worktree IDs first, then repo IDs, then exact repo names.
    /// </summary>
    static (string Repo, string? Worktree)? ResolveRef(string input, string[] entries)
    {
        // Check worktree IDs
        foreach (string repo in entries)
        {
            string wtDir = Path.Combine(Config.SrcRootPath, $"{repo}.worktrees");
            if (!Directory.Exists(wtDir)) continue;
            foreach (string dir in Directory.GetDirectories(wtDir))
            {
                string wt = Path.GetFileName(dir);
                if (WorktreeId(repo, wt) == input)
                    return (repo, wt);
            }
        }

        // Check repo IDs
        foreach (string repo in entries)
        {
            if (RepoId(repo) == input)
                return (repo, null);
        }

        // Check exact repo names
        if (entries.Contains(input, StringComparer.Ordinal))
            return (input, null);

        return null;
    }

    /// <summary>Resolves a ref to a repo name only (ID or exact name).</summary>
    static string? ResolveRepoRef(string input, string[] entries)
    {
        foreach (string repo in entries)
        {
            if (RepoId(repo) == input)
                return repo;
        }
        if (entries.Contains(input, StringComparer.Ordinal))
            return input;
        return null;
    }
}

record GitStatus(string Branch, int Ahead, int Behind, bool HasUpstream, int Additions, int Deletions, int UntrackedFiles);

record PullRequestInfo(int Number, string Title, string State);

internal class CliWrapper(string command)
{
    readonly PipeTarget _stdOutPipe =
        PipeTarget.ToDelegate(line => AnsiConsole.Write(new Markup($"[dim][[stdout]] {Markup.Escape(line)}[/]\n")));
    readonly PipeTarget _stdErrPipe =
        PipeTarget.ToDelegate(line => AnsiConsole.Write(new Markup($"[dim][[stderr]] {Markup.Escape(line)}[/]\n")));

    public async Task<BufferedCommandResult> RunAsync(
        string[] arguments,
        string? workingDirectory = null,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        Command cmd = Cli.Wrap(command)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None);

        if (workingDirectory is not null)
            cmd = cmd.WithWorkingDirectory(workingDirectory);

        if (!silent)
        {
            cmd = cmd.WithStandardOutputPipe(_stdOutPipe).WithStandardErrorPipe(_stdErrPipe);
            string commandString = Markup.Escape($"{command} {string.Join(' ', arguments)}");
            AnsiConsole.Write(new Markup($"[blue][[exec]] {commandString}[/]\n"));
        }

        return await cmd.ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8, cancellationToken);
    }
}
