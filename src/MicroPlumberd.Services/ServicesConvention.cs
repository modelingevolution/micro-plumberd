﻿using System.Reflection;

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
    SessionStreamFromSessionId SessionStreamFromSessionIdConvention { get; set; }
}

public interface IServicesConfig
{
    TimeSpan DefaultTimeout { get; set; } 
}
class ServicesConfig : IServicesConfig
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
class ServicesConvention : IServicesConvention
{
    public OutputSteamNameFromCommandHandler OutputSteamNameFromCommandHandlerConvention { get; set; } = static x => $">{x.GetFriendlyName()}";
    public GroupNameFromCommandHandler GroupNameFromCommandHandlerConvention { get; set; } = static x => $"{x.GetFriendlyName()}";
    public AppCommandStream AppCommandStreamConvention { get; set; } = static () => $">{AppDomain.CurrentDomain.FriendlyName}";
    public CommandName CommandNameConvention { get; set; } = static x => $"{x.Name}";
    public SessionStreamFromSessionId SessionStreamFromSessionIdConvention { get; set; } = static x => $">Session-{x}";
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
                     .Where(x => x is ThrowsFaultCommandExceptionAttribute)
                     .OfType<ThrowsFaultCommandExceptionAttribute>()
                     .Select(x => x.ThrownType))
            yield return ($"{cmdType}Failed<{c.Name}>", typeof(CommandExecuted<>).MakeGenericType(c));
    }
}