using Microsoft.Extensions.Hosting;

namespace MicroPlumberd.Services;

class CommandHandlerService(IEnumerable<ICommandHandlerStarter> starters) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var i in starters)
            await i.Start();
    }
}