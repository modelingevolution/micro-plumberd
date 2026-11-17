using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using KurrentDB.Client;
using Microsoft.Extensions.Logging.Abstractions;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Srv;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

[TestCategory("Integration")]
public class CommandBusTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly TestAppHost _serverTestApp;
    private readonly TestAppHost _clientTestApp;

    public CommandBusTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
    {
        _eventStore = eventStore;
        _testOutputHelper = testOutputHelper;
        _serverTestApp = new TestAppHost(testOutputHelper);
        _clientTestApp = new TestAppHost(testOutputHelper);
    }
        
    [Fact]
    public async Task HandleAggregateValidateCommand()
    {
        await _eventStore.StartInDocker();

        var client = await _clientTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
            .StartAsync();

        var bus = client.GetRequiredService<ICommandBus>();
            
        var recipientId = Guid.NewGuid();
        var mth = async () => await bus.SendAsync(recipientId, new ValidateBoo() { Name = $"1" });
        await mth.Should().ThrowAsync<ValidationException>();

    }



    // feature-017 design.md § slice B1: the session set-up accepts metadata an earlier attempt already wrote
    // (StreamState.Any) and is not cached on failure.
    [Fact]
    public async Task SendAsync_WhenSessionMetadataAlreadyLanded_Succeeds()
    {
        await _eventStore.StartInDocker();

        await _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings())
                .AddCommandHandler<FooCommandHandler>())
            .StartAsync();

        var client = await _clientTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
            .StartAsync();

        var plumber = client.GetRequiredService<IPlumberInstance>();
        var bus = new CommandBus(plumber, client.GetRequiredService<ICommandBusPool>(), NullLogger<CommandBus>.Instance);

        // An earlier set-up attempt that landed its first metadata write.
        var streamIn = plumber.Config.Conventions.ServicesConventions().SessionInStreamFromSessionIdConvention(bus.SessionId);
        await plumber.Client.SetStreamMetadataAsync(streamIn, StreamState.NoStream, new KurrentDB.Client.StreamMetadata(maxAge: TimeSpan.FromDays(30)));

        await bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "first" });
        await bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "second" });
    }
}