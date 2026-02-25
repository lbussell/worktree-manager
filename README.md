# Worktree Manager

A CLI tool for managing git worktrees across multiple repositories in a source directory. Built as a [.NET file-based app](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10#file-based-apps) using [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework), [CliWrap](https://github.com/Tyrrrz/CliWrap), and [Spectre.Console](https://spectreconsole.net/).

## Setup

1. Build the tool:

   ```bash
   dotnet publish WorktreeManager.cs -o artifacts/WorktreeManager
   ```

2. Add a shell alias and helper functions to your `.zshrc`:

   ```zsh
   alias wt="/path/to/artifacts/WorktreeManager/WorktreeManager"
   wtcd() { pushd "$(wt d "$1")" }
   wtcp() { pushd "$(wt d "$1")" && if [[ -n "$2" ]]; then copilot --yolo -i "$2"; else copilot --yolo; fi }
   ```

3. (Optional) Update `SrcRoot` in the `Config` class in `WorktreeManager.cs` to point to your source directory (defaults to `~/src`).

## Commands

| Command | Alias | Description |
|---|---|---|
| `wt add <repodir>` | `a` | Register a repo for worktree management |
| `wt list` | `l` | List managed repos and their worktrees |
| `wt create <repo> <name>` | `new`, `n` | Create a new worktree |
| `wt create <repo> --pr <num>` | | Create a worktree and check out a PR |
| `wt remove <ref>` | `rm` | Remove a worktree or untrack a repo |
| `wt dir <ref>` | `d` | Print the path to a repo or worktree |

### Create options

| Flag | Description |
|---|---|
| `--branch`, `-b` | Create a new branch matching the worktree name |
| `--from`, `-f` | Base ref for the new branch (requires `--branch`) |
| `--pr`, `-p` | PR number to check out in the new worktree |

## Shell functions

| Function | Description |
|---|---|
| `wtcd <id>` | `pushd` to the repo or worktree directory |
| `wtcp <id>` | `pushd` and start `copilot --yolo` |
| `wtcp <id> "<prompt>"` | `pushd` and run `copilot --yolo -i "<prompt>"` |

## Short IDs

All commands accept short IDs (shown in `wt list` output) in place of full names. Short IDs are a 3-character hex string derived from an FNV-1a 32-bit hash of the repo/worktree name, so they are stable and unique.

## License

[MIT](LICENSE)
