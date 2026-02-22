// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Basic process wrapper for executing git CLI commands.
/// </summary>
internal interface IGitCli
{
    /// <summary>
    /// Runs a git command with the specified arguments.
    /// </summary>
    /// <param name="workingDirectory">The working directory to run git in.</param>
    /// <param name="args">The arguments to pass to git.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The result of the git command.</returns>
    Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default
    );
}
