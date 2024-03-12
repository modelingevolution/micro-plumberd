using System.Net;
using FluentAssertions;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;
using NSubstitute;

namespace MicroPlumberd.Tests;

public class FooCommandHandlerTests
{
    [Fact]
    public void ServerMessagesCount()
    {
        var actual = Messages<FooCommandHandler>().ToArray();
        actual.Should().BeEquivalentTo(new[] { typeof(HandlerOperationStatus), typeof(CreateFoo), typeof(ChangeFoo), typeof(BusinessFault) });
    }

    [Fact]
    public async Task ClientMessagesCount()
    {
        await using var client = new ClientApp();

        var sp = client.Start(service => service.AddClientDirectConnect()
            .AddCommandInvokers(typeof(CreateFoo), typeof(ChangeFoo)));

        var t= sp.GetRequiredService<TypeRegister>();

        t[typeof(CommandEnvelope<CreateFoo>).FullName.ToGuid()].Should().NotBeNull();
        t[typeof(CommandEnvelope<ChangeFoo>).FullName.ToGuid()].Should().NotBeNull();
        t[typeof(BusinessFault).FullName.ToGuid()].Should().NotBeNull();
        t[typeof(HandlerOperationStatus).FullName.ToGuid()].Should().NotBeNull();
    }

    private IEnumerable<Type> Messages<T>() where T : IApiTypeRegister
    {
        return T.FaultTypes.Union(T.CommandTypes).Union(T.ReturnTypes).Distinct();
    }

    [Fact]
    public async Task Handle()
    {
        await using ServerApp srv = new ServerApp();
        
        await srv.StartAsync(x => { x.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect(); });

        await using var client = new ClientApp();

        var sp = client.Start(service => service.AddClientDirectConnect()
            .AddCommandInvokers(typeof(CreateFoo), typeof(ChangeFoo)));
            
        var clientPool = sp.GetRequiredService<IRequestInvokerPool>();
        var invoker = clientPool.Get("http://localhost:5001");
        var streamId = Guid.NewGuid();

        await invoker.Execute(streamId, new CreateFoo() { Name="Hello"});
        var ret2 = await invoker.Execute<HandlerOperationStatus>(streamId, new ChangeFoo() { Name = "Hello" });
        ret2.Code.Should().Be(HttpStatusCode.OK);
    }
    [Fact]
    public async Task HandleException()
    {
        await using ServerApp srv = new ServerApp();

        await srv.StartAsync(x => { x.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect(); });

        await using var client = new ClientApp();

        var sp = client.Start(service => service.AddClientDirectConnect()
            .AddCommandInvokers(typeof(CreateFoo), typeof(ChangeFoo)));

        var clientPool = sp.GetRequiredService<IRequestInvokerPool>();
        var invoker = clientPool.Get("http://localhost:5001");
        var streamId = Guid.NewGuid();

        await invoker.Execute(streamId, new CreateFoo() { Name = "Hello" });

        var action = async () => await invoker.Execute<HandlerOperationStatus>(streamId, new ChangeFoo() { Name = "error" });
        await action.Should().ThrowAsync<FaultException<BusinessFault>>();
    }
}
