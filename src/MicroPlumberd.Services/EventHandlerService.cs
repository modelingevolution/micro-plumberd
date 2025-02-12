using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
[assembly: InternalsVisibleTo("MicroPlumberd.Tests.App.Dsl")]
namespace MicroPlumberd.Services;

sealed class EventHandlerService(IEnumerable<IEventHandlerStarter> starters) : BackgroundService
{
    public bool IsReady { get; private set; } = false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            foreach (var i in starters)
                await i.Start(stoppingToken);
            IsReady = true;
        }
        catch (OperationCanceledException ex)
        {
            // we dont do enything. operation was canceled.
        }
        catch (Exception ex)
        {
            throw;
        }
    }
}

internal sealed class StartupHealthCheck(EventHandlerService eventHandlers, CommandHandlerService commandHandlers, ILogger<StartupHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        if (!eventHandlers.IsReady)
            return HealthCheckResult.Unhealthy("Event handlers' projections are not ready.");
        if(!commandHandlers.IsReady)
            return HealthCheckResult.Unhealthy("Command handlers' projections are not ready.");
        
        return HealthCheckResult.Healthy();
    }
}
public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddPlumberdHealthChecks(this IHealthChecksBuilder builder)
    {
        builder.AddTypeActivatedCheck<StartupHealthCheck>("Plumberd Startup Health Check");
            return builder;
    }
}