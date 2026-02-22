// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Result of executing a git command.
/// </summary>
/// <param name="StandardOutput">The standard output from the command.</param>
/// <param name="StandardError">The standard error from the command.</param>
/// <param name="ExitCode">The exit code of the command.</param>
public sealed record GitResult(string StandardOutput, string StandardError, int ExitCode)
{
    /// <summary>
    /// Gets a value indicating whether the command succeeded (exit code 0).
    /// </summary>
    public bool IsSuccess => ExitCode == 0;
}
