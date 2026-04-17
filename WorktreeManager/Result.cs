// SPDX-FileCopyrightText: Copyright (c) 2026 Logan Bussell
// SPDX-License-Identifier: MIT

public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;

    public static Result<T> Success(T value) => new Ok(value);
    public static Result<T> Failure(string message) => new Error(message);
}

public static class ResultExtensions
{
    extension<T>(Result<T> result)
    {
        public bool IsOk => result is Result<T>.Ok;
        public bool IsError => result is Result<T>.Error;

        public Result<U> Map<U>(Func<T, U> f) => result switch
        {
            Result<T>.Ok(var v) => Result<U>.Success(f(v)),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public Result<U> Bind<U>(Func<T, Result<U>> f) => result switch
        {
            Result<T>.Ok(var v) => f(v),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public async Task<Result<U>> BindAsync<U>(Func<T, Task<Result<U>>> f) => result switch
        {
            Result<T>.Ok(var v) => await f(v),
            Result<T>.Error(var msg) => Result<U>.Failure(msg),
            _ => throw new InvalidOperationException()
        };

        public T UnwrapOr(T fallback) => result switch
        {
            Result<T>.Ok(var v) => v,
            _ => fallback
        };

        public void Match(Action<T> onOk, Action<string> onError)
        {
            switch (result)
            {
                case Result<T>.Ok(var v): onOk(v); break;
                case Result<T>.Error(var msg): onError(msg); break;
            }
        }
    }

    extension<T>(Task<Result<T>> task)
    {
        public async Task<Result<U>> Bind<U>(Func<T, Result<U>> f)
        {
            var r = await task;
            return r.Bind(f);
        }

        public async Task<Result<U>> Bind<U>(Func<T, Task<Result<U>>> f)
        {
            var r = await task;
            return await r.BindAsync(f);
        }

        public async Task<Result<U>> Map<U>(Func<T, U> f)
        {
            var r = await task;
            return r.Map(f);
        }

        public async Task Match(Action<T> onOk, Action<string> onError)
        {
            var r = await task;
            r.Match(onOk, onError);
        }
    }
    extension<T>(Task<Result<T[]>> task)
    {
        public async Task<Result<U>[]> BindEach<U>(Func<T, Task<Result<U>>> f)
        {
            var r = await task;
            if (r is Result<T[]>.Error(var msg))
                return [Result<U>.Failure(msg)];

            var items = ((Result<T[]>.Ok)r).Value;
            var results = new Result<U>[items.Length];
            for (int i = 0; i < items.Length; i++)
                results[i] = await f(items[i]);
            return results;
        }
    }

    extension<T>(Result<T>[] results)
    {
        public Result<T[]> Sequence()
        {
            List<T> successes = [];
            List<string> errors = [];

            foreach (var r in results)
                r.Match(v => successes.Add(v), err => errors.Add(err));

            return errors.Count > 0
                ? Result<T[]>.Failure(string.Join("; ", errors))
                : Result<T[]>.Success([.. successes]);
        }
    }

    extension<T>(Task<Result<T>[]> task)
    {
        public async Task<Result<T[]>> Sequence()
        {
            var results = await task;
            return results.Sequence();
        }
    }
}
