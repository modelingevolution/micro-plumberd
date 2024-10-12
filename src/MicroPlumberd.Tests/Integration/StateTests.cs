using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class SubscriptionRunnerStateTests : IClassFixture<EventStoreServer>
{
    private readonly IPlumber plumber;
    private readonly EventStoreServer es;

    public SubscriptionRunnerStateTests(EventStoreServer es)
    {
        plumber = Plumber.Create(es.GetEventStoreSettings());
        this.es = es;
    }

    [Fact]
    public async Task ThrowsWrongExpectedVersionExceptionWhenStreamAlreadyExists()
    {
        await es.StartInDocker();

        var st = new FooEntityState() { Name = "Foo" };
        await plumber.AppendState(st);

        var st2 = new FooEntityState() { Id = st.Id, Name = "Whatever" };

        var mth = async () => await plumber.AppendState(st2);

        await mth.Should().ThrowAsync<WrongExpectedVersionException>();
    }
    [Fact]
    public async Task ThrowsWrongExpectedVersionExceptionWhenOnDoubleChange()
    {
        await es.StartInDocker();

        var root = new FooEntityState() { Name = "Foo" };
        await plumber.AppendState(root);

        FooEntityState second = await plumber.GetState<FooEntityState>(root.Id);
        
        root.Name = "Bar";
        await plumber.AppendState(root);

        var mth = async () => await plumber.AppendState(second);

        await mth.Should().ThrowAsync<WrongExpectedVersionException>();
    }

    [Fact]
    public async Task TripleWrite()
    {
        await es.StartInDocker();

        var st = new FooEntityState() { Name = "Foo"};
        await plumber.AppendState(st);

        st.Name = "Bar";
        await plumber.AppendState(st);

        st.Name = "Xoo";
        await plumber.AppendState(st);

        FooEntityState actual = await plumber.GetState<FooEntityState>(st.Id);

        actual.Should().Be(st);
    }
    [Fact]
    public async Task WriteAfterGet()
    {
        await es.StartInDocker();

        var st = new FooEntityState() { Name = "Foo" };
        await plumber.AppendState(st);
            
        FooEntityState nx = await plumber.GetState<FooEntityState>(st.Id);
        nx.Name = "Bar";
        await plumber.AppendState(nx);

        FooEntityState actual = await plumber.GetState<FooEntityState>(st.Id);
        actual.Should().Be(nx);

    }
}