using KurrentDB.Client;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

/// <summary>Waiting for a KurrentDB node to be usable, and reporting when it never became so.</summary>
public static class StoreHealth
{
    /// <summary>Polls <c>/health/live</c> until it answers 2xx.</summary>
    public static async Task WaitLiveAsync(string connectionString, TimeSpan timeout, ILogger logger,
        CancellationToken ct = default)
    {
        var url = new Uri(StoreHttp.BaseUriOf(connectionString), "health/live");
        var deadline = DateTime.UtcNow + timeout;
        string last = "no response";

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await StoreHttp.GetAsync(connectionString, "health/live", ct)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) { logger.LogInformation("{Url} is live.", url); return; }
                last = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex.Message;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"{url} did not become live within {timeout.TotalSeconds:0}s (last: {last}).");
    }

    /// <summary>Polls until the named projection reports Running.</summary>
    /// <remarks>
    /// The copy engine needs <c>$by_event_type</c> live on the destination, and <c>mp-migrate</c> has always
    /// required it: without it the pre-created join projections have nothing to read and the merge streams
    /// come out empty — a silent, plausible-looking result rather than a failure.
    /// </remarks>
    public static async Task WaitProjectionRunningAsync(KurrentDBProjectionManagementClient projections,
        string name, TimeSpan timeout, ILogger logger, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var s = await projections.GetStatusAsync(name, cancellationToken: ct).ConfigureAwait(false);
                last = s?.Status;
                if (last is not null && last.Contains("Running", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Projection {Name} is {Status}.", name, last);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex.Message;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Projection '{name}' did not reach Running within {timeout.TotalSeconds:0}s (last: {last ?? "unknown"}).");
    }

    /// <summary>Names of the projections currently reporting Faulted.</summary>
    public static async Task<IReadOnlyList<string>> FaultedProjectionsAsync(
        KurrentDBProjectionManagementClient projections, CancellationToken ct = default)
    {
        var faulted = new List<string>();
        await foreach (var p in projections.ListAllAsync(cancellationToken: ct).ConfigureAwait(false))
            if (p.Status?.Contains("Faulted", StringComparison.OrdinalIgnoreCase) == true)
                faulted.Add(p.Name);
        return faulted;
    }
}
