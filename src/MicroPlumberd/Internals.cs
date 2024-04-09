using EventStore.Client;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MicroPlumberd.Tests")]
//[assembly: InternalsVisibleTo("MicroPlumberd.Services")]

public static class EventStoreClientSettingsExtensions
{
    public static async Task WaitUntilReady(this EventStoreClientSettings settings, TimeSpan timeout)
    {
        using var c = new HttpClient();
        DateTime until = DateTime.Now.Add(timeout);
        c.BaseAddress = settings.ConnectivitySettings.Address;
        c.Timeout = TimeSpan.FromMilliseconds(100);
        while (DateTime.Now < until)
        {
            try
            {
                var ret = await c.GetAsync("health/live");
                if (ret.IsSuccessStatusCode)
                    return;
            }
            catch { await Task.Delay(100); }
        }

        throw new TimeoutException("EventStore is not ready, check docker-containers.");
    }
}