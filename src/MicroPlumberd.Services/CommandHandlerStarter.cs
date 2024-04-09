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
    public async Task Start()
    {
        if (!Persistently)
            await plumber.SubscribeEventHandler<THandler>(start: StartPosition);
        else
            await plumber.SubscribeEventHandlerPersistently<THandler>(startFrom: StartPosition.ToStreamPosition());
    }

    public EventHandlerStarter<THandler> Configure(bool persistently = false, FromStream? start = null)
    {
        this.Persistently = persistently;
        this.StartPosition = start ?? FromStream.Start;
        return this;
    }
    public FromStream StartPosition { get; private set; }
    public bool Persistently { get; private set; }
}