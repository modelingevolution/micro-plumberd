using System.Reflection;

namespace MicroPlumberd.Services;

/// <summary>
/// Represents a delegate that returns the output stream name from a command handler type.
/// </summary>
/// <param name="commandHandlerType">The type of the command handler.</param>
/// <returns>The name of the output stream.</returns>
public delegate string OutputSteamNameFromCommandHandler(Type commandHandlerType);
/// <summary>
/// Represents a delegate that returns the group name for a given command handler type.
/// </summary>
/// <param name="commandHandlerType">The type of the command handler.</param>
/// <returns>The group name for the command handler.</returns>
public delegate string GroupNameFromCommandHandler(Type commandHandlerType);
/// <summary>
/// Represents a delegate that returns the name of a command based on its type.
/// </summary>
/// <param name="command">The type of the command.</param>
/// <returns>The name of the command.</returns>
public delegate string CommandName(Type command);
/// <summary>
/// Represents a delegate that returns a collection of command message types.
/// </summary>
/// <param name="command">The command type.</param>
/// <returns>A collection of tuples containing the message name and message type.</returns>
public delegate IEnumerable<(string, Type)> CommandMessageTypes(Type command);
/// <summary>
/// Represents a delegate that returns a string representing the application's command stream.
/// </summary>
/// <returns>A string representing the application command stream.</returns>
public delegate string AppCommandStream();

/// <summary>
/// Represents a delegate that takes a session ID and returns a session stream.
/// </summary>
/// <param name="id">The session ID.</param>
/// <returns>The session stream.</returns>
public delegate string SessionStreamFromSessionId(Guid id);
/// <summary>
/// Convention setting interface for plumberd framework.
/// </summary>
public interface IServicesConvention
{
    /// <summary>
    /// Gets or sets the convention for output stream name from command handler.
    /// </summary>
    OutputSteamNameFromCommandHandler OutputSteamNameFromCommandHandlerConvention { get; set; }
    /// <summary>
    /// Gets or sets the convention for determining the group name for a persisted subscription for a command handler.
    /// </summary>
    GroupNameFromCommandHandler GroupNameFromCommandHandlerConvention { get; set; }
    /// <summary>
    /// Gets or sets the application command's stream convention.
    /// </summary>
    AppCommandStream AppCommandStreamConvention { get; set; }
    /// <summary>
    /// Gets or sets the command message types.
    /// </summary>
    CommandMessageTypes CommandMessageTypes { get; set; }
    /// <summary>
    /// Gets or sets the command name convention.
    /// </summary>
    CommandName CommandNameConvention { get; set; }
    /// <summary>
    /// Gets or sets the convention for session command input stream from session ID.
    /// </summary>
    SessionStreamFromSessionId SessionInStreamFromSessionIdConvention { get; set; }
    /// <summary>
    /// Gets or sets the convention for obtaining the session output stream from the session ID.
    /// </summary>
    SessionStreamFromSessionId SessionOutStreamFromSessionIdConvention { get; set; }
    /// <summary>
    /// Gets or sets a delegate that determines whether handler execution is persistent for manual subscriptions.
    /// Used only for manual subscribing operations like plumberd.SubscribeCommandHandler.
    /// </summary>
    IsHandlerExecutionPersistent IsHandlerExecutionPersistent { get; set; }
    /// <summary>
    /// Gets or sets a function that determines whether the command handlers are executed persistently.
    /// </summary>
    /// <remarks>
    /// The value of this property should be a function that returns a boolean value indicating whether the handlers should be executed persistently.
    /// </remarks>
    Func<bool> AreCommandHandlersExecutedPersistently { get; set; }
    /// <summary>
    /// Gets or sets the filter used to determine whether a command handler should skip a command.
    /// </summary>
    /// <remarks>
    /// The filter is a function that takes a <see cref="Metadata"/> object and an object as parameters,
    /// and returns a boolean value indicating whether the command handler should be skipped.
    /// </remarks>
    Func<Metadata, object, bool> CommandHandlerSkipFilter {get; set;}
}

