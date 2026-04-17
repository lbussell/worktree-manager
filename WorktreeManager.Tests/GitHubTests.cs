namespace WorktreeManager.Tests;

[TestClass]
public sealed class GitHubTests
{
    // Captured from: gh pr list --state all --limit 5 --json number,title,state,headRefName,url
    // (with synthetic data since the test repo has no PRs)

    [TestMethod]
    public void ParsePullRequests_MultiplePRs()
    {
        var json = """
            [
                {
                    "headRefName": "feature-branch",
                    "number": 42,
                    "state": "OPEN",
                    "title": "Add new feature",
                    "url": "https://github.com/owner/repo/pull/42"
                },
                {
                    "headRefName": "fix-bug",
                    "number": 38,
                    "state": "MERGED",
                    "title": "Fix critical bug",
                    "url": "https://github.com/owner/repo/pull/38"
                },
                {
                    "headRefName": "old-feature",
                    "number": 12,
                    "state": "CLOSED",
                    "title": "Old feature attempt",
                    "url": "https://github.com/owner/repo/pull/12"
                }
            ]
            """;

        var prs = GitHub.ParsePullRequests(json);

        Assert.HasCount(3, prs);

        Assert.AreEqual(42, prs[0].Number);
        Assert.AreEqual("Add new feature", prs[0].Title);
        Assert.AreEqual(PullRequestState.Open, prs[0].State);
        Assert.AreEqual("feature-branch", prs[0].HeadBranch);
        Assert.AreEqual("https://github.com/owner/repo/pull/42", prs[0].Url);

        Assert.AreEqual(38, prs[1].Number);
        Assert.AreEqual(PullRequestState.Merged, prs[1].State);

        Assert.AreEqual(12, prs[2].Number);
        Assert.AreEqual(PullRequestState.Closed, prs[2].State);
    }

    [TestMethod]
    public void ParsePullRequests_EmptyArray()
    {
        var prs = GitHub.ParsePullRequests("[]");
        Assert.IsEmpty(prs);
    }

    [TestMethod]
    public void ParsePullRequests_EmptyString()
    {
        var prs = GitHub.ParsePullRequests("");
        Assert.IsEmpty(prs);
    }

    [TestMethod]
    public void ParsePullRequests_WhitespaceOnly()
    {
        var prs = GitHub.ParsePullRequests("   \n  ");
        Assert.IsEmpty(prs);
    }

    [TestMethod]
    public void ParsePullRequests_InvalidJson()
    {
        var prs = GitHub.ParsePullRequests("not valid json");
        Assert.IsEmpty(prs);
    }

    [TestMethod]
    public void ParsePullRequests_UnknownState_DefaultsToClosed()
    {
        var json = """
            [{"number": 1, "title": "Test", "state": "DRAFT", "headRefName": "test", "url": "https://example.com"}]
            """;
        var prs = GitHub.ParsePullRequests(json);
        Assert.AreEqual(PullRequestState.Closed, prs[0].State);
    }

    [TestMethod]
    public void ParsePullRequests_SingleOpenPR()
    {
        var json = """
            [
                {
                    "headRefName": "dev/user/my-feature",
                    "number": 99,
                    "state": "OPEN",
                    "title": "My feature with special chars: <>&\"",
                    "url": "https://github.com/owner/repo/pull/99"
                }
            ]
            """;

        var prs = GitHub.ParsePullRequests(json);

        Assert.HasCount(1, prs);
        Assert.AreEqual(99, prs[0].Number);
        Assert.AreEqual("My feature with special chars: <>&\"", prs[0].Title);
        Assert.AreEqual("dev/user/my-feature", prs[0].HeadBranch);
    }
}
