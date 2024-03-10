namespace MicroPlumberd;

public interface IReadModel<TSelf>
{
    static abstract IDictionary<string, Type> TypeRegister { get; }
    Task Given(Metadata m, object ev);
}