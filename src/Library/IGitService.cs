// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Service for git operations related to worktree management.
/// </summary>
public interface IGitService
{
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
    /// Adds a new worktree.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="worktreePath">The path where the worktree will be created.</param>
    /// <param name="branchName">The branch name to check out or create.</param>
    /// <param name="createBranch">If true, creates a new branch with the given name.</param>
    /// <param name="startPoint">The commit/branch to start the new branch from (only used when createBranch is true).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task AddWorktreeAsync(
        string repoPath,
        string worktreePath,
        string branchName,
        bool createBranch = false,
        string? startPoint = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current branch name.</returns>
    Task<string> GetCurrentBranchAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes a worktree.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="worktreePath">The path to the worktree to remove.</param>
    /// <param name="force">Whether to force removal even if the worktree is dirty.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RemoveWorktreeAsync(
        string repoPath,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the repository name from the remote origin URL or directory name.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The repository name.</returns>
    Task<string> GetRepoNameAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones a repository as a bare clone.
    /// </summary>
    /// <param name="url">The remote URL to clone from.</param>
    /// <param name="targetPath">The path where the bare clone will be created.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task CloneBareAsync(
        string url,
        string targetPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Adds a remote to a repository.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="name">The remote name.</param>
    /// <param name="url">The remote URL.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task AddRemoteAsync(
        string repoPath,
        string name,
        string url,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists all remotes for a repository.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of remote name/URL pairs.</returns>
    Task<IReadOnlyList<(string Name, string Url)>> ListRemotesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists all branches for a repository.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of branch names.</returns>
    Task<IReadOnlyList<string>> ListBranchesAsync(
        string repoPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a new worktree for the specified branch, using the configured base path to determine the worktree location.
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
    /// Removes a worktree by branch name, using the configured base path to determine the worktree location.
    /// The branch itself is left intact.
    /// </summary>
    /// <param name="repoPath">The path to the repository.</param>
    /// <param name="branchName">The branch name of the worktree to remove.</param>
    /// <param name="force">Whether to force removal even if the worktree is dirty.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RemoveWorktreeByBranchAsync(
        string repoPath,
        string branchName,
        bool force = false,
        CancellationToken cancellationToken = default
    );
}
