// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class GetCurrentBranchAsyncTests
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
    public async Task NormalBranch_ReturnsTrimmedName()
    {
        _gitCli.Result = new GitResult("main\n", "", 0);

        string result = await _gitService.GetCurrentBranchAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.AreEqual("main", result);
    }

    [TestMethod]
    public async Task FeatureBranchWithSlash_PreservesSlash()
    {
        _gitCli.Result = new GitResult("feature/my-feature\n", "", 0);

        string result = await _gitService.GetCurrentBranchAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.AreEqual("feature/my-feature", result);
    }

    [TestMethod]
    public async Task DetachedHead_ReturnsHEAD()
    {
        _gitCli.Result = new GitResult("HEAD\n", "", 0);

        string result = await _gitService.GetCurrentBranchAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.AreEqual("HEAD", result);
    }

    [TestMethod]
    public async Task TrailingWhitespace_IsTrimmed()
    {
        _gitCli.Result = new GitResult("main  \n", "", 0);

        string result = await _gitService.GetCurrentBranchAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.AreEqual("main", result);
    }

    [TestMethod]
    public async Task Error_ThrowsInvalidOperationException()
    {
        _gitCli.Result = new GitResult("", "fatal: not a git repository\n", 128);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _gitService.GetCurrentBranchAsync("/not-a-repo", TestContext.CancellationToken)
        );
    }
}
