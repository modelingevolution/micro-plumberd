using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MicroPlumberd.Tests.App.Domain;
using ModelingEvolution.DirectConnect;
using TechTalk.SpecFlow;

namespace MicroPlumberd.Tests.App.Dsl
{
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

    [Binding]
    public partial class FooAggregateSteps(AppStepsContext context)
    {
        private readonly AggregateSpecs<FooAggregate> _aggregateSpecs = context.AggregateSpecs<FooAggregate>();
        
        [Given(@"[F|f]oo '(.*)' was [C|c]reated[:]?")]
        public async Task GivenFooCreated(string id, object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooCreated>(s);
            await _aggregateSpecs.Given(id, ev);
        }
        
        [Given(@"Some [F|f]oos were [C|c]reated[:]?")]
        public async Task GivenFoosCreated(Table s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.RecognizeManyFromTable<FooCreated>(s);
            foreach(var i in ev)
                await _aggregateSpecs.Given(i.Id, i.Data);
        }
        [Given(@"[F|f]oo was [C|c]reated[:]?")]
        [Given(@"[F|f]oo\s?[C|c]reated[:]?")]
        public async Task GivenFooCreated(object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooCreated>(s);
            await _aggregateSpecs.Given(ev);
        }
        
        [Given(@"Foo '(.*)' was [U|u]pdated[:]?")]
        public async Task GivenFooUpdated(string id, object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _aggregateSpecs.Given(id,ev);
        }
        
        [Given(@"Foo was [U|u]pdated[:]?")]
        [Given(@"Foo [U|u]pdated[:]?")]
        public async Task GivenFooUpdated(object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _aggregateSpecs.Given(ev);
        }
        [Given(@"[A|a]nother [F|f]oo was [C|c]reated[:]?")]
        public async Task GivenAnotherFooUpdated(object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _aggregateSpecs.Given(_aggregateSpecs.AnotherSubject(),ev);
        }
        
        [Scope(Tag = "Aggregate")]
        [When(@"I [C|c]hange Foo with msg:\s*'(.*)'")]
        public async Task WhenIChangeFooWithMsg(string mth)
        {
            await _aggregateSpecs.When(x => x.Change(mth));
        }

        [Then(@"I expect[,] that Foo was updated with:")]
        public async Task ThenIExpectThatFooWasUpdatedWith(object s)
        {
            var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooUpdated>(s);
            await _aggregateSpecs.ExpectedPublished(ev);
        }

        [Then(@"I expect business fault exception:")]
        public async Task ThenIExpectBusinessFaultException(object s)
        {
            var errorData = _aggregateSpecs.ArgumentProvider.Recognize<BusinessFault>(s);
            await _aggregateSpecs.ExpectedThrown<FaultException<BusinessFault>>(x=>x.Data.Equals(errorData));
        }
        
        [Then(@"I expect, that Foo's state is set with:")]
        public async Task ThenIExpectThatFoosStateIsSetWith(object s)
        {
            var anonymous = _aggregateSpecs.ArgumentProvider.Recognize(s);
            await _aggregateSpecs.Then(x => x.State().Should().BeEquivalentTo(anonymous));
        }
    }

    public static class AssertionExtensions
    {
        public static T State<T>(this IAggregateStateAccessor<T> aggregate)
        {
            return aggregate.State;
        }
    }
}
