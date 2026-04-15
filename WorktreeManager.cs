#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

using CliWrap;
using CliWrap.Buffered;

await GetWorkingDirectory()
    .Bind(GetGitBranches)
    .Match(PrintBranches, PrintError);

static void PrintBranches(string[] branches)
{
    Console.WriteLine("Branches:");
    foreach (var branch in branches)
        Console.WriteLine($"  {branch}");
}

static void PrintError(string message)
{
    Console.WriteLine($"Error: {message}");
}

static async Task<Result<string>> GetWorkingDirectory()
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithArguments(["rev-parse", "--show-toplevel"])
            .ExecuteBufferedAsync();

        var dir = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(dir)
            ? Result<string>.Failure("Not in a git repository")
            : Result<string>.Success(dir);
    }
    catch (Exception ex)
    {
        return Result<string>.Failure(ex.Message);
    }
}

static async Task<Result<string[]>> GetGitBranches(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(["branch", "--format=%(refname:short)"])
            .ExecuteBufferedAsync();

        var branches = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return branches.Length == 0
            ? Result<string[]>.Failure("No branches found")
            : Result<string[]>.Success(branches);
    }
    catch (Exception ex)
    {
        return Result<string[]>.Failure(ex.Message);
    }
}

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(string message) => new Error(message);

    public Result<U> Map<U>(Func<T, U> f) => this switch
    {
        Ok(var v) => Result<U>.Success(f(v)),
        Error(var msg) => Result<U>.Failure(msg),
        _ => throw new InvalidOperationException()
    };

    public Result<U> Bind<U>(Func<T, Result<U>> f) => this switch
    {
        Ok(var v) => f(v),
        Error(var msg) => Result<U>.Failure(msg),
        _ => throw new InvalidOperationException()
    };

    public T Unwrap() => this switch
    {
        Ok(var v) => v,
        Error(var msg) => throw new InvalidOperationException($"Unwrap called on Error: {msg}"),
        _ => throw new InvalidOperationException()
    };

    public T UnwrapOr(T fallback) => this switch
    {
        Ok(var v) => v,
        _ => fallback
    };

    public void Match(Action<T> onOk, Action<string> onError)
    {
        switch (this)
        {
            case Ok(var v): onOk(v); break;
            case Error(var msg): onError(msg); break;
        }
    }

    public async Task<Result<U>> BindAsync<U>(Func<T, Task<Result<U>>> f) => this switch
    {
        Ok(var v) => await f(v),
        Error(var msg) => Result<U>.Failure(msg),
        _ => throw new InvalidOperationException()
    };

    public bool IsOk => this is Ok;
    public bool IsError => this is Error;
}

public static class ResultExtensions
{
    public static async Task<Result<U>> Bind<T, U>(this Task<Result<T>> task, Func<T, Result<U>> f)
    {
        var result = await task;
        return result.Bind(f);
    }

    public static async Task<Result<U>> Bind<T, U>(this Task<Result<T>> task, Func<T, Task<Result<U>>> f)
    {
        var result = await task;
        return await result.BindAsync(f);
    }

    public static async Task<Result<U>> Map<T, U>(this Task<Result<T>> task, Func<T, U> f)
    {
        var result = await task;
        return result.Map(f);
    }

    public static async Task Match<T>(this Task<Result<T>> task, Action<T> onOk, Action<string> onError)
    {
        var result = await task;
        result.Match(onOk, onError);
    }
}
