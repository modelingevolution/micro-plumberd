using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Domain;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Tests.App.Srv;

[CommandHandler]
public partial class StrCommandHandler(IPlumber plumber)
{
    public async Task Handle(string id, CreateStrFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        
        await plumber.AppendState(new StrEntityState() { Name = cmd.Name, Id = id });
    }
}
public record StrEntityState
{
    [JsonIgnore]
    public string Id { get; set; } 
    public string Name { get; set; }
    [JsonIgnore]
    public long Version { get; set; } = -1;
}