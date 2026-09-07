using KurrentDB.Client;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Rewrite;

/// <summary>Waiting for a KurrentDB node to be usable, and reporting when it never became so.</summary>
public static class StoreHealth
{
    /// <summary>A stream that cannot exist — reading it is the cheapest real gRPC round trip available.</summary>
    private const string ProbeStream = "$$mp-rewrite-readiness-probe";

    /// <summary>
    /// Waits until the store is ready for the kind of call this tool actually makes: first <c>/health/live</c>,
    /// then a real gRPC round trip.
    /// </summary>
    /// <remarks>
    /// <para><b>`/health/live` alone is the wrong signal.</b> It is served over HTTP/1.1 and becomes available
    /// BEFORE KurrentDB will serve gRPC on the same port — measured at <b>528 ms</b> of daylight on a freshly
    /// started container (saturn, 2026-09-07). Every client this tool opens is gRPC, so a caller that trusted
    /// the health check alone could open one inside that window and be answered with an HTTP/2 GOAWAY carrying
    /// <c>HTTP_1_1_REQUIRED</c>, surfacing as <c>RpcException(Internal)</c>. The client's own retry usually
    /// hides it; on a loaded host it does not, and it took out a scenario's fixture on the 81-test run.</para>
    /// <para>This is the single place every client in the tool and the suite waits, so the probe belongs here
    /// rather than in each caller.</para>
    /// </remarks>
    public static async Task WaitLiveAsync(string connectionString, TimeSpan timeout, ILogger logger,
        CancellationToken ct = default)
    {
        var url = new Uri(StoreHttp.BaseUriOf(connectionString), "health/live");
        var deadline = DateTime.UtcNow + timeout;
        var last = "no response";
        var httpLive = false;

        while (!httpLive && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await StoreHttp.GetAsync(connectionString, "health/live", ct)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    logger.LogInformation("{Url} is live.", url);
                    httpLive = true;
                    break;
                }
                last = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex.Message;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        if (!httpLive)
            throw new TimeoutException(
                $"{url} did not become live within {timeout.TotalSeconds:0}s (last: {last}).");

        await WaitGrpcServableAsync(connectionString, deadline, timeout, logger, ct).ConfigureAwait(false);
    }

    /// <summary>Polls a trivial read until the server actually serves gRPC, or the shared deadline passes.</summary>
    private static async Task WaitGrpcServableAsync(string connectionString, DateTime deadline, TimeSpan timeout,
        ILogger logger, CancellationToken ct)
    {
        var last = "no attempt";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A fresh client per attempt: the client caches channel discovery, and a channel poisoned
                // during the window would otherwise be reused for the rest of the run.
                await using var probe = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));
                var read = probe.ReadStreamAsync(Direction.Backwards, ProbeStream, StreamPosition.End,
                    maxCount: 1, resolveLinkTos: false, cancellationToken: ct);
                _ = await read.ReadState.ConfigureAwait(false);   // StreamNotFound — the answer does not matter
                logger.LogInformation("{Store} is serving gRPC.", StoreHttp.BaseUriOf(connectionString));
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex.Message.Split('\n')[0];
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"{StoreHttp.BaseUriOf(connectionString)} answers /health/live but did not serve a gRPC call "
            + $"within {timeout.TotalSeconds:0}s (last: {last}). The health endpoint is HTTP/1.1 and becomes "
            + "available before the gRPC endpoint does.");
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
