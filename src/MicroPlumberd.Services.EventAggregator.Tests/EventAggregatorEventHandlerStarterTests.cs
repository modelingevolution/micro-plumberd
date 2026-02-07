using FluentAssertions;
using MicroPlumberd;
using MicroPlumberd.Services.EventAggregator;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.EventAggregator;

namespace MicroPlumberd.Services.EventAggregator.Tests;

public record TestEvent(string Message);
public record AnotherEvent(int Value);

[OutputStream("TestHandler")]
public class TestHandler : IEventHandler, ITypeRegister
{
    public List<(Metadata Metadata, object Event)> ReceivedEvents { get; } = new();

    public Task Handle(Metadata m, object ev)
    {
        ReceivedEvents.Add((m, ev));
        return Task.CompletedTask;
    }

    public static IEnumerable<Type> Types { get; } = [typeof(TestEvent), typeof(AnotherEvent)];
}

public class EventAggregatorEventHandlerStarterTests
{
    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(_ => PlumberEngine.Create(configure: cfg =>
        {
            // no-op, just need conventions
        }));
        services.AddScoped(_ => OperationContext.Create(Flow.Component));

        // EventAggregator infrastructure
        services.AddSingleton<EventAggregatorPool>();
        services.AddSingleton<IEventAggregatorPool>(sp => sp.GetRequiredService<EventAggregatorPool>());
        services.AddSingleton<IEventAggregatorForwarder, NullForwarder>();
        services.AddScoped<IEventAggregator, ModelingEvolution.EventAggregator.EventAggregator>();

        // Handler as singleton so we can observe it across scopes
        services.AddSingleton<TestHandler>();

        // Starter registration (uses the public extension method)
        services.AddEventHandlerWithEventAggregatorSource<TestHandler, Guid>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handler_receives_event_with_correct_metadata()
    {
        await using var sp = BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var pool = sp.GetRequiredService<IEventAggregatorPool>();
        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("hello");
        await pool.Broadcast(recipientId, evt);

        await Task.Delay(200);

        var handler = sp.GetRequiredService<TestHandler>();
        handler.ReceivedEvents.Should().HaveCount(1);
        var (metadata, receivedEvent) = handler.ReceivedEvents[0];
        receivedEvent.Should().Be(evt);

        // StreamId<Guid>() should correctly parse the recipientId from SourceStreamId
        metadata.StreamId<Guid>().Should().Be(recipientId);
        metadata.SourceStreamId.Should().Contain(recipientId.ToString());
    }

    [Fact]
    public async Task Handler_receives_multiple_event_types()
    {
        await using var sp = BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var pool = sp.GetRequiredService<IEventAggregatorPool>();
        var id = Guid.NewGuid();
        await pool.Broadcast(id, new TestEvent("first"));
        await pool.Broadcast(id, new AnotherEvent(42));

        await Task.Delay(200);

        var handler = sp.GetRequiredService<TestHandler>();
        handler.ReceivedEvents.Should().HaveCount(2);
        handler.ReceivedEvents[0].Event.Should().BeOfType<TestEvent>();
        handler.ReceivedEvents[1].Event.Should().BeOfType<AnotherEvent>();
    }

    [Fact]
    public async Task SourceStreamId_uses_event_convention_not_handler()
    {
        await using var sp = BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var pool = sp.GetRequiredService<IEventAggregatorPool>();
        var recipientId = Guid.NewGuid();
        await pool.Broadcast(recipientId, new TestEvent("test"));

        await Task.Delay(200);

        var handler = sp.GetRequiredService<TestHandler>();
        handler.ReceivedEvents.Should().HaveCount(1);
        var sourceStreamId = handler.ReceivedEvents[0].Metadata.SourceStreamId;
        // TestEvent has no [OutputStream], so StreamNameFromEventConvention uses namespace
        // The category should NOT be "TestHandler" (that's the handler's convention)
        sourceStreamId.Should().NotStartWith("TestHandler",
            "category should come from event convention, not handler");
        sourceStreamId.Should().Contain(recipientId.ToString());
    }

    [Fact]
    public async Task AddEventHandlerWithEventAggregatorSource_registers_all_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => PlumberEngine.Create());
        services.AddScoped(_ => OperationContext.Create(Flow.Component));
        services.AddSingleton<EventAggregatorPool>();
        services.AddSingleton<IEventAggregatorPool>(sp => sp.GetRequiredService<EventAggregatorPool>());
        services.AddSingleton<IEventAggregatorForwarder, NullForwarder>();
        services.AddScoped<IEventAggregator, ModelingEvolution.EventAggregator.EventAggregator>();

        services.AddEventHandlerWithEventAggregatorSource<TestHandler, Guid>();

        await using var sp = services.BuildServiceProvider();

        // Starter should be resolvable
        var starter = sp.GetService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        starter.Should().NotBeNull();

        // Handler should be resolvable from scope
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetService<TestHandler>();
        handler.Should().NotBeNull();
    }
}
