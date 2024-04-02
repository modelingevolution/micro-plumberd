using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;
using ModelingEvolution.DirectConnect;
using TechTalk.SpecFlow;

namespace MicroPlumberd.Tests.App.Dsl;

[Binding]
public partial class CommandHandlerSteps(AppStepsContext context)
{
    private readonly CommandHandlerSpecs<FooCommandHandler> _specs = context.CommandHandlerSpecs<FooCommandHandler>();

    [When(@"I change foo '(.*)' with:")]
    public async Task WhenIChangeFooWith(string id, object arg)
    {
        var cmd = context.SpecsRoot.ArgumentProvider.Recognize<ChangeFoo>(arg);
        var recipient = Guid.TryParse(id, out var g) ? g : id.ToGuid();
        await _specs.When(recipient, cmd);
    }
    [When(@"I change foo with:")]
    public async Task WhenIChangeFooWith(object arg)
    {
        var cmd = context.SpecsRoot.ArgumentProvider.Recognize<ChangeFoo>(arg);
        await _specs.When(cmd);
    }

    
}