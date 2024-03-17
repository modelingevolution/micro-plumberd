namespace MicroPlumberd.Services;

record SteamNameFromCommandExtension
{
    public SteamIdFromCommand Extension { get; set; } = (r, c) => $">Cmd-{r}";
}