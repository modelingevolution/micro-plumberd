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

public class ProcessManagerGnerationTests
{
    [Fact]
    public void CommandTypes()
    {
        GetCommandTypes<XooProcessManager>().Should().BeEquivalentTo(Expected());
    }



    [Fact]
    public void Types()
    {
        GetTypes<XooProcessManager>().Should().BeEquivalentTo(ExpectedTypes.AsEnumerable());

    }

    [Fact]
    public void StartType()
    {
        GetStartType<XooProcessManager>().Should().Be(typeof(FooCreated));
    }

    IEnumerable<Type> ExpectedTypes 
    {
        get
        {
            yield return typeof(FooCreated);
            yield return typeof(BooUpdated);
            yield return typeof(CommandEnqueued<CreateBoo>);
        }
    }

    private Type GetStartType<T>() where T : IProcessManager
    {
        return T.StartEvent;
    }
    private IEnumerable<Type> GetTypes<T>() where T : ITypeRegister
    {
        return T.Types;
    }
    private IEnumerable<Type> Expected()
    {
        yield return typeof(CommandEnqueued<CreateBoo>);
        yield return typeof(CommandEnqueued<CreateLoo>);
    }
    private IEnumerable<Type> GetCommandTypes<T>() where T : IProcessManager
    {
        return T.CommandTypes;
    }
}

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
        //t[typeof(BusinessFault).FullName!.ToGuid()].Should().NotBeNull();
        t[typeof(HandlerOperationStatus).FullName!.ToGuid()].Should().NotBeNull();
    }

    private IEnumerable<Type> Messages<T>() where T : IServiceTypeRegister
    {
        return T.FaultTypes.Union(T.CommandTypes).Union(T.ReturnTypes).Distinct();
    }
}