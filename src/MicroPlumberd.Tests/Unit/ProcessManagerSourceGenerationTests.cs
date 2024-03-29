using FluentAssertions;
using MicroPlumberd.Tests.App.Srv;
using MicroPlumberd.Tests.AppSrc;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Unit;

[TestCategory("Unit")]
public class ProcessManagerSourceGenerationTests
{
    [Fact]
    public void CommandTypes()
    {
        GetCommandTypes<XooProcessManager>().Should().BeEquivalentTo(ExpectedCommands());
    }


    [Fact]
    public void Types()
    {
        GetTypes<XooProcessManager>().Should().BeEquivalentTo(ExpectedTypes());
    }

    [Fact]
    public void StartType()
    {
        GetStartType<XooProcessManager>().Should().Be(typeof(FooCreated));
    }

    IEnumerable<Type> ExpectedTypes()
    {
        yield return typeof(FooCreated);
        yield return typeof(BooUpdated);
        yield return typeof(CommandEnqueued<CreateBoo>);
    }

    private Type GetStartType<T>() where T : IProcessManager
    {
        return T.StartEvent;
    }
    private IEnumerable<Type> GetTypes<T>() where T : ITypeRegister
    {
        return T.Types;
    }
    private IEnumerable<Type> ExpectedCommands()
    {
        yield return typeof(CommandEnqueued<CreateBoo>);
        yield return typeof(CommandEnqueued<CreateLoo>);
    }
    private IEnumerable<Type> GetCommandTypes<T>() where T : IProcessManager
    {
        return T.CommandTypes;
    }
}