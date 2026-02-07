using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

/// <summary>
/// Registry of command types that should be executed in-process instead of via EventStore.
/// Stores both the command type and whether its handler is a singleton (determined at registration time
/// by inspecting <see cref="ServiceDescriptor.Lifetime"/> in the <see cref="IServiceCollection"/>).
/// </summary>
internal class InProcCommandRegistry
{
    private readonly Dictionary<Type, bool> _commandTypes = new();

    /// <summary>
    /// Registers a command type for in-process execution.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="isSingleton">Whether the handler is registered as singleton.</param>
    public void Register(Type commandType, bool isSingleton) => _commandTypes[commandType] = isSingleton;

    /// <summary>
    /// Checks whether the given command type is registered for in-process execution.
    /// </summary>
    public bool IsRegistered(Type commandType) => _commandTypes.ContainsKey(commandType);

    /// <summary>
    /// Returns true if the handler for the given command type is registered as singleton.
    /// </summary>
    public bool IsSingleton(Type commandType) => _commandTypes.TryGetValue(commandType, out var v) && v;
}

/// <summary>
/// Decorator for <see cref="ICommandBus"/> that intercepts commands registered for in-process execution.
/// For registered command types, resolves the <see cref="ICommandHandler"/> from DI and calls
/// <see cref="ICommandHandler.Execute"/> directly — skipping EventStore entirely.
/// For non-registered types, delegates to the inner (next) command bus.
/// <para>
/// If the handler is registered as singleton, it is resolved once and cached — no scope is created.
/// If scoped, a new <see cref="AsyncServiceScope"/> is created per invocation.
/// </para>
/// </summary>
internal class InProcCommandBusDecorator : ICommandBus
{
    private readonly ICommandBus _next;
    private readonly InProcCommandRegistry _registry;
    private readonly IServiceProvider _sp;
    private readonly ILogger<InProcCommandBusDecorator> _logger;
    private readonly ConcurrentDictionary<Type, HandlerInfo> _cache = new();

    public InProcCommandBusDecorator(
        ICommandBus next,
        InProcCommandRegistry registry,
        IServiceProvider sp,
        ILogger<InProcCommandBusDecorator> logger)
    {
        _next = next;
        _registry = registry;
        _sp = sp;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(object recipientId, object command, TimeSpan? timeout = null,
        bool fireAndForget = false, CancellationToken token = default)
    {
        if (!_registry.IsRegistered(command.GetType()))
        {
            await _next.SendAsync(recipientId, command, timeout, fireAndForget, token);
            return;
        }

        await ExecuteInProc(recipientId, command);
    }

    /// <inheritdoc/>
    public async Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null,
        bool fireAndForget = true, CancellationToken token = default)
    {
        if (!_registry.IsRegistered(command.GetType()))
        {
            await _next.QueueAsync(recipientId, command, timeout, fireAndForget, token);
            return;
        }

        await ExecuteInProc(recipientId, command);
    }

    private async Task ExecuteInProc(object recipientId, object command)
    {
        var commandType = command.GetType();
        var info = _cache.GetOrAdd(commandType, t => new HandlerInfo(
            typeof(ICommandHandler<>).MakeGenericType(t),
            _registry.IsSingleton(t)));

        _logger.LogDebug("Executing command {CommandType} in-process for recipient {RecipientId}.",
            commandType.Name, recipientId);

        if (info.IsSingleton)
        {
            // Singleton — resolve once from root, no scope needed
            var handler = info.SingletonHandler ??= (ICommandHandler)_sp.GetRequiredService(info.HandlerInterfaceType);
            await handler.Execute(recipientId.ToString()!, command);
        }
        else
        {
            // Scoped — create a scope per invocation
            await using var scope = _sp.CreateAsyncScope();
            var handler = (ICommandHandler)scope.ServiceProvider.GetRequiredService(info.HandlerInterfaceType);
            await handler.Execute(recipientId.ToString()!, command);
        }
    }

    /// <inheritdoc/>
    public void Dispose() { }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private class HandlerInfo(Type handlerInterfaceType, bool isSingleton)
    {
        public Type HandlerInterfaceType => handlerInterfaceType;
        public bool IsSingleton => isSingleton;
        public ICommandHandler? SingletonHandler;
    }
}
