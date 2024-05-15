using System.Diagnostics;
using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Encryption;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.App.Srv;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services;

[TestCategory("Integration")]
public class SecretTests : IClassFixture<EventStoreServer>
{
    private readonly EventStoreServer _eventStore;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly TestAppHost _serverTestApp;
    private readonly TestAppHost _clientTestApp;

    public SecretTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
    {
        _eventStore = eventStore;
        _testOutputHelper = testOutputHelper;
        _serverTestApp = new TestAppHost(testOutputHelper);
        _clientTestApp = new TestAppHost(testOutputHelper);
    }
    [Fact]
    public async Task HandleCommand()
    {
        await _eventStore.StartInDocker();

        _serverTestApp.Configure(x => x
            .AddEncryption()
            .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp,x) => x.EnableEncryption())
            .AddCommandHandler<SecretCommandHandler>(start: StreamPosition.Start));

        var srv = await _serverTestApp.StartAsync();

        var cmd = new CreateSecret() { Password = "Very secret password" };
        var recipientId = Guid.NewGuid();

        Stopwatch sw = new Stopwatch();
        sw.Start();

        var client = await _clientTestApp.Configure(x => x
                .AddEncryption()
                .AddPlumberd(_eventStore.GetEventStoreSettings(), (sp, x) =>
                    x
                        .EnableEncryption()
                        .ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(10)))
            .StartAsync();

        await client.GetRequiredService<ICommandBus>().SendAsync(recipientId, cmd);

        _testOutputHelper.WriteLine("Command executed in: " + sw.Elapsed);
        var result = await srv.GetRequiredService<IPlumber>().ReadEventsOfType<SecretCreated>().FirstOrDefaultAsync();

        string pwd = result.Item1.Password;
        pwd.Should().Be("Very secret password");

    }
}