// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

/// <summary>
/// A configurable fake implementation of <see cref="IGitCli"/> for testing.
/// Set <see cref="Result"/> to control the <see cref="GitResult"/> returned by <see cref="RunAsync"/>.
/// </summary>
internal sealed class FakeGitCli : IGitCli
{
    /// <summary>
    /// The result to return from <see cref="RunAsync"/>.
    /// </summary>
    public GitResult Result { get; set; } = new("", "", 0);

    /// <summary>
    /// The last working directory passed to <see cref="RunAsync"/>.
    /// </summary>
    public string? LastWorkingDirectory { get; private set; }

    /// <summary>
    /// The last args passed to <see cref="RunAsync"/>.
    /// </summary>
    public IEnumerable<string>? LastArgs { get; private set; }

    public Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default
    )
    {
        LastWorkingDirectory = workingDirectory;
        LastArgs = args;
        return Task.FromResult(Result);
    }
}
