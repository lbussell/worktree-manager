// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class ListWorktreesAsyncTests
{
    public TestContext TestContext { get; set; } = null!;

    private FakeGitCli _gitCli = null!;
    private GitService _gitService = null!;

    [TestInitialize]
    public void Setup()
    {
        _gitCli = new FakeGitCli();
        _gitService = new GitService(_gitCli);
    }

    [TestMethod]
    public async Task SingleWorktree_ParsesCorrectly()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/my-project\n"
                + "HEAD abc1234def5678def5678def5678def5678def5\n"
                + "branch refs/heads/main\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("/home/user/repos/my-project", result[0].Path);
        Assert.AreEqual("abc1234def5678def5678def5678def5678def5", result[0].Head);
        Assert.AreEqual("main", result[0].Branch);
        Assert.IsFalse(result[0].IsLocked);
        Assert.IsFalse(result[0].IsPrunable);
    }

    [TestMethod]
    public async Task MultipleWorktrees_ParsesAll()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/bare\n"
                + "HEAD aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n"
                + "branch refs/heads/main\n"
                + "\n"
                + "worktree /home/user/repos/wt/feature\n"
                + "HEAD bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n"
                + "branch refs/heads/feature/login\n"
                + "\n"
                + "worktree /home/user/repos/wt/bugfix\n"
                + "HEAD cccccccccccccccccccccccccccccccccccccccc\n"
                + "branch refs/heads/bugfix/crash\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(3, result);
        Assert.AreEqual("main", result[0].Branch);
        Assert.AreEqual("feature/login", result[1].Branch);
        Assert.AreEqual("bugfix/crash", result[2].Branch);
    }

    [TestMethod]
    public async Task DetachedHead_BranchIsNull()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/detached\n"
                + "HEAD dddddddddddddddddddddddddddddddddddddddd\n"
                + "detached\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.IsNull(result[0].Branch);
    }

    [TestMethod]
    public async Task LockedWorktree_IsLockedTrue()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/locked-wt\n"
                + "HEAD eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n"
                + "branch refs/heads/feature\n"
                + "locked\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.IsTrue(result[0].IsLocked);
        Assert.IsFalse(result[0].IsPrunable);
    }

    [TestMethod]
    public async Task PrunableWorktree_IsPrunableTrue()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/prunable-wt\n"
                + "HEAD ffffffffffffffffffffffffffffffffffffffff\n"
                + "branch refs/heads/stale\n"
                + "prunable\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.IsFalse(result[0].IsLocked);
        Assert.IsTrue(result[0].IsPrunable);
    }

    [TestMethod]
    public async Task LockedAndPrunable_BothFlagsTrue()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/both\n"
                + "HEAD 1111111111111111111111111111111111111111\n"
                + "branch refs/heads/old\n"
                + "locked\n"
                + "prunable\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.IsTrue(result[0].IsLocked);
        Assert.IsTrue(result[0].IsPrunable);
    }

    [TestMethod]
    public async Task NoTrailingNewline_StillParsesLastEntry()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/project\n"
                + "HEAD 2222222222222222222222222222222222222222\n"
                + "branch refs/heads/main",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("main", result[0].Branch);
    }

    [TestMethod]
    public async Task EmptyOutput_ReturnsEmptyList()
    {
        _gitCli.Result = new GitResult("", "", 0);

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task PathWithSpaces_ParsedCorrectly()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/my projects/repo worktree\n"
                + "HEAD 3333333333333333333333333333333333333333\n"
                + "branch refs/heads/main\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("/home/user/my projects/repo worktree", result[0].Path);
    }

    [TestMethod]
    public async Task BranchWithSlashes_PreservedAfterStrippingRefsHeads()
    {
        _gitCli.Result = new GitResult(
            "worktree /home/user/repos/wt\n"
                + "HEAD 4444444444444444444444444444444444444444\n"
                + "branch refs/heads/feature/deeply/nested/branch\n"
                + "\n",
            "",
            0
        );

        IReadOnlyList<Worktree> result = await _gitService.ListWorktreesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("feature/deeply/nested/branch", result[0].Branch);
    }
}
