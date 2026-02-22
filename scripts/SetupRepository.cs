// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

// Configures GitHub repository settings:
// 1. Enables release immutability
// 2. Disables wikis
// 3. Disables discussions
// 4. Disables merge commit for pull requests
//
// Usage: dotnet run scripts/SetupRepository.cs

#:package CliWrap@3.10.0
#:package Spectre.Console@0.54.1-alpha.0.31

using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

AnsiConsole.WriteLine();
(string? owner, string? repo) = await DetectGitHubRepoAsync();
await EnsureGhAuthenticatedAsync();
await RunEditRepoCommandAsync(owner, repo);
await EnableReleaseImmutabilityAsync(owner, repo);
Prompt.Success("Repository settings configured.");

async Task<(string Owner, string Repo)> DetectGitHubRepoAsync()
{
    BufferedCommandResult result = await Cli.Wrap("git")
        .WithArguments("remote get-url origin")
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    if (result.ExitCode != 0)
    {
        Prompt.Error("Could not detect git remote. Are you in a git repository?");
        Environment.Exit(1);
    }

    string url = result.StandardOutput.Trim();
    (string Owner, string Repo)? repo = ParseGitHubRepo(url);

    if (repo is null)
    {
        Prompt.Error($"Origin URL is not a GitHub repository: [dim]{url}[/]");
        Environment.Exit(1);
    }

    AnsiConsole.MarkupLine($"[bold]Detected repository:[/] [link]https://github.com/{repo.Value.Owner}/{repo.Value.Repo}[/]");

    if (!Prompt.Confirm("Is this correct?"))
    {
        AnsiConsole.MarkupLine("[yellow]Aborted.[/]");
        Environment.Exit(1);
    }

    return (repo.Value.Owner, repo.Value.Repo);
}

async Task EnsureGhAuthenticatedAsync()
{
    AnsiConsole.MarkupLine("Checking GitHub CLI authentication...");
    BufferedCommandResult result = await Cli.Wrap("gh")
        .WithArguments("auth status")
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

    if (result.ExitCode != 0)
    {
        Prompt.Error("The GitHub CLI is not authenticated. Run [blue]gh auth login[/] first.");
        Environment.Exit(1);
    }
}

static async Task EnableReleaseImmutabilityAsync(string owner, string repo)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Enabling [green]release immutability[/][/]");
    await GitHubCli.RunWithConfirmationAsync([
        "api", "--method", "PATCH", $"repos/{owner}/{repo}",
        "-f", "security_and_analysis[release_immutability][status]=enabled"
    ]);
    Prompt.Success("Release immutability enabled.");
}

static async Task RunEditRepoCommandAsync(string owner, string repo)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Disabling [green]wikis[/], [green]discussions[/], and [green]merge commit[/][/]");
    await GitHubCli.RunWithConfirmationAsync([
        "repo", "edit", $"{owner}/{repo}",
        "--enable-wiki=false",
        "--enable-discussions=false",
        "--enable-merge-commit=false"
    ]);
    Prompt.Success("Wikis, discussions, and merge commit disabled.");
}

internal static class Prompt
{
    public static bool Confirm(string message) => AnsiConsole.Confirm(message);
    public static string Ask(string message) => AnsiConsole.Prompt(new TextPrompt<string>(message).PromptStyle("blue"));
    public static void Success(string message) => AnsiConsole.MarkupLine($"[green]✓[/] {message}");
    public static void Skip() => AnsiConsole.MarkupLine("[yellow]Skipped.[/]");
    public static void Error(string message) => AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
}

internal static class GitHubCli
{
    private static readonly Command _gh = Cli.Wrap("gh");

    public static async Task RunWithConfirmationAsync(string[] arguments, string? stdinText = null)
    {
        Command command = _gh.WithArguments(arguments).WithValidation(CommandResultValidation.ZeroExitCode);

        string commandString = Markup.Escape(string.Join(' ', arguments));

        if (stdinText is not null)
            command = command.WithStandardInputPipe(PipeSource.FromString(stdinText));

        if (!Prompt.Confirm($"Run `[blue]gh {commandString}[/]`?"))
            throw new OperationCanceledException("User aborted the operation.");

        BufferedCommandResult result = await command.ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(result.StandardError))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(result.StandardError.Trim())}[/]");
    }
}

partial class Program
{
    // Match HTTPS: https://github.com/{owner}/{repo}.git
    // Match SSH:   git@github.com:{owner}/{repo}.git
    [GeneratedRegex(@"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)")]
    private static partial Regex GitHubUrlRegex { get; }

    private static (string Owner, string Repo)? ParseGitHubRepo(string url)
    {
        Match match = GitHubUrlRegex.Match(url);
        return !match.Success ? null : (match.Groups["owner"].Value, match.Groups["repo"].Value);
    }
}
