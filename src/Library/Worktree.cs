// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager;

/// <summary>
/// Represents a git worktree.
/// </summary>
/// <param name="Path">The filesystem path to the worktree.</param>
/// <param name="Head">The commit SHA that the worktree is at.</param>
/// <param name="Branch">The branch name (without refs/heads/ prefix), or null if detached HEAD.</param>
/// <param name="IsLocked">Whether the worktree is locked.</param>
/// <param name="IsPrunable">Whether the worktree is prunable.</param>
public sealed record Worktree(
    string Path,
    string Head,
    string? Branch,
    bool IsLocked,
    bool IsPrunable
);
