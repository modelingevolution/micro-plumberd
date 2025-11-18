using EventStore.Client;

namespace MicroPlumberd.Services;

/// <summary>
/// Starts and configures command handler subscriptions for a specific command handler type.
/// </summary>
/// <typeparam name="THandler">The type of command handler to start.</typeparam>
class CommandHandlerStarter<THandler>(PlumberEngine plumber) : ICommandHandlerStarter
    where THandler : ICommandHandler, IServiceTypeRegister
{
    /// <summary>
    /// Starts the command handler subscription.
    /// </summary>
    /// <returns>A task representing the asynchronous start operation.</returns>
    public async Task Start()
    {
        await plumber.SubscribeCommandHandler<THandler>(Persistently, StreamStartPosition);
    }

    /// <inheritdoc/>
    public IEnumerable<Type> CommandTypes => THandler.CommandTypes;
    /// <inheritdoc/>
    public Type HandlerType => typeof(THandler);
    /// <inheritdoc/>
    public bool Scoped { get; private set; }

    /// <summary>
    /// Configures the command handler with persistence, start position, and scope settings.
    /// </summary>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from.</param>
    /// <param name="scoped">If true, uses scoped handler execution; otherwise, uses singleton.</param>
    /// <returns>This starter instance for method chaining.</returns>
    public ICommandHandlerStarter Configure(bool? persistently, StreamPosition? start, bool scoped)
    {
        this.Persistently = persistently;
        this.StreamStartPosition = start;
        this.Scoped = scoped;
        return this;
    }

    /// <summary>
    /// Gets the configured stream start position.
    /// </summary>
    public StreamPosition? StreamStartPosition { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the subscription is persistent.
    /// </summary>
    public bool? Persistently { get; private set; }
}

/// <summary>
/// Provides extension methods for converting stream positions.
/// </summary>
public static class StreamPositionExtensions
{
    /// <summary>
    /// Converts a FromStream position to a StreamPosition.
    /// </summary>
    /// <param name="fs">The FromStream position to convert.</param>
    /// <returns>The equivalent StreamPosition.</returns>
    public static StreamPosition ToStreamPosition(this FromStream fs)
    {
        StreamPosition sp = StreamPosition.Start;
        if (fs == FromStream.End)
            sp = StreamPosition.End;
        else if (fs != FromStream.Start)
        {
            var i = fs.ToUInt64();
            sp = StreamPosition.FromInt64((long)i);
        }

        return sp;
    }
}

/// <summary>
/// Starts and configures state event handler subscriptions that read from the end of streams.
/// </summary>
/// <typeparam name="THandler">The type of event handler to start.</typeparam>
class EventStateHandlerStarter<THandler>(IPlumber plumber) : IEventHandlerStarter
    where THandler : class, IEventHandler, ITypeRegister
{

    private FromRelativeStreamPosition _relativeStartPosition;

    /// <inheritdoc/>
    public async Task Start(CancellationToken stoppingToken)
    {
        await plumber.SubscribeStateEventHandler<THandler>(start:_relativeStartPosition, token: stoppingToken);
    }

    /// <summary>
    /// Configures the state event handler with a relative stream position.
    /// </summary>
    /// <param name="start">The relative stream position to start reading from.</param>
    /// <returns>This starter instance for method chaining.</returns>
    public EventStateHandlerStarter<THandler> Configure(FromRelativeStreamPosition? start = null)
    {
        this._relativeStartPosition = start ?? FromRelativeStreamPosition.End - 1;
        return this;
    }

}