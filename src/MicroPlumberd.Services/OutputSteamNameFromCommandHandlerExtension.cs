namespace MicroPlumberd.Services;

record OutputSteamNameFromCommandHandlerExtension
{
    public OutputSteamNameFromCommandHandler Extension { get; set; } = x => $">{x.GetFriendlyName()}";
}
record GroupNameFromCommandHandlerExtension
{
    public GroupNameFromCommandHandler Extension { get; set; } = x => $"{x.GetFriendlyName()}";
}