// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Implementation of <see cref="IGitService"/> for worktree-related git operations.
/// </summary>
internal sealed class GitService(IGitCli gitCli) : IGitService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Worktree>> ListWorktreesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            repoPath,
            ["worktree", "list", "--porcelain"],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to list worktrees: {result.StandardError}"
            );
        }

        return ParseWorktreeListOutput(result.StandardOutput);
    }

    /// <inheritdoc />
    public async Task AddWorktreeAsync(
        string repoPath,
        string worktreePath,
        string branchName,
        bool createBranch = false,
        string? startPoint = null,
        CancellationToken cancellationToken = default
    )
    {
        string[] args = createBranch
            ? startPoint is not null
                ? ["worktree", "add", "-b", branchName, worktreePath, startPoint]
                : ["worktree", "add", "-b", branchName, worktreePath]
            : ["worktree", "add", worktreePath, branchName];

        GitResult result = await gitCli.RunAsync(repoPath, args, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to add worktree: {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task<string> GetCurrentBranchAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            repoPath,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to get current branch: {result.StandardError}"
            );
        }

        return result.StandardOutput.Trim();
    }

    /// <inheritdoc />
    public async Task RemoveWorktreeAsync(
        string repoPath,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default
    )
    {
        string[] args = force
            ? ["worktree", "remove", "--force", worktreePath]
            : ["worktree", "remove", worktreePath];

        GitResult result = await gitCli.RunAsync(repoPath, args, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to remove worktree: {result.StandardError}"
            );
        }
    }

    /// <inheritdoc />
    public async Task<string> GetRepoNameAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            repoPath,
            ["remote", "get-url", "origin"],
            cancellationToken
        );

        if (result.IsSuccess)
        {
            string url = result.StandardOutput.Trim();
            return ParseRepoNameFromUrl(url);
        }

        // Fall back to directory name if no remote origin
        return Path.GetFileName(
            repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        );
    }

    private static IReadOnlyList<Worktree> ParseWorktreeListOutput(string output)
    {
        List<Worktree> worktrees = [];
        string[] lines = output.Split('\n', StringSplitOptions.None);

        string? currentPath = null;
        string? currentHead = null;
        string? currentBranch = null;
        bool isLocked = false;
        bool isPrunable = false;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // Empty line marks end of a worktree entry
                if (currentPath is not null && currentHead is not null)
                {
                    worktrees.Add(
                        new Worktree(currentPath, currentHead, currentBranch, isLocked, isPrunable)
                    );
                }

                // Reset for next entry
                currentPath = null;
                currentHead = null;
                currentBranch = null;
                isLocked = false;
                isPrunable = false;
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                currentPath = line[9..];
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                currentHead = line[5..];
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                string fullRef = line[7..];
                // Strip refs/heads/ prefix
                currentBranch = fullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? fullRef[11..]
                    : fullRef;
            }
            else if (line == "locked")
            {
                isLocked = true;
            }
            else if (line == "prunable")
            {
                isPrunable = true;
            }
        }

        // Handle last entry if no trailing newline
        if (currentPath is not null && currentHead is not null)
        {
            worktrees.Add(
                new Worktree(currentPath, currentHead, currentBranch, isLocked, isPrunable)
            );
        }

        return worktrees;
    }

    /// <inheritdoc />
    public async Task CloneBareAsync(
        string url,
        string targetPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            Directory.GetCurrentDirectory(),
            ["clone", "--bare", url, targetPath],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to clone repository: {result.StandardError}"
            );
        }
    }

    /// <inheritdoc />
    public async Task AddRemoteAsync(
        string repoPath,
        string name,
        string url,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            repoPath,
            ["remote", "add", name, url],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to add remote: {result.StandardError}");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Name, string Url)>> ListRemotesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(repoPath, ["remote", "-v"], cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to list remotes: {result.StandardError}");
        }

        return ParseRemoteListOutput(result.StandardOutput);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListBranchesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        GitResult result = await gitCli.RunAsync(
            repoPath,
            ["branch", "-a", "--format=%(refname:short)"],
            cancellationToken
        );

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to list branches: {result.StandardError}");
        }

        return result
            .StandardOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToList();
    }

    private static IReadOnlyList<(string Name, string Url)> ParseRemoteListOutput(string output)
    {
        // git remote -v outputs lines like: origin\thttps://... (fetch)\norigin\thttps://... (push)
        // Deduplicate by taking only fetch entries.
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.EndsWith("(fetch)", StringComparison.Ordinal))
            .Select(line =>
            {
                string[] parts = line.Split('\t', 2);
                string name = parts[0];
                string url =
                    parts.Length > 1
                        ? parts[1].Replace(" (fetch)", "", StringComparison.Ordinal).Trim()
                        : string.Empty;
                return (name, url);
            })
            .ToList();
    }

    private static string ParseRepoNameFromUrl(string url)
    {
        // Handle SSH URLs like git@github.com:user/repo.git
        if (url.Contains(':') && !url.Contains("://"))
        {
            int colonIndex = url.LastIndexOf(':');
            url = url[(colonIndex + 1)..];
        }

        // Get the last path segment (works for both HTTPS and SSH after colon extraction)
        if (url.Contains('/'))
        {
            url = url.Split('/')[^1];
        }

        // Remove .git suffix
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        return url;
    }
}
