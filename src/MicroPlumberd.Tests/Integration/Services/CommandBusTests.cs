using System.ComponentModel.DataAnnotations;
using FluentAssertions;
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



}