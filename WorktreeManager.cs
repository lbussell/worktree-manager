#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

var result = await Cli.Wrap("git")
    .WithArguments("status")
    .ExecuteBufferedAsync();

AnsiConsole.MarkupLine("[bold green]Hello world[/]");
AnsiConsole.WriteLine(result.StandardOutput);
