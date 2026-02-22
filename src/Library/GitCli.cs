// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

using System.Text;
using CliWrap;

namespace WorktreeManager;

/// <inheritdoc />
internal sealed class GitCli : IGitCli
{
    private static readonly Command Git = Cli.Wrap("git");

    /// <inheritdoc />
    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default
    )
    {
        StringBuilder stdOut = new();
        StringBuilder stdErr = new();

        CommandResult result = await Git.WithArguments(args)
            .WithWorkingDirectory(workingDirectory)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken);

        return new GitResult(
            StandardOutput: stdOut.ToString(),
            StandardError: stdErr.ToString(),
            ExitCode: result.ExitCode
        );
    }
}
