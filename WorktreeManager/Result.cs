// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;

    public sealed record Error(string Message) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);

    public static Result<T> Failure(string message) => new Error(message);
}
