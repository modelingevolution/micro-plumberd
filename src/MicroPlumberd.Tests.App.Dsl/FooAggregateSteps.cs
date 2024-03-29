using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Srv;
using MicroPlumberd.Tests.AppSrc;
using TechTalk.SpecFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroPlumberd.Tests.App.Dsl
{


    [Binding]
    public partial class FooAggregateSteps(AppStepsContext context, ScenarioContext c)
    {
        private readonly Specs<FooAggregate> _specs = context.Specs<FooAggregate>();

        [Given(@"Foo was [C|c]reated[:]?")]
        [Given(@"Foo\s?[C|c]reated[:]?")]
        public async Task GivenFooCreated(object s)
        {
            var ev = _specs.ArgumentProvider.Recognize<FooCreated>(s);
            await _specs.Given(ev);
        }
        
        [Given(@"Foo was [U|u]pdated[:]?")]
        [Given(@"Foo [U|u]pdated[:]?")]
        public async Task GivenFooUpdated(object s)
        {
            var ev = _specs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _specs.Given(ev);
        }

        [When(@"I [C|c]hange Foo with msg:\s*'(.*)'")]
        public async Task WhenIChangeFooWithMsg(string mth)
        {
            await _specs.When(x => x.Change(mth));
        }

        [Then(@"I expect[,] that Foo was updated with:")]
        public async Task ThenIExpectThatFooWasUpdatedWith(object s)
        {
            var ev = _specs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _specs.Then(ev);
        }

        [Then(@"I expect business fault exception:")]
        public async Task ThenIExpectBusinessFaultException(object s)
        {
            var errorData = _specs.ArgumentProvider.Recognize<BusinessFault>(s);
            await _specs.ThenThrown<BusinessFaultException>(x=>x.Data.Equals(errorData));
        }
    }
}
