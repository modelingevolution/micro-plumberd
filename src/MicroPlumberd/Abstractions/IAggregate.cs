namespace MicroPlumberd;

public interface ITypeRegister
{
    static abstract IDictionary<string, Type> TypeRegister { get; }
}
public interface IAggregate
{
    Guid Id { get; }
    long Age { get; }
    IReadOnlyList<object> PendingEvents { get; }
    Task Rehydrate(IAsyncEnumerable<object> events);
    void AckCommitted();
}
public interface IAggregate<out TSelf> : IAggregate
{
    static abstract TSelf New(Guid id);
   
}