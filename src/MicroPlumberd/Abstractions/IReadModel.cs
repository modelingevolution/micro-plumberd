namespace MicroPlumberd;

public interface IReadModel
{
    Task Given(Metadata m, object ev);
}