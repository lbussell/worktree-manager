namespace WorktreeManager.Tests;

[TestClass]
public sealed class GitTests
{
    [TestMethod]
    public void ParseBranches_SingleBranch()
    {
        var output = "*\tmain\t2 days ago\torigin/main\t[ahead 1]\tInitial commit\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.HasCount(1, branches);
        Assert.AreEqual("main", branches[0].Name);
        Assert.IsTrue(branches[0].IsCurrent);
        Assert.AreEqual("Initial commit", branches[0].LastCommit);
        Assert.AreEqual("2 days ago", branches[0].LastCommitDate);
        Assert.AreEqual("origin/main", branches[0].Upstream);
        Assert.AreEqual("[ahead 1]", branches[0].UpstreamTrack);
    }

    [TestMethod]
    public void ParseBranches_MultipleBranches()
    {
        var output =
            "*\tmain\t2 days ago\torigin/main\t\tInitial commit\n"
            + " \tfeature\t1 day ago\t\t\tAdd feature\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.HasCount(2, branches);
        Assert.IsTrue(branches[0].IsCurrent);
        Assert.AreEqual("origin/main", branches[0].Upstream);
        Assert.IsNull(branches[0].UpstreamTrack);
        Assert.IsFalse(branches[1].IsCurrent);
        Assert.AreEqual("feature", branches[1].Name);
        Assert.IsNull(branches[1].Upstream);
        Assert.IsNull(branches[1].UpstreamTrack);
    }

    [TestMethod]
    public void ParseBranches_EmptyOutput_ReturnsFailure()
    {
        var result = Git.ParseBranches("");
        Assert.IsInstanceOfType<Result<Branch[]>.Error>(result);
        Assert.AreEqual("No branches found", ((Result<Branch[]>.Error)result).Message);
    }

    [TestMethod]
    public void ParseBranches_CommitMessageWithPipes()
    {
        var output = " \tfix\t3 hours ago\t\t\tUse a | b syntax\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.AreEqual("Use a | b syntax", branches[0].LastCommit);
    }

    [TestMethod]
    public void ParseBranches_UpstreamAheadAndBehind()
    {
        var output = " \tmain\t5 days ago\torigin/main\t[ahead 2, behind 3]\tSome commit\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.AreEqual("origin/main", branches[0].Upstream);
        Assert.AreEqual("[ahead 2, behind 3]", branches[0].UpstreamTrack);
    }

    [TestMethod]
    public void ParseBranches_UpstreamGone()
    {
        var output = " \told-branch\t1 week ago\torigin/old-branch\t[gone]\tOld commit\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.AreEqual("origin/old-branch", branches[0].Upstream);
        Assert.AreEqual("[gone]", branches[0].UpstreamTrack);
    }

    [TestMethod]
    public void ParseBranches_NoUpstream()
    {
        var output = " \tlocal-only\t2 hours ago\t\t\tLocal commit\n";
        var result = Git.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.IsNull(branches[0].Upstream);
        Assert.IsNull(branches[0].UpstreamTrack);
    }

    [TestMethod]
    public void ParseWorktrees_SingleWorktree()
    {
        var output = "worktree /home/user/repo\nHEAD abc123\nbranch refs/heads/main\n";
        var result = Git.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.HasCount(1, worktrees);
        Assert.AreEqual("/home/user/repo", worktrees[0].Path);
        Assert.AreEqual("main", worktrees[0].Branch);
        Assert.IsFalse(worktrees[0].IsDirty);
    }

    [TestMethod]
    public void ParseWorktrees_MultipleWorktrees()
    {
        var output =
            "worktree /home/user/repo\nHEAD abc123\nbranch refs/heads/main\n\n"
            + "worktree /home/user/repo-feature\nHEAD def456\nbranch refs/heads/feature\n";
        var result = Git.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.HasCount(2, worktrees);
        Assert.AreEqual("main", worktrees[0].Branch);
        Assert.AreEqual("feature", worktrees[1].Branch);
    }

    [TestMethod]
    public void ParseWorktrees_StripsRefsHeadsPrefix()
    {
        var output = "worktree /repo\nbranch refs/heads/my-branch\n";
        var result = Git.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        Assert.AreEqual("my-branch", ((Result<Worktree[]>.Ok)result).Value[0].Branch);
    }

    [TestMethod]
    public void ParseWorktrees_EmptyOutput_ReturnsFailure()
    {
        var result = Git.ParseWorktrees("");
        Assert.IsInstanceOfType<Result<Worktree[]>.Error>(result);
        Assert.AreEqual("No worktrees found", ((Result<Worktree[]>.Error)result).Message);
    }

    [TestMethod]
    public void ParseWorktrees_DetachedHead_NoBranch()
    {
        var output = "worktree /home/user/repo\nHEAD abc123\ndetached\n";
        var result = Git.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.AreEqual("", worktrees[0].Branch);
    }
}
