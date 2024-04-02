using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using EventStore.Client;
using Xunit;

namespace MicroPlumberd.Testing;

public class EventStoreServer :  IDisposable
{
    public EventStoreClientSettings GetEventStoreSettings() => EventStoreClientSettings.Create(HttpUrl.ToString());

    public Uri HttpUrl { get; }
    private readonly int httpPort;
    private static PortSearcher _searcher = new PortSearcher();
    private readonly DockerClient client;
    private readonly bool _isDebuggerAttached = false;
    public static EventStoreServer Create(string? containerName = null) => new EventStoreServer(containerName);
    public EventStoreServer() : this(null) {}
    internal EventStoreServer(string? containerName = null)
    {
        if (containerName != null)
            _containerName = containerName;
        httpPort = _searcher.FindNextAvailablePort();
        const string eventStoreHostName = "localhost";
        //await CheckDns(eventStoreHostName);
        _isDebuggerAttached = Debugger.IsAttached;
        HttpUrl = new Uri($"esdb://admin:changeit@{eventStoreHostName}:{httpPort}?tls=false&tlsVerifyCert=false");
        client = new DockerClientConfiguration()
            .CreateClient();
    }

    private string? _containerName;
    public string ContainerName => _containerName ?? $"eventstore-mem-{httpPort}";
    public int HttpPort => httpPort;

    private async Task<ContainerListResponse?> GetEventStoreContainer()
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters()
        {
            All = true,
            Limit = 10000
        });
        var container = containers.FirstOrDefault(x => x.Names.Any(n => n.Contains(ContainerName)));
        return container;
    }
    public async Task<EventStoreServer> StartInDocker(bool wait = true)
    {
        var container = await GetEventStoreContainer();
        if (container == null)
        {
            var response = await client.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = "eventstore/eventstore:latest",
                Env = new List<string>()
                {
                    "EVENTSTORE_RUN_PROJECTIONS=All",
                    "EVENTSTORE_START_STANDARD_PROJECTIONS=true",
                    "EVENTSTORE_INSECURE=true",
                    "EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP=true",
                    "EVENTSTORE_MEM_DB=true",
                    //"EVENTSTORE_CERTIFICATE_PASSWORD=ca",
                    //"EVENTSTORE_CERTIFICATE_FILE=/cert/eventstore.p12",
                    //"EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH=/cert/ca-certificates/"
                },
                Name = ContainerName,
                HostConfig = new HostConfig()
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>()
                    {
                        { $"2113", new List<PortBinding>() { new PortBinding() { HostPort = $"{httpPort}", HostIP = "0.0.0.0" } }}
                    }

                },
                Volumes = new Dictionary<string, EmptyStruct>() { },
                ExposedPorts = new Dictionary<string, EmptyStruct>()
                {
                    { "2113", default},
                }
            });
            await client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());
        }
        else
        {
            var data = await client.Containers.InspectContainerAsync(container.ID);
            if (data.State.Running)
            {
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
            }

            await client.Containers.StartContainerAsync(data.ID, new ContainerStartParameters());
        }

        await WaitUntilReady(TimeSpan.FromSeconds(100));
        return this;
    }

    public async Task WaitUntilReady(TimeSpan timeout)
    {
        using var c = new HttpClient();
        DateTime until = DateTime.Now.Add(timeout);
        c.BaseAddress = new Uri($"http://localhost:{HttpPort}");
        c.Timeout = TimeSpan.FromMilliseconds(100);
        while (DateTime.Now < until)
        {
            try
            {
                var ret = await c.GetAsync("health/live");
                if (ret.IsSuccessStatusCode)
                    return;
            }
            catch{}
        }

        throw new TimeoutException("EventStore is not ready, check docker-containers.");
    }
    private static async Task CheckDns(string eventStoreHostName)
    {
        try
        {
            var result = await Dns.GetHostEntryAsync(eventStoreHostName);
            foreach (var i in result.AddressList)
            {
                if (i.Equals(IPAddress.Loopback))
                    return;
            }
        }
        catch { }

        throw new Exception(
            $"To run tests put {eventStoreHostName} to your etc/hosts and modellution's ca certificate to trusted certificate store.");

    }

    async ValueTask Cleanup()
    {
        var container = await GetEventStoreContainer();
        if (container != null)
        {
            var data = await client.Containers.InspectContainerAsync(container.ID);
            if (data.State.Running) 
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
            await client.Containers.RemoveContainerAsync(data.ID, new ContainerRemoveParameters() { Force = true});
        }
    }

    public void Dispose()
    {
        Task.Run(Cleanup).Wait();
    }
}