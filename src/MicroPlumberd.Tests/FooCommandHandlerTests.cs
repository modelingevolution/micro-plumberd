using FluentAssertions;
using MicroPlumberd.DirectConnect;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;
using NSubstitute;

namespace MicroPlumberd.Tests;

public class FooCommandHandlerTests
{
    [Fact]
    public void MessagesCount()
    {
        var actual = Messages<FooCommandHandler>().ToArray();
        actual.Should().BeEquivalentTo(new[] { typeof(HandlerOperationStatus), typeof(CreateFoo), typeof(ChangeFoo), typeof(BusinessFault) });
    }

    private IEnumerable<Type> Messages<T>() where T : IApiTypeRegister
    {
        return T.FaultTypes.Union(T.CommandTypes).Union(T.ReturnTypes).Distinct();
    }

    [Fact]
    public async Task Handle()
    {
        using ServerApp srv = new ServerApp();
        
        await srv.StartAsync(x =>
        {
            x.AddCommandHandler<FooCommandHandler>()
                .AddServerDirectConnect();
        });

        using var client = new ClientApp();

        var sp = client.Start(service => service.AddClientDirectConnect()
            .AddCommandInvokers(typeof(CreateFoo), typeof(ChangeFoo)));
            
        var clientPool = sp.GetRequiredService<IRequestInvokerPool>();
        var invoker = clientPool.Get("http://localhost:5001");
        var streamId = Guid.NewGuid();

        await invoker.Execute(streamId, new CreateFoo() { Name="Hello"});
        var ret2 = await invoker.Execute<HandlerOperationStatus>(streamId, new ChangeFoo() { Name = "Hello" });
        //ret.Name.Should().Be("Test2");

        //await customHandler.Received(1).Handle(Arg.Is<FooRequest>(x => x.Name == "Test"));
    }
}
