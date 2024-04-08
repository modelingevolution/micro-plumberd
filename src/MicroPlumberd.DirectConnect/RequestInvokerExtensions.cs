using ModelingEvolution.DirectConnect;
using System.Collections.Concurrent;
using MicroPlumberd.DirectConnect;

namespace MicroPlumberd
{
    public static class RequestInvokerExtensions
    {
        interface IInvoke<TResponse> { Task<TResponse> Execute(IRequestInvoker invoker, string id, object cmd); }
        private sealed class Invoker<TRequest, TResponse> : IInvoke<TResponse>  where TResponse : class
        {
            public Task<TResponse> Execute(IRequestInvoker invoker, string id, object cmd) => RequestInvokerExtensions.OnExecute<TRequest, TResponse>(invoker, id, (TRequest)cmd);
        }
        private static async Task<TResponse> OnExecute<TRequest, TResponse>(IRequestInvoker invoker, string id, TRequest cmd)
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
        /// <summary>
        /// Executes a command and gets a response of type TResponse.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response.</typeparam>
        /// <param name="ri">The IRequestInvoker instance on which this method is invoked.</param>
        /// <param name="id">The unique identifier for the command.</param>
        /// <param name="c">The command object.</param>
        /// <returns>A Task that represents the asynchronous operation. The task result contains the response of type TResponse.</returns>
        public static Task<TResponse> Execute<TResponse>(this IRequestInvoker ri, string id, object c)
        {
            var commandType = c.GetType();
            var invoker = (IInvoke<TResponse>)_invokers.GetOrAdd(commandType, x =>
            {
                var t = typeof(Invoker<,>).MakeGenericType(commandType, typeof(TResponse));
                return Activator.CreateInstance(t)!;
            });
            return invoker.Execute(ri, id, c);
        }
        /// <summary>
        /// Executes a command and gets a response of type TResponse.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response.</typeparam>
        /// <param name="ri">The IRequestInvoker instance on which this method is invoked.</param>
        /// <param name="id">The identifier.</param>
        /// <param name="c">The command object.</param>
        /// <returns></returns>
        public static Task<TResponse> Execute<TResponse>(this IRequestInvoker ri, object id, object c)
        {
            return Execute<TResponse>(ri, id?.ToString(), c);
        }
        /// <summary>
        /// Executes a command and gets a response of type HandlerOperationStatus.
        /// </summary>
        /// <param name="ri">The IRequestInvoker instance on which this method is invoked.</param>
        /// <param name="id">The unique identifier for the command.</param>
        /// <param name="c">The command object.</param>
        /// <returns>A Task that represents the asynchronous operation. The task result contains the response of type HandlerOperationStatus.</returns>

        public static Task Execute(this IRequestInvoker ri, string id, object c)
        {
            var commandType = c.GetType();
            var invoker = (IInvoke<HandlerOperationStatus>)_invokers.GetOrAdd(commandType, x =>
            {
                var t = typeof(Invoker<,>).MakeGenericType(commandType, typeof(HandlerOperationStatus));
                return Activator.CreateInstance(t)!;
            });
            return invoker.Execute(ri, id, c);
        }
        /// <summary>
        /// Executes a command and gets a response of type HandlerOperationStatus.
        /// </summary>
        /// <param name="ri">The IRequestInvoker instance on which this method is invoked.</param>
        /// <param name="id">The unique identifier for the command.</param>
        /// <param name="c">The command object.</param>
        /// <returns>A Task that represents the asynchronous operation. The task result contains the response of type HandlerOperationStatus.</returns>

        public static Task Execute(this IRequestInvoker ri, object id, object c) => Execute(ri, id.ToString(), c);
    }
}
