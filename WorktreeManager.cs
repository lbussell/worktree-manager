#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:package Spectre.Console@*

using System.Diagnostics;
using Spectre.Console;

var worktrees = GitWorktreeReader.GetWorktrees();

if (worktrees.Count == 0)
{
	Console.Error.WriteLine("No git worktrees were found.");
	return 1;
}

var console = AnsiConsole.Create(new AnsiConsoleSettings
{
	Out = new AnsiConsoleOutput(Console.Error),
});

var selectedWorktree = console.Prompt(
	new SelectionPrompt<GitWorktree>()
		.Title("Select a [green]worktree[/]:")
		.PageSize(10)
		.UseConverter(GitWorktreeReader.FormatForSelection)
        .EnableSearch()
		.AddChoices(worktrees));

Console.Out.WriteLine(selectedWorktree.Path);
return 0;

readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

readonly record struct GitWorktree(
	string Path,
	string Head,
	string? Branch,
	bool IsBare,
	bool IsDetached,
	string? LockedReason,
	string? PrunableReason);

static class ProcessHelper
{
	public static ProcessResult Run(string fileName, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = fileName,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

		var standardOutput = process.StandardOutput.ReadToEnd();
		var standardError = process.StandardError.ReadToEnd();

		process.WaitForExit();

		return new ProcessResult(process.ExitCode, standardOutput, standardError);
	}
}

static class GitWorktreeReader
{
	public static IReadOnlyList<GitWorktree> GetWorktrees()
	{
		var result = ProcessHelper.Run("git", "worktree", "list", "--porcelain");

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"git worktree list failed with exit code {result.ExitCode}:{Environment.NewLine}{result.StandardError}");
		}

		return ParseWorktrees(result.StandardOutput);
	}

	public static string FormatForSelection(GitWorktree worktree)
	{
		var details = new List<string>();

		if (!string.IsNullOrWhiteSpace(worktree.Branch))
		{
			details.Add(worktree.Branch);
		}
		else if (worktree.IsDetached)
		{
			details.Add("detached");
		}

		if (worktree.IsBare)
		{
			details.Add("bare");
		}

		if (!string.IsNullOrWhiteSpace(worktree.LockedReason))
		{
			details.Add($"locked: {worktree.LockedReason}");
		}

		if (!string.IsNullOrWhiteSpace(worktree.PrunableReason))
		{
			details.Add($"prunable: {worktree.PrunableReason}");
		}

		return details.Count == 0
			? Markup.Escape(worktree.Path)
			: $"{Markup.Escape(worktree.Path)} [[{Markup.Escape(string.Join(", ", details))}]]";
	}

	private static IReadOnlyList<GitWorktree> ParseWorktrees(string output)
	{
		var worktrees = new List<GitWorktree>();
		string? path = null;
		string? head = null;
		string? branch = null;
		var isBare = false;
		var isDetached = false;
		string? lockedReason = null;
		string? prunableReason = null;

		void FlushCurrent()
		{
			if (path is null)
			{
				return;
			}

			if (head is null)
			{
				throw new FormatException($"Missing HEAD for worktree '{path}'.");
			}

			worktrees.Add(new GitWorktree(path, head, branch, isBare, isDetached, lockedReason, prunableReason));
			path = null;
			head = null;
			branch = null;
			isBare = false;
			isDetached = false;
			lockedReason = null;
			prunableReason = null;
		}

		foreach (var rawLine in output.Split('\n'))
		{
			var line = rawLine.TrimEnd('\r');

			if (string.IsNullOrWhiteSpace(line))
			{
				FlushCurrent();
				continue;
			}

			if (line.StartsWith("worktree ", StringComparison.Ordinal))
			{
				if (path is not null)
				{
					FlushCurrent();
				}

				path = line["worktree ".Length..];
				continue;
			}

			if (path is null)
			{
				throw new FormatException($"Encountered metadata before worktree path: '{line}'.");
			}

			if (line.StartsWith("HEAD ", StringComparison.Ordinal))
			{
				head = line["HEAD ".Length..];
				continue;
			}

			if (line.StartsWith("branch ", StringComparison.Ordinal))
			{
				branch = line["branch ".Length..];
				continue;
			}

			if (line == "bare")
			{
				isBare = true;
				continue;
			}

			if (line == "detached")
			{
				isDetached = true;
				continue;
			}

			if (line.StartsWith("locked", StringComparison.Ordinal))
			{
				lockedReason = ParseOptionalValue(line, "locked");
				continue;
			}

			if (line.StartsWith("prunable", StringComparison.Ordinal))
			{
				prunableReason = ParseOptionalValue(line, "prunable");
				continue;
			}

			throw new FormatException($"Unsupported git worktree line: '{line}'.");
		}

		FlushCurrent();
		return worktrees;
	}

	private static string? ParseOptionalValue(string line, string key)
	{
		return line.Length == key.Length
			? null
			: line[(key.Length + 1)..];
	}
}
