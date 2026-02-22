// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Configuration options for <see cref="GitService"/>.
/// </summary>
public sealed class WorktreeManagerOptions
{
    /// <summary>
    /// Gets or sets the base path under which worktree directories are created.
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
