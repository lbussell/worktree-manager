namespace WorktreeManager.Tests;

[TestClass]
public sealed class GitParsingTests
{
    [TestMethod]
    public void ParseBranches_SingleBranch()
    {
        var output = "*|main|Initial commit|2 days ago\n";
        var result = GitParsing.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.AreEqual(1, branches.Length);
        Assert.AreEqual("main", branches[0].Name);
        Assert.IsTrue(branches[0].IsCurrent);
        Assert.AreEqual("Initial commit", branches[0].LastCommit);
        Assert.AreEqual("2 days ago", branches[0].LastCommitDate);
    }

    [TestMethod]
    public void ParseBranches_MultipleBranches()
    {
        var output = "*|main|Initial commit|2 days ago\n |feature|Add feature|1 day ago\n";
        var result = GitParsing.ParseBranches(output);

        Assert.IsInstanceOfType<Result<Branch[]>.Ok>(result);
        var branches = ((Result<Branch[]>.Ok)result).Value;
        Assert.AreEqual(2, branches.Length);
        Assert.IsTrue(branches[0].IsCurrent);
        Assert.IsFalse(branches[1].IsCurrent);
        Assert.AreEqual("feature", branches[1].Name);
    }

    [TestMethod]
    public void ParseBranches_EmptyOutput_ReturnsFailure()
    {
        var result = GitParsing.ParseBranches("");
        Assert.IsInstanceOfType<Result<Branch[]>.Error>(result);
        Assert.AreEqual("No branches found", ((Result<Branch[]>.Error)result).Message);
    }

    [TestMethod]
    public void ParseWorktrees_SingleWorktree()
    {
        var output = "worktree /home/user/repo\nHEAD abc123\nbranch refs/heads/main\n";
        var result = GitParsing.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.AreEqual(1, worktrees.Length);
        Assert.AreEqual("/home/user/repo", worktrees[0].Path);
        Assert.AreEqual("main", worktrees[0].Branch);
    }

    [TestMethod]
    public void ParseWorktrees_MultipleWorktrees()
    {
        var output =
            "worktree /home/user/repo\nHEAD abc123\nbranch refs/heads/main\n\n"
            + "worktree /home/user/repo-feature\nHEAD def456\nbranch refs/heads/feature\n";
        var result = GitParsing.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.AreEqual(2, worktrees.Length);
        Assert.AreEqual("main", worktrees[0].Branch);
        Assert.AreEqual("feature", worktrees[1].Branch);
    }

    [TestMethod]
    public void ParseWorktrees_StripsRefsHeadsPrefix()
    {
        var output = "worktree /repo\nbranch refs/heads/my-branch\n";
        var result = GitParsing.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        Assert.AreEqual("my-branch", ((Result<Worktree[]>.Ok)result).Value[0].Branch);
    }

    [TestMethod]
    public void ParseWorktrees_EmptyOutput_ReturnsFailure()
    {
        var result = GitParsing.ParseWorktrees("");
        Assert.IsInstanceOfType<Result<Worktree[]>.Error>(result);
        Assert.AreEqual("No worktrees found", ((Result<Worktree[]>.Error)result).Message);
    }

    [TestMethod]
    public void ParseWorktrees_DetachedHead_NoBranch()
    {
        var output = "worktree /home/user/repo\nHEAD abc123\ndetached\n";
        var result = GitParsing.ParseWorktrees(output);

        Assert.IsInstanceOfType<Result<Worktree[]>.Ok>(result);
        var worktrees = ((Result<Worktree[]>.Ok)result).Value;
        Assert.AreEqual("", worktrees[0].Branch);
    }
}
