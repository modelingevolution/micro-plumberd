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

        // Propagation for both event types (broadcast so pool.Broadcast delivers too)
        services.AddEventAggregatorPropagation<TestEvent, Guid>(broadcast: true);
        services.AddEventAggregatorPropagation<AnotherEvent, Guid>(broadcast: true);

        // Handler as singleton so we can observe it across scopes
        services.AddSingleton<TestHandler>();
        services.AddScopedEventAggregatorHandler<TestHandler, Guid>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handler_receives_event_with_correct_metadata()
    {
        await using var sp = BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var propagation = sp.GetRequiredService<EventAggregatorPropagation>();
        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("hello");
        var context = OperationContext.Create(Flow.Component);
        await propagation.OnEventAppendingAsync(context, evt, recipientId, null);

        await Task.Delay(200);

        var handler = sp.GetRequiredService<TestHandler>();
        handler.ReceivedEvents.Should().HaveCount(1);
        var (metadata, receivedEvent) = handler.ReceivedEvents[0];
        receivedEvent.Should().Be(evt);

        metadata.StreamId<Guid>().Should().Be(recipientId);
        metadata.SourceStreamId.Should().Contain(recipientId.ToString());
    }

    [Fact]
    public async Task Handler_receives_multiple_event_types()
    {
        await using var sp = BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var propagation = sp.GetRequiredService<EventAggregatorPropagation>();
        var id = Guid.NewGuid();
        var context = OperationContext.Create(Flow.Component);
        await propagation.OnEventAppendingAsync(context, new TestEvent("first"), id, null);
        await propagation.OnEventAppendingAsync(context, new AnotherEvent(42), id, null);

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

        var propagation = sp.GetRequiredService<EventAggregatorPropagation>();
        var recipientId = Guid.NewGuid();
        var context = OperationContext.Create(Flow.Component);
        await propagation.OnEventAppendingAsync(context, new TestEvent("test"), recipientId, null);

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
    public async Task AddScopedEventAggregatorHandler_registers_all_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => PlumberEngine.Create());
        services.AddScoped(_ => OperationContext.Create(Flow.Component));
        services.AddSingleton<EventAggregatorPool>();
        services.AddSingleton<IEventAggregatorPool>(sp => sp.GetRequiredService<EventAggregatorPool>());

        services.AddEventAggregatorPropagation<TestEvent, Guid>();
        services.AddScopedEventAggregatorHandler<TestHandler, Guid>();

        await using var sp = services.BuildServiceProvider();

        var starter = sp.GetService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        starter.Should().NotBeNull();

        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetService<TestHandler>();
        handler.Should().NotBeNull();
    }

    [Fact]
    public async Task AddSingletonEventAggregatorHandler_registers_all_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => PlumberEngine.Create());
        services.AddScoped(_ => OperationContext.Create(Flow.Component));
        services.AddSingleton<EventAggregatorPool>();
        services.AddSingleton<IEventAggregatorPool>(sp => sp.GetRequiredService<EventAggregatorPool>());

        services.AddEventAggregatorPropagation<TestEvent, Guid>();
        services.AddSingletonEventAggregatorHandler<TestHandler, Guid>();

        await using var sp = services.BuildServiceProvider();

        var starter = sp.GetService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        starter.Should().NotBeNull();

        var handler = sp.GetService<TestHandler>();
        handler.Should().NotBeNull();

        var handler2 = sp.GetService<TestHandler>();
        handler2.Should().BeSameAs(handler);
    }
}
