_default:
    @just --list

build: _format
    dotnet publish src/WorktreeManager

run:
    dotnet run --project src/WorktreeManager

_format: _tool-restore
    dotnet fantomas .

_tool-restore:
    dotnet tool restore
