// Holds one EventStoreServer for <hold> seconds, starting it at <startAtUtcTicks> (a barrier shared with a sibling process),
// and checks every 500 ms that the store is still THIS process's: its own container (by the id seen right after start) is
// running, and its own marker event is still readable (a restart of an in-memory store wipes it).
// Prints one line: "PORT=<p> CONTAINER=<id> RESULT=SURVIVED" or "... RESULT=LOST:<why>"; exit 0 only when it survived.
using Docker.DotNet;
using Docker.DotNet.Models;
using KurrentDB.Client;
using MicroPlumberd.Testing;

var startAt = new DateTime(long.Parse(args[0]), DateTimeKind.Utc);
var hold = TimeSpan.FromSeconds(int.Parse(args[1]));
var delay = startAt - DateTime.UtcNow;
if (delay > TimeSpan.Zero) await Task.Delay(delay);

using var docker = new DockerClientConfiguration().CreateClient();
string? id = null;
string result;
var server = new EventStoreServer();
try
{
    await server.StartInDocker();
    id = (await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }))
        .FirstOrDefault(c => c.Names.Contains("/" + server.ContainerName))?.ID;
    var client = new KurrentDBClient(server.GetEventStoreSettings());
    var mine = $"holder-{Environment.ProcessId}";
    await client.AppendToStreamAsync(mine, StreamState.Any,
        [new EventData(Uuid.NewUuid(), "Marker", "{}"u8.ToArray())]);

    result = "SURVIVED";
    var until = DateTime.UtcNow + hold;
    while (DateTime.UtcNow < until)
    {
        await Task.Delay(500);
        var running = id != null && (await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }))
            .Any(c => c.ID == id && c.State == "running");
        if (!running) { result = "LOST:my-container-is-gone-or-stopped"; break; }
        try
        {
            var read = client.ReadStreamAsync(Direction.Forwards, mine, StreamPosition.Start);
            if (await read.ReadState == ReadState.StreamNotFound) { result = "LOST:my-marker-is-gone(store-restarted)"; break; }
        }
        catch (Exception e) { result = $"LOST:store-unreachable({e.GetType().Name})"; break; }
    }
}
catch (Exception e)
{
    result = $"LOST:start-failed({e.GetType().Name}: {e.Message.Split('\n')[0]})";
}
finally
{
    await server.DisposeAsync();
}

Console.WriteLine($"PORT={server.HttpPort} CONTAINER={id?[..12] ?? "none"} RESULT={result}");
return result == "SURVIVED" ? 0 : 1;
