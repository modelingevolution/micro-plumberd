using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
[assembly: InternalsVisibleTo("MicroPlumberd.Tests.App.Dsl")]
[assembly: InternalsVisibleTo("MicroPlumberd.Services.EventAggregator")]
namespace MicroPlumberd.Services;

/// <summary>
/// Background service that starts and manages event handler subscriptions.
/// </summary>
sealed class EventHandlerService(IEnumerable<IEventHandlerStarter> starters) : BackgroundService
{
    /// <summary>
    /// Gets a value indicating whether all event handlers have been started and are ready.
    /// </summary>
    public bool IsReady { get; private set; } = false;

    /// <summary>The starter in progress — named (e.g. <c>EventHandlerStarter&lt;FooModel&gt;</c>) so a startup that never becomes
    /// ready says WHICH handler it is waiting for, not only "not ready".</summary>
    internal string? Starting { get; private set; }

    /// <summary>A failed start: the starter it failed at and its exception — one immutable snapshot.</summary>
    internal sealed record StartFailure(string? Starter, Exception Error);

    private StartFailure? _failure;

    /// <summary>The FIRST failure that stopped the start, if one did (first wins; IsReady then stays false for good).
    /// Read it once into a local: the starter and the exception come from the same snapshot.</summary>
    internal StartFailure? Failure => Volatile.Read(ref _failure);

    /// <summary>
    /// Executes the event handler service, starting all registered event handlers.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token to stop the service.</param>
    /// <returns>A task representing the asynchronous execution.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            foreach (var i in starters)
            {
                Starting = i.GetType().GetFriendlyName();
                await i.Start(stoppingToken);
            }
            Starting = null;
            IsReady = true;
        }
        catch (OperationCanceledException ex)
        {
            // we dont do enything. operation was canceled.
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _failure, new StartFailure(Starting, ex), null);
            throw;
        }
    }
}

/// <summary>
/// Health check that verifies MicroPlumberd event and command handlers are ready.
/// A failed start is DESCRIBED by the starter's name and the exception TYPE only — its message can carry a connection
/// string with credentials. The full exception is attached to the result: a CUSTOM health response writer that serialises
/// <c>HealthReportEntry.Exception.Message</c> re-exposes what the description hides (the default writer prints only the status).
/// </summary>
internal sealed class StartupHealthCheck(EventHandlerService eventHandlers, CommandHandlerService commandHandlers, ILogger<StartupHealthCheck> logger) : IHealthCheck
{
    /// <summary>
    /// Checks the health status of event and command handlers.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous health check operation.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        if (!eventHandlers.IsReady)
        {
            // The text names the starter and the exception TYPE only — never its message: a starter's message can carry a
            // connection string (esdb://user:pass@…, Password=…), and a health description is served to anyone who can
            // reach the endpoint. The full exception stays attached (and in the logs).
            var failure = eventHandlers.Failure;
            return failure != null
                ? HealthCheckResult.Unhealthy($"Event handlers failed to start at {failure.Starter}: {failure.Error.GetType().Name}.", failure.Error)
                : HealthCheckResult.Unhealthy($"Event handlers' projections are not ready{(eventHandlers.Starting is { } s ? $" (starting {s})" : "")}.");
        }
        if(!commandHandlers.IsReady)
            return HealthCheckResult.Unhealthy("Command handlers' projections are not ready.");
        
        return HealthCheckResult.Healthy();
    }
}
/// <summary>
/// Provides extension methods for adding Plumberd health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds MicroPlumberd startup health checks to the health checks builder.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <returns>The health checks builder for method chaining.</returns>
    public static IHealthChecksBuilder AddPlumberdHealthChecks(this IHealthChecksBuilder builder)
    {
        builder.AddTypeActivatedCheck<StartupHealthCheck>("Plumberd Startup Health Check");
            return builder;
    }
}