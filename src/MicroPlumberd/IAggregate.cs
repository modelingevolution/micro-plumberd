namespace MicroPlumberd;

public interface IAggregate<out TSelf>
{
    static abstract IDictionary<string, Type> TypeRegister { get; }
    static abstract TSelf New(Guid id);
    Guid Id { get; }
    long Age { get; }
    IReadOnlyList<object> PendingEvents { get; }
    Task Rehydrate(IAsyncEnumerable<object> events);
}