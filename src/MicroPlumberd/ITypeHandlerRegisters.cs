namespace MicroPlumberd;

public interface ITypeHandlerRegisters
{
    IEnumerable<Type> HandlerTypes { get; }
    TypeEventConverter GetEventNameConverterFor<THandler>() where THandler:ITypeRegister;
    IEnumerable<KeyValuePair<string, Type>> GetEventNameMappingsFor<THandler>() where THandler : ITypeRegister;
    IEnumerable<string> GetEventNamesFor<THandler>() where THandler : ITypeRegister;
}