// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

public static class GitParsing
{
    public static Result<Branch[]> ParseBranches(string output)
    {
        var branches = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('|', 4);
                return new Branch(
                    Name: parts[1],
                    IsCurrent: parts[0] == "*",
                    LastCommit: parts[3],
                    LastCommitDate: parts[2]
                );
            })
            .ToArray();

        return branches.Length == 0
            ? Result<Branch[]>.Failure("No branches found")
            : Result<Branch[]>.Success(branches);
    }

    public static Result<Worktree[]> ParseWorktrees(string output)
    {
        var worktrees = output
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var path =
                    lines.FirstOrDefault(l => l.StartsWith("worktree "))?["worktree ".Length..]
                    ?? "";
                var branch =
                    lines.FirstOrDefault(l => l.StartsWith("branch "))?["branch ".Length..] ?? "";
                if (branch.StartsWith("refs/heads/"))
                    branch = branch["refs/heads/".Length..];
                return new Worktree(path, branch);
            })
            .ToArray();

        return worktrees.Length == 0
            ? Result<Worktree[]>.Failure("No worktrees found")
            : Result<Worktree[]>.Success(worktrees);
    }
}
