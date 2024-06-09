using EventStore.Client;

namespace MicroPlumberd.Services;

class CommandHandlerStarter<THandler>(IPlumber plumber) : ICommandHandlerStarter
    where THandler : ICommandHandler, IServiceTypeRegister
{
    public async Task Start()
    {
        await plumber.SubscribeCommandHandler<THandler>(Persistently, StreamStartPosition);
    }

    public IEnumerable<Type> CommandTypes => THandler.CommandTypes;
    public Type HandlerType => typeof(THandler);

    public ICommandHandlerStarter Configure(bool? persistently, StreamPosition? start)
    {
        this.Persistently = persistently;
        this.StreamStartPosition = start;
        return this;
    }

    public StreamPosition? StreamStartPosition { get; private set; }

    public bool? Persistently { get; private set; }
}

public static class StreamPositionExtensions
{
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
class EventHandlerStarter<THandler>(IPlumber plumber) : IEventHandlerStarter
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
class EventStateHandlerStarter<THandler>(IPlumber plumber) : IEventHandlerStarter
    where THandler : class, IEventHandler, ITypeRegister
{
    
    private FromRelativeStreamPosition _relativeStartPosition;
    
    public async Task Start(CancellationToken stoppingToken)
    {
        await plumber.SubscribeStateEventHandler<THandler>(start:_relativeStartPosition, token: stoppingToken);
    }

   
    public EventStateHandlerStarter<THandler> Configure(FromRelativeStreamPosition? start = null)
    {
        this._relativeStartPosition = start ?? FromRelativeStreamPosition.End - 1;
        return this;
    }

}