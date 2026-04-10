#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

using Spectre.Console;

var directories = GetSourceDirectories();

var selected = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Select a directory:")
        .EnableSearch()
        .PageSize(15)
        .AddChoices(directories));

AnsiConsole.MarkupLine($"[bold green]{selected}[/]");

static string[] GetSourceDirectories()
{
    var srcDir = Environment.GetEnvironmentVariable("WORKTREE_MANAGER_SRC_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "src");

    if (!Directory.Exists(srcDir))
    {
        AnsiConsole.MarkupLine($"[red]Source directory not found:[/] {srcDir}");
        Environment.Exit(1);
    }

    return Directory.GetDirectories(srcDir)
        .Select(Path.GetFileName)
        .Where(name => name is not null)
        .ToArray()!;
}
