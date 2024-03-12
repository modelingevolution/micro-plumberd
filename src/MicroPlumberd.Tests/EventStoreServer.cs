using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace MicroPlumberd.Tests;

class EventStoreServer
{
    //private ClusterVNode _node;


    public static async Task<EventStoreServer> Start(int httpPort)
    {
        EventStoreServer s = new EventStoreServer();
        //await s.StartAsync();
        await s.StartInDocker(httpPort);
        return s;
    }
    
    public Uri HttpUrl { get; set; }
    public async Task StartInDocker(int httpPort)
    {
        const string eventStoreHostName = "127.0.0.1";
        
        
        await CheckDns(eventStoreHostName);

        
        HttpUrl = new Uri($"http://{eventStoreHostName}:{httpPort}");
        DockerClient client = new DockerClientConfiguration()
            .CreateClient();

        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters()
        {
            All = true,
            Limit = 10000
        });
        var containerName = $"eventstore-mem-{httpPort}";
        var container = containers.FirstOrDefault(x => x.Names.Any(n => n.Contains(containerName)));
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
                Name = containerName,
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
                    { "2113", default(EmptyStruct)},
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
}