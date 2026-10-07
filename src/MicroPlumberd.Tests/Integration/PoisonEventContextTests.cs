using System.Text;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// A stored event that cannot be deserialized reaches the error-handle policy with the context naming
/// WHICH event it is: its own stream, its type and its number — set before deserialization, because
/// nothing else the policy receives names it (the subscription's stream is the handler's output stream).
/// </summary>
[TestCategory("Integration")]
public class PoisonEventContextTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore = new();

    [Fact]
    public async Task APolicySeesTheSourceStreamTypeAndNumberOfAnEventThatCannotBeRead()
    {
        await _eventStore.StartInDocker();

        var seen = new List<(string? Stream, string? Type, long? Number, string? Subscription)>();
        var engine = PlumberEngine.Create(_eventStore.GetEventStoreSettings(), config =>
            config.SetErrorHandlePolicy((ex, context, ct) =>
            {
                lock (seen)
                    seen.Add((
                        context.TryGetValue<string>(OperationContextProperty.SourceStreamId, out var s) ? s : null,
                        context.TryGetValue<string>(OperationContextProperty.EventType, out var t) ? t : null,
                        context.TryGetValue<long>(OperationContextProperty.SourceEventNumber, out var n) ? n : null,
                        context.TryGetValue<string>(OperationContextProperty.StreamName, out var sub) ? sub : null));
                return Task.FromResult(ErrorHandleDecision.Ignore);
            }));

        var stream = $"PoisonFoo-{Guid.NewGuid():N}";
        await using (var client = new KurrentDBClient(_eventStore.GetEventStoreSettings()))
            await client.AppendToStreamAsync(stream, StreamState.NoStream,
            [
                Raw("""{"Name":"good-1"}"""),
                // Valid JSON that cannot become a FooCreated: a number where a string is declared.
                Raw("""{"Name":123}"""),
                Raw("""{"Name":"good-2"}""")
            ]);

        var model = new CaughtUpFooModel(new InMemoryAssertionDb());
        await engine.SubscribeEventHandler(model);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && model.AssertionDb.Index.Count < 2) await Task.Delay(200);

        model.AssertionDb.Index.Should().HaveCount(2, "the policy ignored the poison and the good events around it were handled");

        lock (seen)
        {
            var poison = seen.Should().ContainSingle().Subject;
            poison.Stream.Should().Be(stream, "the event's OWN stream, not the subscription's");
            poison.Type.Should().Be(nameof(FooCreated));
            poison.Number.Should().Be(1, "the poison is the second event of its stream");
            poison.Subscription.Should().NotBeNullOrEmpty("StreamName is still set — the new keys sit beside it, not over it")
                .And.NotBe(stream, "StreamName stays the subscription's stream");
        }
    }

    private static EventData Raw(string json) =>
        new(Uuid.NewUuid(), nameof(FooCreated), Encoding.UTF8.GetBytes(json));

    public async ValueTask DisposeAsync() => await _eventStore.DisposeAsync();

    public void Dispose() => _eventStore.Dispose();
}
