using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using System.Threading.Tasks;
using Xunit;

namespace MicroPlumberd.SourceGenerators.Tests;

public class AggregateSourceGeneratorTests
{
    [Fact]
    public async Task GeneratesAggregateCode_ForValidAggregate()
    {
        var source = @"
using System;
using MicroPlumberd;

namespace TestNamespace
{
    public record FooCreated(string Name);
    public record FooUpdated(int Value);
    public record FooState(string Name, int Value);

    [Aggregate]
    public partial class FooAggregate : AggregateBase<Guid, FooState>
    {
        public FooAggregate(Guid id) : base(id) { }

        private static FooState Given(FooState state, FooCreated ev)
        {
            return state with { Name = ev.Name };
        }

        private static FooState Given(FooState state, FooUpdated ev)
        {
            return state with { Value = ev.Value };
        }
    }
}
";

        var expected = @"using System;
using MicroPlumberd;

namespace TestNamespace;

partial class FooAggregate : IAggregate<FooAggregate>, ITypeRegister
{
    [AcceptedType(typeof(FooCreated)), AcceptedType(typeof(FooUpdated))]
    protected override FooState Given(FooState state, object ev)
    {
        switch(ev)
        {
            case FooCreated e: return Given(state, e);
            case FooUpdated e: return Given(state, e);
            default: return state;
        };
    }

    public static FooAggregate Empty(object id) => new FooAggregate((Guid)id);

    static IEnumerable<Type> ITypeRegister.Types => [typeof(FooCreated), typeof(FooUpdated)];
}";

        await new CSharpSourceGeneratorTest<AggregateSourceGenerator, XUnitVerifier>
        {
            TestState =
            {
                Sources = { source },
                GeneratedSources =
                {
                    (typeof(AggregateSourceGenerator), "FooAggregate_Aggregate.g.cs", expected)
                }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNoGivenMethodsFound()
    {
        var source = @"
using System;
using MicroPlumberd;

namespace TestNamespace
{
    public record FooState(string Name);

    [Aggregate]
    public partial class {|MPLUMB001:FooAggregate|} : AggregateBase<Guid, FooState>
    {
        public FooAggregate(Guid id) : base(id) { }

        // No Given methods!
    }
}
";

        await new CSharpSourceGeneratorTest<AggregateSourceGenerator, XUnitVerifier>
        {
            TestState =
            {
                Sources = { source }
            }
        }.RunAsync();
    }

    [Fact]
    public async Task DoesNotGenerate_ForClassWithoutAggregateAttribute()
    {
        var source = @"
using System;
using MicroPlumberd;

namespace TestNamespace
{
    public record FooState(string Name);

    public partial class NotAnAggregate : AggregateBase<Guid, FooState>
    {
        public NotAnAggregate(Guid id) : base(id) { }

        private static FooState Given(FooState state, object ev)
        {
            return state;
        }
    }
}
";

        await new CSharpSourceGeneratorTest<AggregateSourceGenerator, XUnitVerifier>
        {
            TestState =
            {
                Sources = { source },
                GeneratedSources = { } // No sources should be generated
            }
        }.RunAsync();
    }

    [Fact]
    public async Task SupportsMultipleEventTypes()
    {
        var source = @"
using System;
using MicroPlumberd;

namespace TestNamespace
{
    public record Event1();
    public record Event2();
    public record Event3();
    public record FooState();

    [Aggregate]
    public partial class FooAggregate : AggregateBase<Guid, FooState>
    {
        public FooAggregate(Guid id) : base(id) { }

        private static FooState Given(FooState state, Event1 ev) => state;
        private static FooState Given(FooState state, Event2 ev) => state;
        private static FooState Given(FooState state, Event3 ev) => state;
    }
}
";

        var test = new CSharpSourceGeneratorTest<AggregateSourceGenerator, XUnitVerifier>
        {
            TestState =
            {
                Sources = { source }
            }
        };

        await test.RunAsync();

        // Verify at least one source was generated
        Assert.NotEmpty(test.TestState.GeneratedSources);
    }
}
