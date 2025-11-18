using System.Collections.Concurrent;
using EventStore.Client;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MicroPlumberd.Tests")]
//[assembly: InternalsVisibleTo("MicroPlumberd.Services")]

/// <summary>
/// Extension methods for <see cref="EventStoreClientSettings"/> to provide utility functionality.
/// </summary>
public static class EventStoreClientSettingsExtensions
{
    static readonly HttpClient client = new HttpClient();

    /// <summary>
    /// Asynchronously waits until the EventStore server is ready to accept connections.
    /// </summary>
    /// <param name="settings">The EventStore client settings containing connection information.</param>
    /// <param name="timeout">The maximum amount of time to wait for the server to become ready.</param>
    /// <param name="delay">The delay between health check attempts. Defaults to 100 milliseconds if not specified.</param>
    /// <returns>A task that completes when the EventStore server is ready.</returns>
    /// <exception cref="TimeoutException">Thrown when the EventStore server does not respond as ready within the specified timeout period.</exception>
    public static async Task WaitUntilReady(this EventStoreClientSettings settings, TimeSpan timeout, TimeSpan? delay = null)
    {

        DateTime until = DateTime.Now.Add(timeout);
        Uri? add = settings.ConnectivitySettings.Address;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        while (DateTime.Now < until)
        {
            try
            {
                var url = new Uri(add, "health/live");
                var ret = await client.GetAsync(url, cts.Token);
                if (ret.IsSuccessStatusCode)
                    return;
            }
            catch { await Task.Delay(delay ?? TimeSpan.FromMilliseconds(100)); }
        }

        throw new TimeoutException("EventStore is not ready, check docker-containers.");
    }
}