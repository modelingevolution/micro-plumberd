using FluentAssertions;
using MicroPlumberd;
using MicroPlumberd.Services.EventAggregator;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.EventAggregator;

namespace MicroPlumberd.Services.EventAggregator.Tests;

public class EventAggregatorPropagationTests
{
    [Fact]
    public async Task Propagation_delivers_registered_event_to_handler()
    {
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        propagation.Register<TestEvent, Guid>(broadcast: false);

        var received = new List<EventEnvelope<Guid, TestEvent>>();
        propagation.EventAggregator.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(env => received.Add(env), keepSubscriberReferenceAlive: true);

        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("hello");
        var context = OperationContext.Create(Flow.Component);

        await propagation.OnEventAppendingAsync(context, evt, recipientId, null);

        received.Should().HaveCount(1);
        received[0].RecipientId.Should().Be(recipientId);
        received[0].Event.Should().Be(evt);
    }

    [Fact]
    public async Task Propagation_ignores_unregistered_event_types()
    {
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        propagation.Register<TestEvent, Guid>(broadcast: false);

        var received = new List<EventEnvelope<Guid, AnotherEvent>>();
        propagation.EventAggregator.GetEvent<EventEnvelope<Guid, AnotherEvent>>()
            .Subscribe(env => received.Add(env), keepSubscriberReferenceAlive: true);

        var context = OperationContext.Create(Flow.Component);

        await propagation.OnEventAppendingAsync(context, new AnotherEvent(42), Guid.NewGuid(), null);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task Propagation_broadcasts_when_enabled()
    {
        // Use a real pool and a second EA to observe the broadcast
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        propagation.Register<TestEvent, Guid>(broadcast: true);

        // Create a second EA on the same pool — simulates a Blazor circuit
        using var circuitEa = new ModelingEvolution.EventAggregator.EventAggregator(new NullForwarder(), pool);

        var broadcastReceived = new List<EventEnvelope<Guid, TestEvent>>();
        circuitEa.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(env => broadcastReceived.Add(env), keepSubscriberReferenceAlive: true);

        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("broadcast me");
        var context = OperationContext.Create(Flow.Component);

        await propagation.OnEventAppendingAsync(context, evt, recipientId, null);
        await Task.Delay(100);

        broadcastReceived.Should().HaveCount(1);
        broadcastReceived[0].RecipientId.Should().Be(recipientId);
        broadcastReceived[0].Event.Should().Be(evt);
    }

    [Fact]
    public async Task Propagation_does_not_broadcast_when_disabled()
    {
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        propagation.Register<TestEvent, Guid>(broadcast: false);

        // Second EA on the same pool
        using var circuitEa = new ModelingEvolution.EventAggregator.EventAggregator(new NullForwarder(), pool);

        var broadcastReceived = new List<EventEnvelope<Guid, TestEvent>>();
        circuitEa.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(env => broadcastReceived.Add(env), keepSubscriberReferenceAlive: true);

        var context = OperationContext.Create(Flow.Component);

        await propagation.OnEventAppendingAsync(context, new TestEvent("no broadcast"), Guid.NewGuid(), null);
        await Task.Delay(100);

        broadcastReceived.Should().BeEmpty();
    }

    [Fact]
    public void EnsureHookInstalled_installs_hook_exactly_once()
    {
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        var engine = PlumberEngine.Create();
        int hookCallCount = 0;

        propagation.EnsureHookInstalled(engine);
        propagation.EnsureHookInstalled(engine); // second call should be no-op

        propagation.Register<TestEvent, Guid>(broadcast: false);

        propagation.EventAggregator.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(_ => hookCallCount++, keepSubscriberReferenceAlive: true);

        propagation.OnEventAppendingAsync(OperationContext.Create(Flow.Component),
            new TestEvent("test"), Guid.NewGuid(), null).Wait();

        hookCallCount.Should().Be(1, "hook should fire exactly once");
    }

    [Fact]
    public async Task End_to_end_propagation_handler_receives_event_via_hook()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => PlumberEngine.Create());
        services.AddScoped(_ => OperationContext.Create(Flow.Component));

        services.AddSingleton<EventAggregatorPool>();
        services.AddSingleton<IEventAggregatorPool>(sp => sp.GetRequiredService<EventAggregatorPool>());
        services.AddSingleton<IEventAggregatorForwarder, NullForwarder>();
        services.AddScoped<IEventAggregator, ModelingEvolution.EventAggregator.EventAggregator>();

        services.AddEventAggregatorPropagation<TestEvent, Guid>();

        services.AddSingleton<TestHandler>();
        services.AddSingletonEventAggregatorHandler<TestHandler, Guid>();

        await using var sp = services.BuildServiceProvider();

        var starter = sp.GetRequiredService<EventAggregatorEventHandlerStarter<TestHandler, Guid>>();
        await starter.Start(CancellationToken.None);

        var propagation = sp.GetRequiredService<EventAggregatorPropagation>();

        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("via propagation");
        var context = OperationContext.Create(Flow.Component);
        await propagation.OnEventAppendingAsync(context, evt, recipientId, null);

        await Task.Delay(200);

        var handler = sp.GetRequiredService<TestHandler>();
        handler.ReceivedEvents.Should().HaveCount(1);
        handler.ReceivedEvents[0].Event.Should().Be(evt);
        handler.ReceivedEvents[0].Metadata.StreamId<Guid>().Should().Be(recipientId);
    }

    [Fact]
    public void AddEventAggregatorPropagation_registers_registry_singleton()
    {
        var services = new ServiceCollection();
        services.AddEventAggregatorPropagation<TestEvent, Guid>();
        services.AddEventAggregatorPropagation<AnotherEvent, Guid>(broadcast: true);

        var registryDescriptors = services
            .Where(d => d.ImplementationInstance is EventAggregatorPropagationRegistry)
            .ToList();
        registryDescriptors.Should().HaveCount(1, "registry should be a singleton reused across calls");
    }

    [Fact]
    public async Task Propagation_with_broadcast_true_delivers_to_both_local_and_pool()
    {
        var pool = new EventAggregatorPool();
        var propagation = new EventAggregatorPropagation(pool);
        propagation.Register<TestEvent, Guid>(broadcast: true);

        // Local subscriber
        var localReceived = new List<EventEnvelope<Guid, TestEvent>>();
        propagation.EventAggregator.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(env => localReceived.Add(env), keepSubscriberReferenceAlive: true);

        // Circuit subscriber (via pool broadcast)
        using var circuitEa = new ModelingEvolution.EventAggregator.EventAggregator(new NullForwarder(), pool);
        var broadcastReceived = new List<EventEnvelope<Guid, TestEvent>>();
        circuitEa.GetEvent<EventEnvelope<Guid, TestEvent>>()
            .Subscribe(env => broadcastReceived.Add(env), keepSubscriberReferenceAlive: true);

        var recipientId = Guid.NewGuid();
        var evt = new TestEvent("both");
        var context = OperationContext.Create(Flow.Component);

        await propagation.OnEventAppendingAsync(context, evt, recipientId, null);
        await Task.Delay(100);

        localReceived.Should().HaveCount(1);
        localReceived[0].Event.Should().Be(evt);

        broadcastReceived.Should().HaveCount(1);
        broadcastReceived[0].Event.Should().Be(evt);
    }
}
