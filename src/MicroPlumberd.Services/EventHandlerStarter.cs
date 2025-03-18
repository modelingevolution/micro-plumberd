using EventStore.Client;

namespace MicroPlumberd.Services;

class EventHandlerStarter<THandler>(PlumberEngine plumber) : IEventHandlerStarter
    where THandler : class, IEventHandler, ITypeRegister
{
    private FromStream _startPosition;
    private FromRelativeStreamPosition _relativeStartPosition;
    private bool _persistently;
    public async Task Start(CancellationToken stoppingToken)
    {
        if (!_persistently)
            await plumber.SubscribeEventHandler<THandler>(start: _relativeStartPosition, token: stoppingToken);
        else
            await plumber.SubscribeEventHandlerPersistently<THandler>(startFrom: _startPosition.ToStreamPosition(), token: stoppingToken);
    }

    public EventHandlerStarter<THandler> Configure(bool persistently = false, FromStream? start = null)
    {
        this._persistently = persistently;
        this._startPosition = start ?? FromStream.Start;
        this._relativeStartPosition = _startPosition;
        return this;
    }
    public EventHandlerStarter<THandler> Configure(FromRelativeStreamPosition? start = null)
    {
        this._persistently = false;
        this._relativeStartPosition = start ?? FromRelativeStreamPosition.Start;
        return this;
    }

}