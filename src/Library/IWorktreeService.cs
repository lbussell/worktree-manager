// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// High-level service for managing git worktrees.
/// </summary>
public interface IWorktreeService
{
    /// <summary>
    /// Creates a new worktree for the specified branch.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="branchName">The branch name to check out or create.</param>
    /// <param name="createBranch">If true, creates a new branch with the given name.</param>
    /// <param name="startPoint">The commit/branch to start the new branch from (only used when createBranch is true).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created worktree.</returns>
    Task<Worktree> CreateWorktreeAsync(
        string repoPath,
        string branchName,
        bool createBranch = false,
        string? startPoint = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists all worktrees for a repository.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of worktrees.</returns>
    Task<IReadOnlyList<Worktree>> ListWorktreesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a worktree.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="branchName">The branch name of the worktree to delete.</param>
    /// <param name="force">Whether to force deletion even if the worktree is dirty.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteWorktreeAsync(
        string repoPath,
        string branchName,
        bool force = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Archives a worktree by removing the worktree directory but leaving the branch intact.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="branchName">The branch name of the worktree to archive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ArchiveWorktreeAsync(
        string repoPath,
        string branchName,
        CancellationToken cancellationToken = default
    );
}
