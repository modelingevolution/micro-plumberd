namespace MicroPlumberd.Services;

/// <summary>
/// Defines a non-generic interface for command handlers that execute commands with a string-based recipient ID.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Executes the specified command for the given recipient ID.
    /// </summary>
    /// <param name="id">The recipient ID as a string.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result value.</returns>
    Task<object?> Execute(string id, object command);
}

/// <summary>
/// Defines a generic interface for command handlers that execute strongly-typed commands with a string-based recipient ID.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler
{
    /// <inheritdoc/>
    Task<object?> ICommandHandler.Execute(string id, object command) => Execute(id, (TCommand)command);

    /// <summary>
    /// Executes the specified command for the given recipient ID.
    /// </summary>
    /// <param name="id">The recipient ID as a string.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result value.</returns>
    Task<object?> Execute(string id, TCommand command);
}

/// <summary>
/// Defines a generic interface for command handlers that execute strongly-typed commands with a strongly-typed recipient ID.
/// </summary>
/// <typeparam name="TId">The type of the recipient ID, which must be parsable.</typeparam>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
public interface ICommandHandler<in TId, in TCommand> : ICommandHandler<TCommand>
where TId:IParsable<TId>
{
    /// <summary>
    /// Executes the specified command for the given recipient ID.
    /// </summary>
    /// <param name="id">The recipient ID of type TId.</param>
    /// <param name="command">The command to execute.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result value.</returns>
    Task<object?> Execute(TId id, TCommand command);

    /// <inheritdoc/>
    Task<object?> ICommandHandler<TCommand>.Execute(string id, TCommand command) => Execute(TId.Parse(id,null), command);
}