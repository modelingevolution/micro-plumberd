// ReSharper disable once CheckNamespace
namespace MicroPlumberd;

public interface ICommandRequest
{
    Guid RecipientId { get; }
    object Command { get; }
}
public interface ICommandRequest<out TCommand> : ICommandRequest
{
    new TCommand Command { get; }
}