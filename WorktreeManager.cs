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

static Task<Result<string>> GetWorkingDirectory()
{
    var dir = Directory.GetCurrentDirectory();
    var result = string.IsNullOrEmpty(dir)
        ? Result<string>.Failure("Could not determine working directory")
        : Result<string>.Success(dir);
    return Task.FromResult(result);
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

#region Result<T>

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(string message) => new Error(message);
}

public static class ResultExtensions
{
    extension<T>(Result<T> result)
    {
        public bool IsOk => result is Result<T>.Ok;
        public bool IsError => result is Result<T>.Error;

        public Result<U> Map<U>(Func<T, U> f) => result switch
        {
            Result<T>.Ok(var v) => Result<U>.Success(f(v)),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public Result<U> Bind<U>(Func<T, Result<U>> f) => result switch
        {
            Result<T>.Ok(var v) => f(v),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public async Task<Result<U>> BindAsync<U>(Func<T, Task<Result<U>>> f) => result switch
        {
            Result<T>.Ok(var v) => await f(v),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public T Unwrap() => result switch
        {
            Result<T>.Ok(var v) => v,
            Result<T>.Error(var msg) => throw new InvalidOperationException($"Unwrap called on Error: {msg}"),
            _ => throw new InvalidOperationException()
        };

        public T UnwrapOr(T fallback) => result switch
        {
            Result<T>.Ok(var v) => v,
            _ => fallback
        };

        public void Match(Action<T> onOk, Action<string> onError)
        {
            switch (result)
            {
                case Result<T>.Ok(var v): onOk(v); break;
                case Result<T>.Error(var msg): onError(msg); break;
            }
        }
    }

    extension<T>(Task<Result<T>> task)
    {
        public async Task<Result<U>> Bind<U>(Func<T, Result<U>> f)
        {
            var r = await task;
            return r.Bind(f);
        }

        public async Task<Result<U>> Bind<U>(Func<T, Task<Result<U>>> f)
        {
            var r = await task;
            return await r.BindAsync(f);
        }

        public async Task<Result<U>> Map<U>(Func<T, U> f)
        {
            var r = await task;
            return r.Map(f);
        }

        public async Task Match(Action<T> onOk, Action<string> onError)
        {
            var r = await task;
            r.Match(onOk, onError);
        }
    }
}

#endregion
