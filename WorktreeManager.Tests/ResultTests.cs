namespace WorktreeManager.Tests;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_CreatesOk()
    {
        var result = Result<int>.Success(42);
        Assert.IsInstanceOfType<Result<int>.Ok>(result);
        Assert.AreEqual(42, ((Result<int>.Ok)result).Value);
    }

    [TestMethod]
    public void Failure_CreatesError()
    {
        var result = Result<int>.Failure("boom");
        Assert.IsInstanceOfType<Result<int>.Error>(result);
        Assert.AreEqual("boom", ((Result<int>.Error)result).Message);
    }

    [TestMethod]
    public void IsOk_ReturnsCorrectly()
    {
        Assert.IsTrue(Result<int>.Success(1).IsOk);
        Assert.IsFalse(Result<int>.Failure("err").IsOk);
    }

    [TestMethod]
    public void IsError_ReturnsCorrectly()
    {
        Assert.IsTrue(Result<int>.Failure("err").IsError);
        Assert.IsFalse(Result<int>.Success(1).IsError);
    }

    [TestMethod]
    public void Map_Ok_TransformsValue()
    {
        var result = Result<int>.Success(5).Map(x => x * 2);
        Assert.IsInstanceOfType<Result<int>.Ok>(result);
        Assert.AreEqual(10, ((Result<int>.Ok)result).Value);
    }

    [TestMethod]
    public void Map_Error_PropagatesError()
    {
        var result = Result<int>.Failure("fail").Map(x => x * 2);
        Assert.IsInstanceOfType<Result<int>.Error>(result);
        Assert.AreEqual("fail", ((Result<int>.Error)result).Message);
    }

    [TestMethod]
    public void Bind_Ok_AppliesFunction()
    {
        var result = Result<int>.Success(5)
            .Bind(x => Result<string>.Success(x.ToString()));
        Assert.IsInstanceOfType<Result<string>.Ok>(result);
        Assert.AreEqual("5", ((Result<string>.Ok)result).Value);
    }

    [TestMethod]
    public void Bind_Ok_CanReturnError()
    {
        var result = Result<int>.Success(5)
            .Bind(_ => Result<string>.Failure("nope"));
        Assert.IsInstanceOfType<Result<string>.Error>(result);
        Assert.AreEqual("nope", ((Result<string>.Error)result).Message);
    }

    [TestMethod]
    public void Bind_Error_PropagatesError()
    {
        var result = Result<int>.Failure("fail")
            .Bind(x => Result<string>.Success(x.ToString()));
        Assert.IsInstanceOfType<Result<string>.Error>(result);
        Assert.AreEqual("fail", ((Result<string>.Error)result).Message);
    }

    [TestMethod]
    public async Task BindAsync_Ok_AppliesFunction()
    {
        var result = await Result<int>.Success(5)
            .BindAsync(x => Task.FromResult(Result<string>.Success(x.ToString())));
        Assert.IsInstanceOfType<Result<string>.Ok>(result);
        Assert.AreEqual("5", ((Result<string>.Ok)result).Value);
    }

    [TestMethod]
    public async Task BindAsync_Error_PropagatesError()
    {
        var result = await Result<int>.Failure("fail")
            .BindAsync(x => Task.FromResult(Result<string>.Success(x.ToString())));
        Assert.IsInstanceOfType<Result<string>.Error>(result);
        Assert.AreEqual("fail", ((Result<string>.Error)result).Message);
    }

    [TestMethod]
    public void UnwrapOr_Ok_ReturnsValue()
    {
        Assert.AreEqual(42, Result<int>.Success(42).UnwrapOr(0));
    }

    [TestMethod]
    public void UnwrapOr_Error_ReturnsFallback()
    {
        Assert.AreEqual(0, Result<int>.Failure("fail").UnwrapOr(0));
    }

    [TestMethod]
    public void Match_Ok_CallsOnOk()
    {
        string? captured = null;
        Result<string>.Success("hello").Match(v => captured = v, _ => { });
        Assert.AreEqual("hello", captured);
    }

    [TestMethod]
    public void Match_Error_CallsOnError()
    {
        string? captured = null;
        Result<string>.Failure("boom").Match(_ => { }, err => captured = err);
        Assert.AreEqual("boom", captured);
    }

    [TestMethod]
    public void Sequence_AllOk_ReturnsOkArray()
    {
        Result<int>[] results = [
            Result<int>.Success(1),
            Result<int>.Success(2),
            Result<int>.Success(3),
        ];
        var sequenced = results.Sequence();
        Assert.IsInstanceOfType<Result<int[]>.Ok>(sequenced);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ((Result<int[]>.Ok)sequenced).Value);
    }

    [TestMethod]
    public void Sequence_WithErrors_ReturnsJoinedErrors()
    {
        Result<int>[] results = [
            Result<int>.Success(1),
            Result<int>.Failure("err1"),
            Result<int>.Failure("err2"),
        ];
        var sequenced = results.Sequence();
        Assert.IsInstanceOfType<Result<int[]>.Error>(sequenced);
        Assert.AreEqual("err1; err2", ((Result<int[]>.Error)sequenced).Message);
    }

    [TestMethod]
    public async Task TaskBind_Ok_AppliesSyncFunction()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .Bind(x => Result<string>.Success(x.ToString()));
        Assert.IsInstanceOfType<Result<string>.Ok>(result);
        Assert.AreEqual("5", ((Result<string>.Ok)result).Value);
    }

    [TestMethod]
    public async Task TaskBind_Ok_AppliesAsyncFunction()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .Bind(x => Task.FromResult(Result<string>.Success(x.ToString())));
        Assert.IsInstanceOfType<Result<string>.Ok>(result);
        Assert.AreEqual("5", ((Result<string>.Ok)result).Value);
    }

    [TestMethod]
    public async Task TaskMap_Ok_TransformsValue()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .Map(x => x * 2);
        Assert.IsInstanceOfType<Result<int>.Ok>(result);
        Assert.AreEqual(10, ((Result<int>.Ok)result).Value);
    }

    [TestMethod]
    public async Task TaskMatch_Ok_CallsOnOk()
    {
        string? captured = null;
        await Task.FromResult(Result<string>.Success("hello"))
            .Match(v => captured = v, _ => { });
        Assert.AreEqual("hello", captured);
    }

    [TestMethod]
    public async Task TaskSequence_AllOk_ReturnsOkArray()
    {
        Result<int>[] results = [
            Result<int>.Success(1),
            Result<int>.Success(2),
        ];
        var sequenced = await Task.FromResult(results).Sequence();
        Assert.IsInstanceOfType<Result<int[]>.Ok>(sequenced);
        CollectionAssert.AreEqual(new[] { 1, 2 }, ((Result<int[]>.Ok)sequenced).Value);
    }
}
