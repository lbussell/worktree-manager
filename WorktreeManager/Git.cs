// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using CliWrap;
using CliWrap.Buffered;

public static class Git
{
    public static Result<Branch[]> ParseBranches(string output)
    {
        var branches = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('\t', 6);
                var upstream = parts.Length > 3 ? parts[3] : "";
                var track = parts.Length > 4 ? parts[4] : "";
                return new Branch(
                    Name: parts[1],
                    IsCurrent: parts[0] == "*",
                    LastCommit: parts.Length > 5 ? parts[5] : "",
                    LastCommitDate: parts[2],
                    Upstream: string.IsNullOrEmpty(upstream) ? null : upstream,
                    UpstreamTrack: string.IsNullOrEmpty(track) ? null : track
                );
            })
            .ToArray();

        return branches.Length == 0
            ? Result<Branch[]>.Failure("No branches found")
            : Result<Branch[]>.Success(branches);
    }

    public static Result<Worktree[]> ParseWorktrees(string output)
    {
        var worktrees = output
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var path =
                    lines.FirstOrDefault(l => l.StartsWith("worktree "))?["worktree ".Length..]
                    ?? "";
                var branch =
                    lines.FirstOrDefault(l => l.StartsWith("branch "))?["branch ".Length..] ?? "";
                if (branch.StartsWith("refs/heads/"))
                    branch = branch["refs/heads/".Length..];
                return new Worktree(path, branch);
            })
            .ToArray();

        return worktrees.Length == 0
            ? Result<Worktree[]>.Failure("No worktrees found")
            : Result<Worktree[]>.Success(worktrees);
    }

    public static async Task<Result<Branch[]>> GetBranches(string workingDirectory)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(
                    [
                        "branch",
                        "--sort=-committerdate",
                        $"--format=%(HEAD)\t%(refname:short)\t%(committerdate:relative)\t%(upstream:short)\t%(upstream:track)\t%(subject)",
                    ]
                )
                .ExecuteBufferedAsync();

            return ParseBranches(result.StandardOutput);
        }
        catch (Exception ex)
        {
            return Result<Branch[]>.Failure(ex.Message);
        }
    }

    public static async Task<Result<Worktree[]>> GetWorktrees(string workingDirectory)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(["worktree", "list", "--porcelain"])
                .ExecuteBufferedAsync();

            return ParseWorktrees(result.StandardOutput);
        }
        catch (Exception ex)
        {
            return Result<Worktree[]>.Failure(ex.Message);
        }
    }

    public static async Task<bool> CheckDirtyState(string worktreePath)
    {
        try
        {
            var result = await Cli.Wrap("git")
                .WithWorkingDirectory(worktreePath)
                .WithArguments(["status", "--porcelain"])
                .ExecuteBufferedAsync();
            return !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<Worktree[]> EnrichWithDirtyState(Worktree[] worktrees)
    {
        var semaphore = new SemaphoreSlim(4);
        var tasks = worktrees.Select(async wt =>
        {
            await semaphore.WaitAsync();
            try
            {
                var isDirty = await CheckDirtyState(wt.Path);
                return wt with { IsDirty = isDirty };
            }
            finally
            {
                semaphore.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    public static async Task<Result<string>> RemoveBranch(string workingDirectory, Branch branch)
    {
        try
        {
            await Cli.Wrap("git")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(["branch", "-d", branch.Name])
                .ExecuteBufferedAsync();
            return Result<string>.Success(branch.Name);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"{branch.Name}: {ex.Message}");
        }
    }

    public static async Task<Result<string>> RemoveWorktree(string workingDirectory, Worktree wt)
    {
        try
        {
            await Cli.Wrap("git")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(["worktree", "remove", wt.Path])
                .ExecuteBufferedAsync();
            return Result<string>.Success(wt.Branch);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"{wt.Branch}: {ex.Message}");
        }
    }

    public static async Task<Result<string>> SwitchBranch(
        string workingDirectory,
        string branchName
    )
    {
        try
        {
            await Cli.Wrap("git")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(["switch", branchName])
                .ExecuteBufferedAsync();
            return Result<string>.Success($"Switched to {branchName}");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ex.Message);
        }
    }
}
