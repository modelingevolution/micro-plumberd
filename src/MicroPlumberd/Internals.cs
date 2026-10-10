using System.Collections.Concurrent;
using KurrentDB.Client;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MicroPlumberd.Tests")]
//[assembly: InternalsVisibleTo("MicroPlumberd.Services")]

/// <summary>
/// Extension methods for <see cref="KurrentDBClientSettings"/> to provide utility functionality.
/// </summary>
public static class KurrentDBClientSettingsExtensions
{
    static readonly HttpClient client = new HttpClient();

    /// <summary>A stream that cannot exist — reading it is the cheapest real gRPC round trip available.</summary>
    private const string ReadinessProbeStream = "$$microplumberd-readiness-probe";

    /// <summary>
    /// Asynchronously waits until the KurrentDB server is ready for what a MicroPlumberd client does first: a gRPC
    /// call. <c>/health/live</c> first, then a real gRPC round trip, on ONE deadline.
    /// </summary>
    /// <remarks>
    /// <c>/health/live</c> alone is the wrong signal: it is HTTP/1.1 and KurrentDB serves it ~500 ms BEFORE it
    /// serves gRPC on the same port (measured 2026-09-07). A caller that trusted it could make its first append
    /// inside that window and meet <c>HTTP_1_1_REQUIRED</c>. Each gRPC attempt uses a NEW client: the client
    /// caches channel discovery, and one opened inside the window would keep its poisoned channel.
    /// </remarks>
    /// <param name="settings">The EventStore client settings containing connection information.</param>
    /// <param name="timeout">The maximum amount of time to wait for the server to become ready.</param>
    /// <param name="delay">The delay between attempts. Defaults to 100 milliseconds if not specified.</param>
    /// <returns>A task that completes when the server answers a gRPC call.</returns>
    /// <exception cref="TimeoutException">Thrown when the server is not ready within <paramref name="timeout"/>;
    /// the message says whether <c>/health/live</c> never answered or gRPC was never served.</exception>
    public static Task WaitUntilReady(this KurrentDBClientSettings settings, TimeSpan timeout, TimeSpan? delay = null) =>
        WaitUntilReady(settings, timeout, delay, s => new KurrentDBClient(s));

    /// <param name="newClient">Builds the client for ONE gRPC attempt (a seam for the fresh-client-per-attempt test).</param>
    internal static async Task WaitUntilReady(KurrentDBClientSettings settings, TimeSpan timeout, TimeSpan? delay,
        Func<KurrentDBClientSettings, KurrentDBClient> newClient)
    {
        var pause = delay ?? TimeSpan.FromMilliseconds(100);
        var deadline = DateTime.UtcNow + timeout;
        var health = new Uri(settings.ConnectivitySettings.Address!, "health/live");
        var last = "no response";
        var live = false;

        while (!live && DateTime.UtcNow < deadline)
        {
            try
            {
                using var cts = new CancellationTokenSource(Remaining(deadline, TimeSpan.FromSeconds(5)));
                using var ret = await client.GetAsync(health, cts.Token);
                if (ret.IsSuccessStatusCode) { live = true; break; }
                last = $"HTTP {(int)ret.StatusCode}";
            }
            catch (Exception ex) { last = ex.Message; }
            await Task.Delay(pause); // after a non-2xx answer too: it used to spin with no delay
        }
        if (!live)
            throw new TimeoutException(
                $"KurrentDB at {health} did not answer /health/live within {timeout.TotalSeconds:0}s (last: {last}). " +
                "Check the docker containers.");

        last = "no attempt";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var probe = newClient(settings);
                using var cts = new CancellationTokenSource(Remaining(deadline, TimeSpan.FromSeconds(5)));
                var read = probe.ReadStreamAsync(Direction.Backwards, ReadinessProbeStream, StreamPosition.End,
                    maxCount: 1, resolveLinkTos: false, cancellationToken: cts.Token);
                _ = await read.ReadState;   // StreamNotFound — the answer does not matter, the round trip does
                return;
            }
            catch (Exception ex) { last = ex.Message.Split('\n')[0]; }
            await Task.Delay(pause);
        }
        throw new TimeoutException(
            $"KurrentDB at {health} answers /health/live but did not serve a gRPC call within {timeout.TotalSeconds:0}s " +
            $"(last: {last}). The health endpoint is HTTP/1.1 and becomes available before gRPC does.");
    }

    private static TimeSpan Remaining(DateTime deadline, TimeSpan cap)
    {
        var left = deadline - DateTime.UtcNow;
        return left <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : left < cap ? left : cap;
    }
}