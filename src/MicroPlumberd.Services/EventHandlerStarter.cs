using EventStore.Client;

namespace MicroPlumberd.Services;

/// <summary>
/// Starts and configures event handler subscriptions for a specific event handler type.
/// </summary>
/// <typeparam name="THandler">The type of event handler to start.</typeparam>
class EventHandlerStarter<THandler>(PlumberEngine plumber) : IEventHandlerStarter
    where THandler : class, IEventHandler, ITypeRegister
{
    private FromStream _startPosition;
    private FromRelativeStreamPosition _relativeStartPosition;
    private bool _persistently;
    /// <summary>
    /// Starts the event handler subscription with the configured settings.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token to stop the subscription.</param>
    /// <returns>A task representing the asynchronous start operation.</returns>
    public async Task Start(CancellationToken stoppingToken)
    {
        try
        {
            if (!_persistently)
                await plumber.SubscribeEventHandler<THandler>(start: _relativeStartPosition, token: stoppingToken);
            else
                await plumber.SubscribeEventHandlerPersistently<THandler>(startFrom: _startPosition.ToStreamPosition(), token: stoppingToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start event handler subscription for '{typeof(THandler).Name}'. See inner exception for details.", ex);
        }
    }

    /// <summary>
    /// Configures the event handler with persistence and start position settings.
    /// </summary>
    /// <param name="persistently">If true, uses persistent subscriptions; otherwise, uses catch-up subscriptions.</param>
    /// <param name="start">The stream position to start reading from.</param>
    /// <returns>This starter instance for method chaining.</returns>
    public EventHandlerStarter<THandler> Configure(bool persistently = false, FromStream? start = null)
    {
        this._persistently = persistently;
        this._startPosition = start ?? FromStream.Start;
        this._relativeStartPosition = _startPosition;
        return this;
    }
    /// <summary>
    /// Configures the event handler with a relative stream position.
    /// </summary>
    /// <param name="start">The relative stream position to start reading from.</param>
    /// <returns>This starter instance for method chaining.</returns>
    public EventHandlerStarter<THandler> Configure(FromRelativeStreamPosition? start = null)
    {
        this._persistently = false;
        this._relativeStartPosition = start ?? FromRelativeStreamPosition.Start;
        return this;
    }

}