using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class ReadModelTests : IAsyncDisposable, IDisposable
{
    private readonly EventStoreServer _eventStore;

    private IPlumber plumber;

    public ReadModelTests()
    {
        _eventStore = new EventStoreServer();
        plumber = Plumber.Create(_eventStore.GetEventStoreSettings());
    }

    [Fact]
    public async Task SubscribeModelPersistently()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var fooModel = new FooModel(new InMemoryAssertionDb());

        var sub = await plumber.SubscribeEventHandlerPersistently(fooModel, startFrom: StreamPosition.Start);

        await Task.Delay(1000);

        fooModel.AssertionDb.Index.Should().HaveCount(1);
    }
    [Fact]
    public async Task SubscribeModelFromEnd()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();
        await AppendOneEvent();
        await Task.Delay(100);

        var fooModel = new FooModel(new InMemoryAssertionDb());
        var sub = await plumber.SubscribeEventHandler(fooModel, start: FromRelativeStreamPosition.End-1);

        await Task.Delay(1000);

        fooModel.AssertionDb.Index.Should().HaveCount(1);

        await AppendOneEvent();
        await Task.Delay(200);
        fooModel.AssertionDb.Index.Should().HaveCount(2);
    }
    [Fact]
    public async Task SubscribeModel()
    {
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var fooModel = new FooModel(new InMemoryAssertionDb());

        var sub = await plumber.SubscribeEventHandler(fooModel);

        await Task.Delay(1000);

        fooModel.AssertionDb.Index.Should().HaveCount(1);
    }
    [Fact]
    public async Task SubscribeModelWithEventStoreRestart()
    {
        await _eventStore.StartInDocker(inMemory:false);
        await AppendOneEvent();

        var fooModel = new FooModel(new InMemoryAssertionDb());

        var sub = await plumber.SubscribeEventHandler(fooModel);

        await Task.Delay(1000);

        fooModel.AssertionDb.Index.Should().HaveCount(1);

        await _eventStore.Restart(TimeSpan.FromSeconds(5));
        await AppendOneEvent();
        await Task.Delay(1000);
        fooModel.AssertionDb.Index.Should().HaveCount(2);

    }
    [Fact]
    public async Task SubscribeScopedModel()
    {
        // TODO: Switch to EF to check
        await _eventStore.StartInDocker();
        await AppendOneEvent();

        var sp = new ServiceCollection()
            .AddPlumberd(_eventStore.GetEventStoreSettings())
            .AddEventHandler<FooModel>()
            .BuildServiceProvider();
        using var scope = sp.CreateScope();

        plumber = scope.ServiceProvider.GetRequiredService<IPlumber>();

        var sub = await plumber.SubscribeEventHandler<FooModel>();

        await Task.Delay(1000);
        // Should check db.
    }

    private async Task AppendOneEvent()
    {
        FooAggregate aggregate = FooAggregate.Open("Hello");
        await plumber.SaveNew(aggregate);
    }

    public async ValueTask DisposeAsync()
    {
        await _eventStore.DisposeAsync();
    }

    public void Dispose()
    {
        _eventStore.Dispose();
    }
}