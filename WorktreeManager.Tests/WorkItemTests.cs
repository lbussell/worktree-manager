namespace WorktreeManager.Tests;

[TestClass]
public sealed class WorkItemTests
{
    [TestMethod]
    public void Compose_MatchesBranchesToWorktrees()
    {
        Branch[] branches =
        [
            new("main", true, "Init", "2 days ago"),
            new("feature", false, "Add feature", "1 day ago"),
        ];
        Worktree[] worktrees = [new("/repo", "main"), new("/repo-feature", "feature")];
        PullRequest[] prs = [];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(2, items);
        Assert.AreEqual("main", items[0].Branch.Name);
        Assert.IsNotNull(items[0].Worktree);
        Assert.AreEqual("/repo", items[0].Worktree!.Path);
        Assert.AreEqual("feature", items[1].Branch.Name);
        Assert.IsNotNull(items[1].Worktree);
        Assert.AreEqual("/repo-feature", items[1].Worktree!.Path);
    }

    [TestMethod]
    public void Compose_MatchesBranchesToPullRequests()
    {
        Branch[] branches = [new("feature", false, "Add feature", "1 day ago")];
        Worktree[] worktrees = [];
        PullRequest[] prs =
        [
            new(42, "Add feature", PullRequestState.Open, "feature", "https://example.com/42"),
        ];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(1, items);
        Assert.IsNotNull(items[0].PullRequest);
        Assert.AreEqual(42, items[0].PullRequest!.Number);
    }

    [TestMethod]
    public void Compose_BranchWithNoWorktreeOrPR()
    {
        Branch[] branches = [new("lonely", false, "Commit", "3 days ago")];
        Worktree[] worktrees = [];
        PullRequest[] prs = [];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(1, items);
        Assert.IsNull(items[0].Worktree);
        Assert.IsNull(items[0].PullRequest);
    }

    [TestMethod]
    public void Compose_PrefersOpenPRWhenMultipleExist()
    {
        Branch[] branches = [new("feature", false, "Commit", "1 day ago")];
        Worktree[] worktrees = [];
        PullRequest[] prs =
        [
            new(10, "Old attempt", PullRequestState.Closed, "feature", "https://example.com/10"),
            new(42, "New attempt", PullRequestState.Open, "feature", "https://example.com/42"),
        ];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(1, items);
        Assert.IsNotNull(items[0].PullRequest);
        Assert.AreEqual(42, items[0].PullRequest!.Number);
        Assert.AreEqual(PullRequestState.Open, items[0].PullRequest!.State);
    }

    [TestMethod]
    public void Compose_IgnoresDetachedWorktrees()
    {
        Branch[] branches = [new("main", true, "Init", "1 day ago")];
        Worktree[] worktrees = [new("/repo-detached", "")];
        PullRequest[] prs = [];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(1, items);
        Assert.IsNull(items[0].Worktree);
    }

    [TestMethod]
    public void Compose_UnmatchedPRsAreIgnored()
    {
        Branch[] branches = [new("main", true, "Init", "1 day ago")];
        Worktree[] worktrees = [];
        PullRequest[] prs =
        [
            new(
                99,
                "Someone else's PR",
                PullRequestState.Open,
                "not-a-local-branch",
                "https://example.com/99"
            ),
        ];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(1, items);
        Assert.IsNull(items[0].PullRequest);
    }

    [TestMethod]
    public void Compose_FullComposite()
    {
        Branch[] branches =
        [
            new("main", true, "Init", "2 days ago", "origin/main"),
            new("feature", false, "Add feature", "1 day ago"),
            new("old-branch", false, "Old work", "1 week ago", "origin/old-branch", "[gone]"),
        ];
        Worktree[] worktrees =
        [
            new("/repo", "main"),
            new("/repo-feature", "feature", IsDirty: true),
        ];
        PullRequest[] prs =
        [
            new(42, "Add feature", PullRequestState.Open, "feature", "https://example.com/42"),
            new(10, "Old work", PullRequestState.Merged, "old-branch", "https://example.com/10"),
        ];

        var items = WorkItem.Compose(branches, worktrees, prs);

        Assert.HasCount(3, items);

        // main — has worktree, no PR
        Assert.AreEqual("main", items[0].Branch.Name);
        Assert.IsNotNull(items[0].Worktree);
        Assert.IsNull(items[0].PullRequest);
        Assert.AreEqual("origin/main", items[0].Branch.Upstream);

        // feature — has worktree (dirty) and open PR
        Assert.AreEqual("feature", items[1].Branch.Name);
        Assert.IsNotNull(items[1].Worktree);
        Assert.IsTrue(items[1].Worktree!.IsDirty);
        Assert.IsNotNull(items[1].PullRequest);
        Assert.AreEqual(PullRequestState.Open, items[1].PullRequest!.State);

        // old-branch — no worktree, merged PR, gone upstream
        Assert.AreEqual("old-branch", items[2].Branch.Name);
        Assert.IsNull(items[2].Worktree);
        Assert.IsNotNull(items[2].PullRequest);
        Assert.AreEqual(PullRequestState.Merged, items[2].PullRequest!.State);
        Assert.AreEqual("[gone]", items[2].Branch.UpstreamTrack);
    }
}
