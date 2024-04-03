using FluentAssertions;
using MicroPlumberd.Tests.App.Domain;
using ModelingEvolution.DirectConnect;
using TechTalk.SpecFlow;

namespace MicroPlumberd.Tests.App.Dsl;

[Binding]
public partial class FooModelSteps(AppStepsContext context)
{
    [When(@"I find by id '(.*)'")]
    public async Task WhenIFindById(string id)
    {
        Guid arg_1 = Guid.TryParse(id, out var r) ? r : id.ToGuid();
        await context.ModelSpecs<FooModel>().When(x => x.FindById(arg_1));
    }

    [Then(@"I get '(.*)'")]
    public void ThenIGet(string arg)
    {
        context.ModelSpecs<FooModel>().ThenQueryResult(x => x.Should().Be(arg));
    }
}