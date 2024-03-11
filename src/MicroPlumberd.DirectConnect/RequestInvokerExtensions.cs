﻿using ModelingEvolution.DirectConnect;
using System.Collections.Concurrent;

namespace MicroPlumberd.DirectConnect
{
    public static class RequestInvokerExtensions
    {
        interface IInvoke<TResponse> { Task<TResponse?> Execute(IRequestInvoker invoker, Guid id, ICommand cmd); }
        private sealed class Invoker<TRequest, TResponse> : IInvoke<TResponse> where TRequest : ICommand where TResponse : class
        {
            public Task<TResponse?> Execute(IRequestInvoker invoker, Guid id, ICommand cmd) => RequestInvokerExtensions.OnExecute<TRequest, TResponse>(invoker, id, (TRequest)cmd);
        }
        private static async Task<TResponse?> OnExecute<TRequest, TResponse>(IRequestInvoker invoker, Guid id, TRequest cmd)
            where TRequest : ICommand where TResponse : class
        {
            var result = await invoker.Invoke<CommandEnvelope<TRequest>, object>(new CommandEnvelope<TRequest>() { StreamId = id, Command = cmd });
            return result as TResponse;
        }
        private static readonly ConcurrentDictionary<Type, object> _invokers = new();
        public static Task<TResponse?> Execute<TResponse>(this IRequestInvoker ri, Guid id, ICommand c)
        {
            var commandType = c.GetType();
            var invoker = (IInvoke<TResponse>)_invokers.GetOrAdd(commandType, x =>
            {
                var t = typeof(Invoker<,>).MakeGenericType(commandType, typeof(TResponse));
                return Activator.CreateInstance(t)!;
            });
            return invoker.Execute(ri, id, c);
        }

    }
}
