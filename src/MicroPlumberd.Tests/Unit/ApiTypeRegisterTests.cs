using FluentAssertions;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Fixtures;
using MicroPlumberd.Tests.Integration.Services.Grpc.DirectConnect.Fixtures;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.Unit;

[TestCategory("Unit")]
public class ApiTypeRegisterTests
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

        var t = sp.GetRequiredService<TypeRegister>();

        t[typeof(CommandEnvelope<CreateFoo>).FullName!.ToGuid()].Should().NotBeNull();
        t[typeof(CommandEnvelope<ChangeFoo>).FullName!.ToGuid()].Should().NotBeNull();
        t[typeof(BusinessFault).FullName!.ToGuid()].Should().NotBeNull();
        t[typeof(HandlerOperationStatus).FullName!.ToGuid()].Should().NotBeNull();
    }

    private IEnumerable<Type> Messages<T>() where T : IServiceTypeRegister
    {
        return T.FaultTypes.Union(T.CommandTypes).Union(T.ReturnTypes).Distinct();
    }
}