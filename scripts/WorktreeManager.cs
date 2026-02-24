// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

// Manages git worktrees across repositories in a source directory.
//
// Usage:
//   dotnet scripts/WorktreeManager.cs add <repodir>
//   dotnet scripts/WorktreeManager.cs list
//   dotnet scripts/WorktreeManager.cs create <repodir> <worktreename> [--branch] [--from <ref>]
//
// Configuration: change SrcRoot in the Config class below.

#:package ConsoleAppFramework@*
#:package CliWrap@*

using System.Text;
using ConsoleAppFramework;
using CliWrap;
using CliWrap.Buffered;

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
    /// <summary>Register a repository for worktree management.</summary>
    /// <param name="repodir">Repository directory relative to the source root.</param>
    [Command("add|a")]
    public async Task Add([Argument] string repodir)
    {
        string repoPath = Path.Combine(Config.SrcRootPath, repodir);

        if (!Directory.Exists(repoPath))
        {
            Console.Error.WriteLine($"Error: Directory does not exist: {repoPath}");
            Environment.ExitCode = 1;
            return;
        }

        BufferedCommandResult result = await Cli.Wrap("git")
            .WithArguments("rev-parse --git-dir")
            .WithWorkingDirectory(repoPath)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8);

        if (result.ExitCode != 0)
        {
            Console.Error.WriteLine($"Error: Not a git repository: {repoPath}");
            Environment.ExitCode = 1;
            return;
        }

        string[] entries = ReadWorktreesFile();
        if (entries.Contains(repodir, StringComparer.Ordinal))
        {
            Console.WriteLine($"Already registered: {repodir}");
            return;
        }

        File.AppendAllLines(Config.WorktreesFilePath, [repodir]);

        string worktreesDir = Path.Combine(Config.SrcRootPath, $"{repodir}.worktrees");
        Directory.CreateDirectory(worktreesDir);

        Console.WriteLine($"Added: {repodir}");
    }

    /// <summary>List all managed repositories and their worktrees.</summary>
    [Command("list|l")]
    public void List()
    {
        string[] entries = ReadWorktreesFile();

        if (entries.Length == 0)
        {
            Console.WriteLine("No repositories registered. Use 'add' to register one.");
            return;
        }

        foreach (string entry in entries)
        {
            Console.WriteLine(entry);

            string worktreesDir = Path.Combine(Config.SrcRootPath, $"{entry}.worktrees");
            if (!Directory.Exists(worktreesDir))
                continue;

            foreach (string dir in Directory.GetDirectories(worktreesDir))
            {
                Console.WriteLine($"  {Path.GetFileName(dir)}");
            }
        }
    }

    /// <summary>Create a new worktree for a managed repository.</summary>
    /// <param name="repodir">Repository directory relative to the source root.</param>
    /// <param name="worktreename">Name for the new worktree.</param>
    /// <param name="branch">-b, Create a new branch matching the worktree name.</param>
    /// <param name="from">-f, Base ref for the new branch (requires --branch).</param>
    [Command("create|new|n")]
    public async Task Create(
        [Argument] string repodir,
        [Argument] string worktreename,
        bool branch = false,
        string? from = null)
    {
        if (from is not null && !branch)
        {
            Console.Error.WriteLine("Error: --from/-f requires --branch/-b.");
            Environment.ExitCode = 1;
            return;
        }

        string[] entries = ReadWorktreesFile();
        if (!entries.Contains(repodir, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Error: Repository not managed: {repodir}. Use 'add' first.");
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

        BufferedCommandResult result = await Cli.Wrap("git")
            .WithArguments(gitArgs)
            .WithWorkingDirectory(repoPath)
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
            .ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Created worktree: {worktreePath}");
    }

    /// <summary>Remove a worktree, or untrack a repository.</summary>
    /// <param name="repodir">Repository directory relative to the source root.</param>
    /// <param name="worktreename">Worktree to remove. If omitted, removes the repo from tracking.</param>
    [Command("remove|rm")]
    public async Task Remove([Argument] string repodir, [Argument] string? worktreename = null)
    {
        string[] entries = ReadWorktreesFile();
        if (!entries.Contains(repodir, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Error: Repository not managed: {repodir}.");
            Environment.ExitCode = 1;
            return;
        }

        if (worktreename is null)
        {
            // Remove repo from tracking
            string[] updated = entries.Where(e => e != repodir).ToArray();
            File.WriteAllLines(Config.WorktreesFilePath, updated);
            Console.WriteLine($"Removed from tracking: {repodir}");
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

        BufferedCommandResult result = await Cli.Wrap("git")
            .WithArguments(["worktree", "remove", worktreePath])
            .WithWorkingDirectory(repoPath)
            .WithValidation(CommandResultValidation.None)
            .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
            .ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8);

        if (result.ExitCode != 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Removed worktree: {worktreePath}");
    }

    static string[] ReadWorktreesFile()
    {
        if (!File.Exists(Config.WorktreesFilePath))
            return [];

        return File.ReadAllLines(Config.WorktreesFilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
}
