using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MicroPlumberd.Services.Identity
{
    /// <summary>
    /// Internal implementation of <see cref="IUserAuthContext"/> using function delegates.
    /// </summary>
    class UserAuthContextFunc(Func<IServiceProvider, Flow, Task<string>> Func, Func<IServiceProvider, ValueTask<Flow>> Flow, IServiceProvider sp) : IUserAuthContext
    {
        /// <summary>
        /// Gets the current user's unique identifier.
        /// </summary>
        /// <returns>The unique identifier of the current user.</returns>
        public async Task<string> GetCurrentUserId() => await Func(sp, await GetCurrentFlow());

        /// <summary>
        /// Gets the current flow context.
        /// </summary>
        /// <returns>The current flow context.</returns>
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

        /// <summary>
        /// Retrieves the current flow context.
        /// </summary>
        /// <returns>A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the result being the current <see cref="Flow"/>.</returns>
        ValueTask<Flow> GetCurrentFlow();
    }

    /// <summary>
    /// Decorator for <see cref="ICommandBus"/> that automatically sets the current user ID in the operation context for all commands.
    /// </summary>
    class CommandBusIdentityDecorator(ICommandBus commandBus, IUserAuthContext auth) : ICommandBus
    {
        /// <summary>
        /// Sends a command asynchronously to a recipient, automatically setting the current user ID in the operation context.
        /// </summary>
        /// <param name="recipientId">The identifier of the command recipient.</param>
        /// <param name="command">The command to send.</param>
        /// <param name="timeout">Optional timeout for the command execution.</param>
        /// <param name="fireAndForget">Indicates whether to wait for command completion.</param>
        /// <param name="token">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null, bool fireAndForget = false,
            CancellationToken token = default)
        {
            // So if we are top level, then we set userId. 
            using var context = await OperationContext.GetOrCreate(auth.GetCurrentFlow, async (x) => x.SetUserId(await auth.GetCurrentUserId()));
            await commandBus.SendAsync(recipientId, command, timeout, fireAndForget, token);
        }

        /// <summary>
        /// Queues a command asynchronously for a recipient, automatically setting the current user ID in the operation context.
        /// </summary>
        /// <param name="recipientId">The identifier of the command recipient.</param>
        /// <param name="command">The command to queue.</param>
        /// <param name="timeout">Optional timeout for the command execution.</param>
        /// <param name="fireAndForget">Indicates whether to wait for command completion. Defaults to true.</param>
        /// <param name="token">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null,
            bool fireAndForget = true,
            CancellationToken token = default)
        {
            using var context = await OperationContext.GetOrCreate(auth.GetCurrentFlow, async (x) => x.SetUserId(await auth.GetCurrentUserId()));
            await commandBus.QueueAsync(recipientId, command, timeout, fireAndForget, token);
        }

        /// <summary>
        /// Disposes of resources used by the command bus decorator.
        /// </summary>
        public void Dispose()
        {

        }

        /// <summary>
        /// Asynchronously disposes of resources used by the command bus decorator.
        /// </summary>
        /// <returns>A task representing the asynchronous disposal operation.</returns>
        public async ValueTask DisposeAsync()
        {

        }
    }
}
