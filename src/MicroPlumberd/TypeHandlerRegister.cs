using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace MicroPlumberd;

sealed class TypeHandlerRegisters(EventNameConvention conventions) : ITypeHandlerRegisters
{
    private readonly ConcurrentDictionary<Type, FrozenDictionary<string, Type>> _index = new();

    public IEnumerable<Type> HandlerTypes => _index.Keys;
    public TypeEventConverter GetEventNameConverterFor<T>() where T:ITypeRegister => Get<T>().TryGetValue!;

    private FrozenDictionary<string, Type> Get<T>() where T:ITypeRegister
    {
        var ownerType = typeof(T);
        return _index.GetOrAdd(ownerType, x => T.Types.ToFrozenDictionary(x => conventions(ownerType, x)));
    }
    
    public IEnumerable<KeyValuePair<string, Type>> GetEventNameMappingsFor<T>() where T : ITypeRegister
    {
        return Get<T>();
    }

    public IEnumerable<string> GetEventNamesFor<T>() where T : ITypeRegister => Get<T>().Keys;
}