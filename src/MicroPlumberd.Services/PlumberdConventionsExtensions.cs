namespace MicroPlumberd.Services;

public static class PlumberdConventionsExtensions
{
    public static string GetCommandHandlerOutputStreamName<TCommandHandler>(this IConventions conventions) => 
        conventions.GetExtension<OutputSteamNameFromCommandHandlerExtension>().Extension(typeof(TCommandHandler));
    public static string GetCommandHandlerGroupName<TCommandHandler>(this IConventions conventions) => 
        conventions.GetExtension<GroupNameFromCommandHandlerExtension>().Extension(typeof(TCommandHandler));
    public static void SetCommandHandlerOutputStreamName<TCommandHandler>(this IConventions conventions, OutputSteamNameFromCommandHandler cvt) => conventions.GetExtension<OutputSteamNameFromCommandHandlerExtension>().Extension = cvt;

    public static string GetSteamIdFromCommand<TCommand>(this IConventions conventions, Guid recipientId) =>
        conventions.GetSteamIdFromCommand(typeof(TCommand), recipientId);
    public static string GetSteamIdFromCommand(this IConventions conventions,Type commandType, Guid recipientId) => conventions.GetExtension<SteamNameFromCommandExtension>().Extension(recipientId, commandType);
    public static void SetSteamNameFromCommand(this IConventions conventions, SteamIdFromCommand cvt) => conventions.GetExtension<SteamNameFromCommandExtension>().Extension = cvt;
}