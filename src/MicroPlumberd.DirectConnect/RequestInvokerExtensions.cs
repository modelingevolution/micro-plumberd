using ModelingEvolution.DirectConnect;
using System.Collections.Concurrent;
using MicroPlumberd.DirectConnect;

namespace MicroPlumberd
{
    public static class RequestInvokerExtensions
    {
        interface IInvoke<TResponse> { Task<TResponse> Execute(IRequestInvoker invoker, Guid id, object cmd); }
        private sealed class Invoker<TRequest, TResponse> : IInvoke<TResponse>  where TResponse : class
        {
            public Task<TResponse> Execute(IRequestInvoker invoker, Guid id, object cmd) => RequestInvokerExtensions.OnExecute<TRequest, TResponse>(invoker, id, (TRequest)cmd);
        }
        private static async Task<TResponse> OnExecute<TRequest, TResponse>(IRequestInvoker invoker, Guid id, TRequest cmd)
            where TResponse : class
        {
            var result = await invoker.Invoke<CommandEnvelope<TRequest>, object>(new CommandEnvelope<TRequest>() { StreamId = id, Command = cmd });
            if(result is not IFaultEnvelope)
                return (TResponse)result;
            var fault = (IFaultEnvelope)result;
            if (fault.Data != null)
                throw FaultException.Create(fault.Error, fault.Data, (int)fault.Code);
            throw new FaultException(fault.Error, (int)fault.Code);
        }
        private static readonly ConcurrentDictionary<Type, object> _invokers = new();
        public static Task<TResponse> Execute<TResponse>(this IRequestInvoker ri, Guid id, object c)
        {
            var commandType = c.GetType();
            var invoker = (IInvoke<TResponse>)_invokers.GetOrAdd(commandType, x =>
            {
                var t = typeof(Invoker<,>).MakeGenericType(commandType, typeof(TResponse));
                return Activator.CreateInstance(t)!;
            });
            return invoker.Execute(ri, id, c);
        }
        public static Task Execute(this IRequestInvoker ri, Guid id, object c)
        {
            var commandType = c.GetType();
            var invoker = (IInvoke<HandlerOperationStatus>)_invokers.GetOrAdd(commandType, x =>
            {
                var t = typeof(Invoker<,>).MakeGenericType(commandType, typeof(HandlerOperationStatus));
                return Activator.CreateInstance(t)!;
            });
            return invoker.Execute(ri, id, c);
        }

    }
}
