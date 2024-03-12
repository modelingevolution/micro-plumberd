using System.Net;
using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;
using NSubstitute;

namespace MicroPlumberd.Tests;

public class CommandHandler_IntegrationTests : IDisposable, IAsyncDisposable
{
    private ClientApp client;
    private const int EVENTSTORE_PORT = 2119;
    [Fact]
    public async Task ApiSuccessfullInvocation()
    {
        await ArrangeEventStore();

        await using ServerApp srv = new ServerApp(EVENTSTORE_PORT);
        await srv.StartAsync(x => { x.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect(); });

        var invoker = await ArrangeClientApp();
        var streamId = Guid.NewGuid();

        await invoker.Execute(streamId, new CreateFoo() { Name="Hello"});
        var ret2 = await invoker.Execute<HandlerOperationStatus>(streamId, new ChangeFoo() { Name = "Hello" });
        ret2.Code.Should().Be(HttpStatusCode.OK);
    }
    [Fact]
    public async Task ApiInvocationWithProcessorCorrelation()
    {
        // Arrange
        // EventStore
        await ArrangeEventStore();
        // App Server
        await using ServerApp srv = new ServerApp(EVENTSTORE_PORT);

        var srvProvider = await srv.StartAsync(x =>
        {
            x.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect();
        });
        FooModel srvModel = new FooModel();

        await srvProvider.GetRequiredService<IPlumber>().SubscribeModel(srvModel, FromStream.End);
        await srvProvider.GetRequiredService<IPlumber>().SubscribeModel(new FooProcessor(srvProvider.GetRequiredService<IPlumber>()), FromStream.End);

        // Making sure we have subscribed.
        await Task.Delay(1000);
        
        // Client App
        var invoker = await ArrangeClientApp();

        // Invocation
        var streamId = Guid.NewGuid();
        var initialCommand = new CreateFoo() { Name = "Hello" };
        await invoker.Execute(streamId, initialCommand);
        var secondCommand = new ChangeFoo() { Name = "Hello" };
        var ret = await invoker.Execute<HandlerOperationStatus>(streamId, secondCommand);
        ret.Code.Should().Be(HttpStatusCode.OK);

        await Task.Delay(1000);
        // Let's wait for results to flow back.
        srvModel.Metadatas.Should().HaveCount(3);
        srvModel.Metadatas[2].CorrelationId().Should().Be(secondCommand.Id);
        srvModel.Events[2].Should().BeOfType<FooCreated>();
    }

    private async Task<IRequestInvoker> ArrangeClientApp()
    {
        this.client = new ClientApp();

        var sp = client.Start(service => service.AddClientDirectConnect()
            .AddCommandInvokers(typeof(CreateFoo), typeof(ChangeFoo)));

        var clientPool = sp.GetRequiredService<IRequestInvokerPool>();
        var invoker = clientPool.Get("http://localhost:5001");
        
        return invoker;
    }

    [Fact]
    public async Task ApiHandingExceptionInvocation()
    {
        await ArrangeEventStore();

        await using ServerApp srv = new ServerApp(EVENTSTORE_PORT);

        await srv.StartAsync(x => { x.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect(); });

        var invoker = await ArrangeClientApp();
        var streamId = Guid.NewGuid();

        await invoker.Execute(streamId, new CreateFoo() { Name = "Hello" });

        var action = async () => await invoker.Execute<HandlerOperationStatus>(streamId, new ChangeFoo() { Name = "error" });
        await action.Should().ThrowAsync<FaultException<BusinessFault>>();
    }

    private static async Task ArrangeEventStore()
    {
        var es = new EventStoreServer();
        await es.StartInDocker(EVENTSTORE_PORT);
        await Task.Delay(8000);
    }

    public void Dispose()
    {
        client?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
