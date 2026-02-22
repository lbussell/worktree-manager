// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class ListBranchesAsyncTests
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
    public async Task LocalAndRemoteBranches_ReturnsAll()
    {
        _gitCli.Result = new GitResult(
            "main\n" + "feature/login\n" + "origin/main\n" + "origin/feature/login\n",
            "",
            0
        );

        IReadOnlyList<string> result = await _gitService.ListBranchesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(4, result);
        Assert.AreEqual("main", result[0]);
        Assert.AreEqual("feature/login", result[1]);
        Assert.AreEqual("origin/main", result[2]);
        Assert.AreEqual("origin/feature/login", result[3]);
    }

    [TestMethod]
    public async Task SingleBranch_ReturnsSingleElement()
    {
        _gitCli.Result = new GitResult("main\n", "", 0);

        IReadOnlyList<string> result = await _gitService.ListBranchesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("main", result[0]);
    }

    [TestMethod]
    public async Task EmptyRepo_ReturnsEmptyList()
    {
        _gitCli.Result = new GitResult("", "", 0);

        IReadOnlyList<string> result = await _gitService.ListBranchesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task Error_ThrowsInvalidOperationException()
    {
        _gitCli.Result = new GitResult("", "fatal: not a git repository\n", 128);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _gitService.ListBranchesAsync("/not-a-repo", TestContext.CancellationToken)
        );
    }
}
