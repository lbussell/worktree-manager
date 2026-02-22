// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Implementation of <see cref="IWorktreeService"/> for high-level worktree management.
/// </summary>
public sealed class WorktreeService(IGitService gitService, WorktreeOptions options)
    : IWorktreeService
{
    /// <inheritdoc />
    public async Task<Worktree> CreateWorktreeAsync(
        string repoPath,
        string branchName,
        bool createBranch = false,
        string? startPoint = null,
        CancellationToken cancellationToken = default
    )
    {
        string repoName = await gitService.GetRepoNameAsync(repoPath, cancellationToken);
        string worktreePath = GetWorktreePath(repoName, branchName);

        await gitService.AddWorktreeAsync(
            repoPath,
            worktreePath,
            branchName,
            createBranch,
            startPoint,
            cancellationToken
        );

        IReadOnlyList<Worktree> worktrees = await gitService.ListWorktreesAsync(
            repoPath,
            cancellationToken
        );
        return worktrees.First(w => w.Path == worktreePath);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Worktree>> ListWorktreesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    )
    {
        return gitService.ListWorktreesAsync(repoPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteWorktreeAsync(
        string repoPath,
        string branchName,
        bool force = false,
        CancellationToken cancellationToken = default
    )
    {
        string repoName = await gitService.GetRepoNameAsync(repoPath, cancellationToken);
        string worktreePath = GetWorktreePath(repoName, branchName);

        await gitService.RemoveWorktreeAsync(repoPath, worktreePath, force, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ArchiveWorktreeAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default
    )
    {
        // Archive = remove worktree directory, but leave the branch intact.
        string repoName = await gitService.GetRepoNameAsync(repoPath, cancellationToken);
        string worktreePath = GetWorktreePath(repoName, branchName);

        await gitService.RemoveWorktreeAsync(
            repoPath,
            worktreePath,
            force: false,
            cancellationToken
        );
    }

    private string GetWorktreePath(string repoName, string branchName)
    {
        // Replace slashes in branch names with dashes for directory names
        string safeBranchName = branchName.Replace('/', '-');
        return Path.Combine(options.BasePath, "repos", repoName, "worktrees", safeBranchName);
    }
}
