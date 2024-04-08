﻿using System.Runtime.CompilerServices;

namespace MicroPlumberd;

internal class AsyncLazy<T> : Lazy<Task<T>>
{
    public AsyncLazy(Func<T> valueFactory) :
        base(() => Task.Factory.StartNew(valueFactory))
    {
    }

    public AsyncLazy(Func<Task<T>> taskFactory) :
        base(() => Task.Factory.StartNew(taskFactory).Unwrap())
    {
    }

    public TaskAwaiter<T> GetAwaiter()
    {
        return Value.GetAwaiter();
    }
}