using System.Reflection;

namespace MicroPlumberd.Services;

public delegate string OutputSteamNameFromCommandHandler(Type commandHandlerType);
public delegate string GroupNameFromCommandHandler(Type commandHandlerType);
public delegate string CommandName(Type command);
public delegate IEnumerable<(string, Type)> CommandMessageTypes(Type command);
public delegate string AppCommandStream();

public delegate string SessionStreamFromSessionId(Guid id);
public interface IServicesConvention
{
    OutputSteamNameFromCommandHandler OutputSteamNameFromCommandHandlerConvention { get; set; }
    GroupNameFromCommandHandler GroupNameFromCommandHandlerConvention { get; set; }
    AppCommandStream AppCommandStreamConvention { get; set; }
    CommandMessageTypes CommandMessageTypes { get; set; }
    CommandName CommandNameConvention { get; set; }
    SessionStreamFromSessionId SessionInStreamFromSessionIdConvention { get; set; }
    SessionStreamFromSessionId SessionOutStreamFromSessionIdConvention { get; set; }
    /// <summary>
    /// Used only for manual subscribing: plumberd.SubscribeCommandHandler<THandler>()
    /// </summary>
    IsHandlerExecutionPersistent IsHandlerExecutionPersistent { get; set; }
    Func<bool> AreHandlersExecutedPersistently { get; set; }
    Func<Metadata, object, bool> CommandHandlerSkipFilter {get; set;}
}

public interface IServicesConfig
{
    TimeSpan DefaultTimeout { get; set; } 
}
class ServicesConfig : IServicesConfig
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

public delegate bool IsHandlerExecutionPersistent(Type handlerType);
class ServicesConvention : IServicesConvention
{
    public IsHandlerExecutionPersistent IsHandlerExecutionPersistent { get; set; } = static x => false;
    public Func<bool> AreHandlersExecutedPersistently { get; set; } = () => false;
    public Func<Metadata, object, bool> CommandHandlerSkipFilter { get; set; } = (m, e) => false;
    public OutputSteamNameFromCommandHandler OutputSteamNameFromCommandHandlerConvention { get; set; } = static x => $">{x.GetFriendlyName()}";
    public GroupNameFromCommandHandler GroupNameFromCommandHandlerConvention { get; set; } = static x => $"{x.GetFriendlyName()}";
    public AppCommandStream AppCommandStreamConvention { get; set; } = static () => $">{AppDomain.CurrentDomain.FriendlyName}";
    public CommandName CommandNameConvention { get; set; } = static x => $"{x.Name}";
    
    public SessionStreamFromSessionId SessionInStreamFromSessionIdConvention { get; set; } = static x => $">SessionIn-{x}";
    public SessionStreamFromSessionId SessionOutStreamFromSessionIdConvention { get; set; } = static x => $">SessionOut-{x}";
    public CommandMessageTypes CommandMessageTypes { get; set; }

    public ServicesConvention()
    {
        CommandMessageTypes = x => CommandMappings.Discover(this.CommandNameConvention,x);
    }
}
public static class PlumberdConventionsExtensions
{
    public static IServicesConvention ServicesConventions(this IConventions conventions) =>
        conventions.GetExtension<ServicesConvention>();
    public static IServicesConfig ServicesConfig(this IPlumberConfig config) =>
        config.GetExtension<ServicesConfig>();
}

class CommandMappings
{
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