/// <summary>
/// Represents the configuration for services.
/// </summary>
public interface IServicesConfig
{
    /// <summary>
    /// Gets or sets the default timeout for services.
    /// </summary>
    TimeSpan DefaultTimeout { get; set; } 
}
class ServicesConfig : IServicesConfig
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Represents a delegate that determines whether the execution of a handler is persistent.
/// </summary>
/// <param name="handlerType">The type of the handler.</param>
/// <returns><c>true</c> if the execution of the handler is persistent; otherwise, <c>false</c>.</returns>
public delegate bool IsHandlerExecutionPersistent(Type handlerType);
/// <summary>
/// Default implementation of services conventions for MicroPlumberd.
/// </summary>
class ServicesConvention : IServicesConvention
{
    /// <inheritdoc/>
    public IsHandlerExecutionPersistent IsHandlerExecutionPersistent { get; set; } = static x => false;
    /// <inheritdoc/>
    public Func<bool> AreCommandHandlersExecutedPersistently { get; set; } = () => false;
    /// <inheritdoc/>
    public Func<Metadata, object, bool> CommandHandlerSkipFilter { get; set; } = (m, e) => false;
    /// <inheritdoc/>
    public OutputSteamNameFromCommandHandler OutputSteamNameFromCommandHandlerConvention { get; set; } = static x => $">{x.GetFriendlyName()}";
    /// <inheritdoc/>
    public GroupNameFromCommandHandler GroupNameFromCommandHandlerConvention { get; set; } = static x => $"{x.GetFriendlyName()}";
    /// <inheritdoc/>
    public AppCommandStream AppCommandStreamConvention { get; set; } = static () => $">{AppDomain.CurrentDomain.FriendlyName}";
    /// <inheritdoc/>
    public CommandName CommandNameConvention { get; set; } = static x => $"{x.Name}";

    /// <inheritdoc/>
    public SessionStreamFromSessionId SessionInStreamFromSessionIdConvention { get; set; } = static x => $">SessionIn-{x}";
    /// <inheritdoc/>
    public SessionStreamFromSessionId SessionOutStreamFromSessionIdConvention { get; set; } = static x => $">SessionOut-{x}";
    /// <inheritdoc/>
    public CommandMessageTypes CommandMessageTypes { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServicesConvention"/> class.
    /// </summary>
    public ServicesConvention()
    {
        CommandMessageTypes = x => CommandMappings.Discover(this.CommandNameConvention,x);
    }
}
/// <summary>
/// Provides extension methods for working with conventions on service-layer.
/// </summary>
public static class PlumberdConventionsExtensions
{
    /// <summary>
    /// Retrieves the services conventions from the specified root conventions.
    /// </summary>
    /// <param name="conventions">The conventions to retrieve the services conventions from.</param>
    /// <returns>The services conventions.</returns>
    public static IServicesConvention ServicesConventions(this IConventions conventions) =>
        conventions.GetExtension<ServicesConvention>();

    /// <summary>
    /// Retrieves the services conventions from the specified read-only conventions.
    /// </summary>
    /// <param name="conventions">The read-only conventions to retrieve the services conventions from.</param>
    /// <returns>The services conventions.</returns>
    public static IServicesConvention ServicesConventions(this IReadOnlyConventions conventions) =>
        conventions.GetExtension<ServicesConvention>();

    /// <summary>
    /// Retrieves the services configuration from the specified plumber configuration.
    /// </summary>
    /// <param name="config">The plumber configuration to retrieve the services configuration from.</param>
    /// <returns>The services configuration.</returns>
    public static IServicesConfig ServicesConfig(this IPlumberConfig config) =>
        config.GetExtension<ServicesConfig>();

    /// <summary>
    /// Retrieves the services configuration from the specified read-only plumber configuration.
    /// </summary>
    /// <param name="config">The read-only plumber configuration to retrieve the services configuration from.</param>
    /// <returns>The services configuration.</returns>
    public static IServicesConfig ServicesConfig(this IPlumberReadOnlyConfig config) =>
        config.GetExtension<ServicesConfig>();
}

/// <summary>
/// Provides utility methods for discovering command message type mappings.
/// </summary>
class CommandMappings
{
    /// <summary>
    /// Discovers all message types associated with a command, including execution results and failures.
    /// </summary>
    /// <param name="commandNameConvention">The command naming convention.</param>
    /// <param name="command">The command type.</param>
    /// <returns>A collection of message name and type tuples.</returns>
    public static IEnumerable<(string, Type)> Discover(CommandName commandNameConvention, Type command)
    {
        var cmdType = commandNameConvention(command);

        yield return (cmdType, command);
        yield return ($"{cmdType}Executed", typeof(CommandExecuted));
        yield return ($"{cmdType}Failed", typeof(CommandFailed));
        foreach (var c in command
                     .GetCustomAttributes()
                     .Where(x => x is ThrowsFaultExceptionAttribute)
                     .OfType<ThrowsFaultExceptionAttribute>()
                     .Select(x => x.ThrownType))
            yield return ($"{cmdType}Failed<{c.Name}>", typeof(CommandFailed<>).MakeGenericType(c));
    }
}