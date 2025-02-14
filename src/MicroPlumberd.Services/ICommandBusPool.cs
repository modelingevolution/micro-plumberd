namespace MicroPlumberd.Services;

/// <summary>
/// Represents a pool of command buses that can be rented and returned for managing command execution.
/// </summary>
public interface ICommandBusPool
{
    /// <summary>
    /// Rents an <see cref="ICommandBusOwner"/> instance from the pool, allowing the caller to send commands
    /// using the rented command bus. The rented instance must be returned to the pool after use by calling dispose
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for a command bus to become available.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation. The result contains
    /// an <see cref="ICommandBusOwner"/> instance that can be used to send commands.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the pool is empty and no command bus is available for rent.
    /// </exception>
    ValueTask<ICommandBusOwner> RentScope(CancellationToken ct = default);
}