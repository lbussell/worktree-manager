// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class ErrorHandlingTests
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
    public async Task ListWorktrees_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: not a git repository\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.ListWorktreesAsync("/bad", TestContext.CancellationToken)
        );

        StringAssert.Contains(ex.Message, "not a git repository");
    }

    [TestMethod]
    public async Task AddWorktree_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: 'feature' is already checked out\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.AddWorktreeAsync(
                    "/repo",
                    "/wt",
                    "feature",
                    cancellationToken: TestContext.CancellationToken
                )
        );

        StringAssert.Contains(ex.Message, "already checked out");
    }

    [TestMethod]
    public async Task RemoveWorktree_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: '/wt' is not a working tree\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.RemoveWorktreeAsync(
                    "/repo",
                    "/wt",
                    cancellationToken: TestContext.CancellationToken
                )
        );

        StringAssert.Contains(ex.Message, "not a working tree");
    }

    [TestMethod]
    public async Task Clone_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: repository 'https://bad.url' not found\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.CloneAsync(
                    "https://bad.url",
                    "/target",
                    cancellationToken: TestContext.CancellationToken
                )
        );

        StringAssert.Contains(ex.Message, "not found");
    }

    [TestMethod]
    public async Task AddRemote_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: remote origin already exists.\n", 3);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.AddRemoteAsync(
                    "/repo",
                    "origin",
                    "https://url",
                    TestContext.CancellationToken
                )
        );

        StringAssert.Contains(ex.Message, "already exists");
    }

    [TestMethod]
    public async Task ListRemotes_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: not a git repository\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.ListRemotesAsync("/bad", TestContext.CancellationToken)
        );

        StringAssert.Contains(ex.Message, "not a git repository");
    }

    [TestMethod]
    public async Task ListBranches_NonZeroExit_ThrowsWithStderr()
    {
        _gitCli.Result = new GitResult("", "fatal: not a git repository\n", 128);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () =>
                _gitService.ListBranchesAsync("/bad", TestContext.CancellationToken)
        );

        StringAssert.Contains(ex.Message, "not a git repository");
    }
}
