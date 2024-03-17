using System.Collections.Concurrent;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

class CommandHandlerDispatcher<T>(IPlumber plumber) : IEventHandler, ITypeRegister
    where T:ICommandHandler, IServiceTypeRegister
{
    private readonly ConcurrentDictionary<Type, Type> _cached = new();
    public async Task Handle(Metadata m, object ev)
    {
        var chType = _cached.GetOrAdd(ev.GetType(), x => typeof(ICommandHandler<>).MakeGenericType(ev.GetType()));
        await using var scope = plumber.Config.ServiceProvider.CreateAsyncScope();
        var ch = (ICommandHandler)scope.ServiceProvider.GetRequiredService(chType);
        await ch.Execute(m.RecipientId(), ev);
    }

    
    public static IServiceCollection RegisterHandlers(IServiceCollection services)
    {
        return T.RegisterHandlers(services);
    }
    // TODO: Conventions are not consistent.
    private static Dictionary<string, Type> _typeRegister = T.CommandTypes.ToDictionary(x => x.Name);
    public static IReadOnlyDictionary<string, Type> TypeRegister => _typeRegister;
}