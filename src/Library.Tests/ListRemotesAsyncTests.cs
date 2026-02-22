// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

namespace WorktreeManager.Tests;

[TestClass]
public sealed class ListRemotesAsyncTests
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
    public async Task SingleRemote_DedupsFetchAndPush()
    {
        _gitCli.Result = new GitResult(
            "origin\thttps://github.com/user/repo.git (fetch)\n"
                + "origin\thttps://github.com/user/repo.git (push)\n",
            "",
            0
        );

        IReadOnlyList<(string Name, string Url)> result = await _gitService.ListRemotesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("origin", result[0].Name);
        Assert.AreEqual("https://github.com/user/repo.git", result[0].Url);
    }

    [TestMethod]
    public async Task MultipleRemotes_ReturnsBothDeduped()
    {
        _gitCli.Result = new GitResult(
            "origin\thttps://github.com/user/repo.git (fetch)\n"
                + "origin\thttps://github.com/user/repo.git (push)\n"
                + "upstream\thttps://github.com/upstream/repo.git (fetch)\n"
                + "upstream\thttps://github.com/upstream/repo.git (push)\n",
            "",
            0
        );

        IReadOnlyList<(string Name, string Url)> result = await _gitService.ListRemotesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(2, result);
        Assert.AreEqual("origin", result[0].Name);
        Assert.AreEqual("upstream", result[1].Name);
        Assert.AreEqual("https://github.com/upstream/repo.git", result[1].Url);
    }

    [TestMethod]
    public async Task SshRemoteUrl_PreservedIntact()
    {
        _gitCli.Result = new GitResult(
            "origin\tgit@github.com:user/repo.git (fetch)\n"
                + "origin\tgit@github.com:user/repo.git (push)\n",
            "",
            0
        );

        IReadOnlyList<(string Name, string Url)> result = await _gitService.ListRemotesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("git@github.com:user/repo.git", result[0].Url);
    }

    [TestMethod]
    public async Task EmptyOutput_ReturnsEmptyList()
    {
        _gitCli.Result = new GitResult("", "", 0);

        IReadOnlyList<(string Name, string Url)> result = await _gitService.ListRemotesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task RemoteWithNoTab_HandlesGracefully()
    {
        // Malformed line with no tab separator — name only, no URL
        _gitCli.Result = new GitResult("origin (fetch)\n", "", 0);

        IReadOnlyList<(string Name, string Url)> result = await _gitService.ListRemotesAsync(
            "/repo",
            TestContext.CancellationToken
        );

        Assert.HasCount(1, result);
        Assert.AreEqual("origin (fetch)", result[0].Name);
        Assert.AreEqual(string.Empty, result[0].Url);
    }
}
