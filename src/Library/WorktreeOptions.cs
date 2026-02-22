// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Configuration options for <see cref="WorktreeService"/>.
/// </summary>
public sealed class WorktreeOptions
{
    /// <summary>
    /// Gets or sets the base path under which worktree directories are created.
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
