#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

await GetWorkingDirectory()
    .Bind(GetGitBranches)
    .Bind(branches => Choose("Select a branch:", branches, FormatBranch))
    .Match(PrintResult, PrintError);

static string FormatBranch(Branch b) =>
    $"{(b.IsCurrent ? "* " : "  ")}{b.Name} ({b.LastCommitDate}) [dim]{b.LastCommit}[/]";

static void PrintResult(Branch branch)
{
    AnsiConsole.MarkupLine($"[green]Selected:[/] {branch.Name}");
}

static void PrintError(string message)
{
    Console.WriteLine($"Error: {message}");
}

static Result<T> Choose<T>(string title, T[] choices, Func<T, string> display) where T : notnull
{
    if (choices.Length == 0)
        return Result<T>.Failure("No choices available");

    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<T>()
            .Title(title)
            .UseConverter(display)
            .AddChoices(choices));

    return Result<T>.Success(selected);
}

static Task<Result<string>> GetWorkingDirectory()
{
    var dir = Directory.GetCurrentDirectory();
    var result = string.IsNullOrEmpty(dir)
        ? Result<string>.Failure("Could not determine working directory")
        : Result<string>.Success(dir);
    return Task.FromResult(result);
}

static async Task<Result<Branch[]>> GetGitBranches(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(["branch",
                "--format=%(HEAD)|%(refname:short)|%(subject)|%(creatordate:relative)"])
            .ExecuteBufferedAsync();

        var branches = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('|', 4);
                return new Branch(
                    Name: parts[1],
                    IsCurrent: parts[0] == "*",
                    LastCommit: parts[2],
                    LastCommitDate: parts[3]);
            })
            .ToArray();

        return branches.Length == 0
            ? Result<Branch[]>.Failure("No branches found")
            : Result<Branch[]>.Success(branches);
    }
    catch (Exception ex)
    {
        return Result<Branch[]>.Failure(ex.Message);
    }
}

public record Branch(string Name, bool IsCurrent, string LastCommit, string LastCommitDate);

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
