// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class GetRepoNameAsyncTests
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
    public async Task HttpsUrlWithGitSuffix_ExtractsRepoName()
    {
        _gitCli.Result = new GitResult("https://github.com/user/my-project.git\n", "", 0);

        string result = await _gitService.GetRepoNameAsync("/repo", TestContext.CancellationToken);

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task HttpsUrlWithoutGitSuffix_ExtractsRepoName()
    {
        _gitCli.Result = new GitResult("https://github.com/user/my-project\n", "", 0);

        string result = await _gitService.GetRepoNameAsync("/repo", TestContext.CancellationToken);

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task SshUrlWithGitSuffix_ExtractsRepoName()
    {
        _gitCli.Result = new GitResult("git@github.com:user/my-project.git\n", "", 0);

        string result = await _gitService.GetRepoNameAsync("/repo", TestContext.CancellationToken);

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task SshUrlWithoutGitSuffix_ExtractsRepoName()
    {
        _gitCli.Result = new GitResult("git@github.com:user/my-project\n", "", 0);

        string result = await _gitService.GetRepoNameAsync("/repo", TestContext.CancellationToken);

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task NestedGitLabPath_ExtractsLastSegment()
    {
        _gitCli.Result = new GitResult(
            "git@gitlab.com:group/subgroup/deep/my-project.git\n",
            "",
            0
        );

        string result = await _gitService.GetRepoNameAsync("/repo", TestContext.CancellationToken);

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task NoRemote_FallsBackToDirectoryName()
    {
        _gitCli.Result = new GitResult("", "fatal: No such remote 'origin'\n", 2);

        string result = await _gitService.GetRepoNameAsync(
            "/home/user/repos/my-project",
            TestContext.CancellationToken
        );

        Assert.AreEqual("my-project", result);
    }

    [TestMethod]
    public async Task NoRemote_TrailingSlash_FallsBackToDirectoryName()
    {
        _gitCli.Result = new GitResult("", "fatal: No such remote 'origin'\n", 2);

        string result = await _gitService.GetRepoNameAsync(
            "/home/user/repos/my-project/",
            TestContext.CancellationToken
        );

        Assert.AreEqual("my-project", result);
    }
}
