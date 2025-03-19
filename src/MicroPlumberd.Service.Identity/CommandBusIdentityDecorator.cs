using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MicroPlumberd.Services.Identity
{
    class UserAuthContextFunc(Func<IServiceProvider, Flow, Task<string>> Func, Func<IServiceProvider, ValueTask<Flow>> Flow, IServiceProvider sp) : IUserAuthContext
    {
        public async Task<string> GetCurrentUserId() => await Func(sp, await GetCurrentFlow());
        public ValueTask<Flow> GetCurrentFlow() => Flow(sp);
    }
    /// <summary>
    /// Represents a context for user authentication, providing methods to interact with 
    /// the current user's authentication state and retrieve user-specific information.
    /// </summary>
    public interface IUserAuthContext
    {
        /// <summary>
        /// Retrieves the current user's unique identifier.
        /// </summary>
        /// <returns>
        /// A <see cref="Task{TResult}"/> representing the asynchronous operation, 
        /// with the result being the unique identifier of the current user as a <see cref="string"/>.
        /// </returns>
        Task<string> GetCurrentUserId();

        ValueTask<Flow> GetCurrentFlow();
    }
    class CommandBusIdentityDecorator(ICommandBus commandBus, IUserAuthContext auth) : ICommandBus
    {
        public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
            CancellationToken token = default)
        {
            // So if we are top level, then we set userId. 
            using var context = await OperationContext.GetOrCreate(auth.GetCurrentFlow, async (x) => x.SetUserId(await auth.GetCurrentUserId()));
            await commandBus.SendAsync(recipientId, command, timeout, fireAndForget, token);
        }

        public async Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null,
            bool fireAndForget = true,
            CancellationToken token = default)
        {
            using var context = await OperationContext.GetOrCreate(auth.GetCurrentFlow, async (x) => x.SetUserId(await auth.GetCurrentUserId()));
            await commandBus.QueueAsync(recipientId, command, timeout, fireAndForget, token);
        }

        public void Dispose()
        {
            
        }

        public async ValueTask DisposeAsync()
        {
            
        }
    }
}
