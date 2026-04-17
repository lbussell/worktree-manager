// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;

public static class GitHub
{
    public static PullRequest[] ParsePullRequests(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc
                .RootElement.EnumerateArray()
                .Select(el => new PullRequest(
                    Number: el.GetProperty("number").GetInt32(),
                    Title: el.GetProperty("title").GetString() ?? "",
                    State: ParseState(el.GetProperty("state").GetString()),
                    HeadBranch: el.GetProperty("headRefName").GetString() ?? "",
                    Url: el.GetProperty("url").GetString() ?? ""
                ))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static PullRequestState ParseState(string? state) =>
        state switch
        {
            "OPEN" => PullRequestState.Open,
            "MERGED" => PullRequestState.Merged,
            "CLOSED" => PullRequestState.Closed,
            _ => PullRequestState.Closed,
        };

    /// <summary>
    /// Fetches pull requests from GitHub. Best-effort: returns empty on any failure
    /// (gh not installed, not authenticated, not a GitHub repo, network error, etc.)
    /// </summary>
    public static async Task<PullRequest[]> GetPullRequests(string workingDirectory)
    {
        try
        {
            var result = await Cli.Wrap("gh")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(
                    [
                        "pr",
                        "list",
                        "--state",
                        "all",
                        "--limit",
                        "100",
                        "--json",
                        "number,title,state,headRefName,url",
                    ]
                )
                .ExecuteBufferedAsync();
            return ParsePullRequests(result.StandardOutput);
        }
        catch
        {
            return [];
        }
    }
}
