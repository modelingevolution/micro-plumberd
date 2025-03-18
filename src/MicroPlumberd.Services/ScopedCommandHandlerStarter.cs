using EventStore.Client;

namespace MicroPlumberd.Services;

class CommandHandlerStarter<THandler>(PlumberEngine plumber) : ICommandHandlerStarter
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