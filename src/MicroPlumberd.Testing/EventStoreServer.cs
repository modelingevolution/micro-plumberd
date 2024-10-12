using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using EventStore.Client;
using Xunit;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

    public async Task<bool> Stop()
    {
        var container = await GetEventStoreContainer();
        if (container != null)
        {
            var data = await client.Containers.InspectContainerAsync(container.ID);
            if (data.State.Running)
            {
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
                return true;
            }
        }

        return false;
    }

    public async Task Restart(TimeSpan delay)
    {
        var container = await GetEventStoreContainer();
        if (container != null)
        {
            var data = await client.Containers.InspectContainerAsync(container.ID);
            if (data.State.Running)
            {
                await client.Containers.StopContainerAsync(data.ID, new ContainerStopParameters());
                await Task.Delay(delay);
                await client.Containers.StartContainerAsync(data.ID, new ContainerStartParameters());
                await Task.Delay(10000);
                return;
            }
        }

        throw new InvalidOperationException("No event-store container found");

    }
    public async Task<EventStoreServer> StartInDocker(bool wait = true, bool inMemory=true)
    {
        var container = await GetEventStoreContainer();
        if (!inMemory && container != null)
        {
            var data = await client.Containers.InspectContainerAsync(container.ID);
            await client.Containers.RemoveContainerAsync(data.ID, new ContainerRemoveParameters() { RemoveVolumes = true});
            container = null;
        }
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
                    $"EVENTSTORE_MEM_DB={inMemory.ToString().ToLower()}",
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
                await client.Containers.RestartContainerAsync(data.ID, new ContainerRestartParameters());
            else
                await client.Containers.StartContainerAsync(data.ID, new ContainerStartParameters());
        }

        await this.GetEventStoreSettings().WaitUntilReady(TimeSpan.FromSeconds(30));
        return this;
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

