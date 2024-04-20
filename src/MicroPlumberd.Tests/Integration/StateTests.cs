using FluentAssertions;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Integration;

[TestCategory("Integration")]
public class StateTests : IClassFixture<EventStoreServer>
{
    private readonly IPlumber plumber;
    private readonly EventStoreServer es;

    public StateTests(EventStoreServer es)
    {
        plumber = Plumber.Create(es.GetEventStoreSettings());
        this.es = es;
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