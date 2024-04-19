using System.Diagnostics;
using System.Text;
using EventStore.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services;

sealed class EventHandlerService(IEnumerable<IEventHandlerStarter> starters) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var i in starters) 
            await i.Start(stoppingToken);
    }

}