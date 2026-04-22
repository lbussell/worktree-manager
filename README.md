# Worktree Manager

Minimal .NET 10 + F# console app with a small [Spectre.Console](https://spectreconsole.net/) example.

## Requirements

- A .NET SDK with `net10.0` support
- [`just`](https://github.com/casey/just) for convenience commands

## Development

Run `just` to see available commands.

Common commands:

```bash
just build
just run
just format
```

The sample app asks a simple yes/no question using Spectre.Console.

If you prefer, you can also run the app directly:

```bash
dotnet run --project src/WorktreeManager/WorktreeManager.fsproj
```

## License

[MIT](LICENSE)
