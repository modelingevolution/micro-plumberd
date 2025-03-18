using System.Collections.Concurrent;
using EventStore.Client;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MicroPlumberd.Tests")]
//[assembly: InternalsVisibleTo("MicroPlumberd.Services")]

public static class EventStoreClientSettingsExtensions
{
    static readonly HttpClient client = new HttpClient();
    
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