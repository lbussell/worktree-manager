#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

AnsiConsole.WriteLine();

MenuOption[] menuOptions = [
    new("Branches", ChooseBranch),
    new("Worktrees", ChooseWorktree),
    new("Exit", () => Task.FromResult(Result<string>.Success("Exiting"))),
];

await Choose(menuOptions, o => o.Name)
    .BindAsync(option => option.Action())
    .Match(PrintOk, PrintError);

static async Task<Result<string>> ChooseBranch() =>
    await GetWorkingDirectory()
        .BindAsync(GetGitBranches)
        .Bind(branches => Choose(branches, FormatBranchSpectreConsole, "Select a branch:"))
        .Map(branch => branch.Name);

static async Task<Result<string>> ChooseWorktree() =>
    await GetWorkingDirectory()
        .BindAsync(GetGitWorktrees)
        .Bind(worktrees => Choose(worktrees, FormatWorktree, "Select a worktree:"))
        .Map(wt => wt.Path);

static Result<string> GetWorkingDirectory()
{
    var dir = Directory.GetCurrentDirectory();
    return string.IsNullOrEmpty(dir)
        ? Result<string>.Failure("Could not determine working directory")
        : Result<string>.Success(dir);
}

static async Task<Result<Branch[]>> GetGitBranches(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(["branch",
                "--sort=-committerdate",
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

static async Task<Result<Worktree[]>> GetGitWorktrees(string workingDirectory)
{
    try
    {
        var result = await Cli.Wrap("git")
            .WithWorkingDirectory(workingDirectory)
            .WithArguments(["worktree", "list", "--porcelain"])
            .ExecuteBufferedAsync();

        var worktrees = result.StandardOutput
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var path = lines.FirstOrDefault(l => l.StartsWith("worktree "))?["worktree ".Length..] ?? "";
                var branch = lines.FirstOrDefault(l => l.StartsWith("branch "))?["branch ".Length..] ?? "";
                if (branch.StartsWith("refs/heads/"))
                    branch = branch["refs/heads/".Length..];
                return new Worktree(path, branch);
            })
            .ToArray();

        return worktrees.Length == 0
            ? Result<Worktree[]>.Failure("No worktrees found")
            : Result<Worktree[]>.Success(worktrees);
    }
    catch (Exception ex)
    {
        return Result<Worktree[]>.Failure(ex.Message);
    }
}

static Result<T> Choose<T>(T[] choices, Func<T, string> displayConverter, string? title = null) where T : notnull
{
    if (choices.Length == 0)
        return Result<T>.Failure("No choices available");

    var prompt = new SelectionPrompt<T>()
        .UseConverter(displayConverter)
        .AddChoices(choices);

    if (!string.IsNullOrEmpty(title))
        prompt.Title(title);

    var selected = AnsiConsole.Prompt(prompt);

    return Result<T>.Success(selected);
}

static string FormatBranchSpectreConsole(Branch b) =>
    $"{(b.IsCurrent ? "*" : "")}({Markup.Escape(b.LastCommitDate)}) {Markup.Escape(b.Name)} [gray]{Spectre.Console.Markup.Escape(b.LastCommit)}[/]";

static string FormatWorktree(Worktree wt) =>
    $"{Markup.Escape(wt.Path)} [blue]{Markup.Escape(wt.Branch)}[/]";

static void PrintOk(string result) => AnsiConsole.MarkupLineInterpolated($"[green]OK[/]: {result}");

static void PrintError(string message) => AnsiConsole.MarkupLineInterpolated($"[red]Error[/]: {message}");

public record Branch(string Name, bool IsCurrent, string LastCommit, string LastCommitDate);
public record Worktree(string Path, string Branch);
public record MenuOption(string Name, Func<Task<Result<string>>> Action);

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
