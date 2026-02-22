// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Options for cloning a git repository.
/// </summary>
public sealed class CloneOptions
{
    /// <summary>
    /// Gets or sets whether to create a bare clone (no working directory).
    /// </summary>
    public bool Bare { get; init; }
}
