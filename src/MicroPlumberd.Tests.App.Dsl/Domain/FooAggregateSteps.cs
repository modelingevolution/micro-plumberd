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
using TechTalk.SpecFlow;

namespace MicroPlumberd.Tests.App.Dsl.Domain;


public partial class FooAggregateSteps
{

    
    // TODO
    [Scope(Tag = "Aggregate")]
    [When(@"I [C|c]hange Foo with msg:\s*'(.*)'")]
    public async Task WhenIChangeFooWithMsg(string mth)
    {
        await _aggregateSpecs.When(x => x.Refine(mth));
    }

   
    // TODO
    [Then(@"I expect business fault exception:")]
    public async Task ThenIExpectBusinessFaultException(object s)
    {
        var errorData = _aggregateSpecs.ArgumentProvider.Recognize<BusinessFault>(s);
        await _aggregateSpecs.ExpectedThrown<FaultException<BusinessFault>>(x => x.Data.Equals(errorData));
    }

}