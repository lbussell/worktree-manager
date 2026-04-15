#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

#:property TargetFramework=net10.0
#:package Spectre.Console@0.*
#:package CliWrap@3.*

Console.WriteLine("Hello world!");

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(string message) => new Error(message);

    public Result<U> Map<U>(Func<T, U> f) => this switch
    {
        Ok(var v) => Result<U>.Success(f(v)),
        Error(var msg) => Result<U>.Failure(msg),
        _ => throw new InvalidOperationException()
    };

    public Result<U> Bind<U>(Func<T, Result<U>> f) => this switch
    {
        Ok(var v) => f(v),
        Error(var msg) => Result<U>.Failure(msg),
        _ => throw new InvalidOperationException()
    };

    public T Unwrap() => this switch
    {
        Ok(var v) => v,
        Error(var msg) => throw new InvalidOperationException($"Unwrap called on Error: {msg}"),
        _ => throw new InvalidOperationException()
    };

    public T UnwrapOr(T fallback) => this switch
    {
        Ok(var v) => v,
        _ => fallback
    };

    public bool IsOk => this is Ok;
    public bool IsError => this is Error;
}
