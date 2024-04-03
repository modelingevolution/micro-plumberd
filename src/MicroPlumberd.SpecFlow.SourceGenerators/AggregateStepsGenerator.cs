using System.Linq;
using Humanizer;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

static class AggregateStepsGenerator
{
    public static string Generate(this AggregateDescriptor d, IAssemblySymbol assemblySymbol)
    {
        StringBuilder sb = new StringBuilder();

        var dstNs = assemblySymbol.Name.ToString();

        var ns = d.Type.ContainingNamespace.ToString();
        var assemblyRootNs = d.Type.ContainingAssembly.Identity.Name;
        if (ns.StartsWith(assemblyRootNs))
        {
            var rel = ns.Substring(assemblyRootNs.Length + 1);
            dstNs = $"{dstNs}.{rel}";
        }

        sb.AppendLine($"using System;");
        sb.AppendLine($"using System.Collections.Generic;");
        sb.AppendLine($"using System.Linq;");
        sb.AppendLine($"using System.Reflection;");
        sb.AppendLine($"using System.Reflection.Emit;");
        sb.AppendLine($"using System.Text;");
        sb.AppendLine($"using System.Text.Json;");
        sb.AppendLine($"using System.Threading.Tasks;");
        sb.AppendLine($"using FluentAssertions;");
        sb.AppendLine($"using TechTalk.SpecFlow;");

        sb.AppendLine($"using {ns};");
        sb.AppendLine($"namespace {dstNs};");
        sb.AppendLine($"// Generated with Micro-Plumberd");
        sb.AppendLine("[Binding]");
        sb.AppendLine($"public partial class {d.Name}Steps(AppStepsContext context)");
        sb.AppendLine("{");

        //private readonly AggregateSpecs<FooAggregate> _aggregateSpecs = context.AggregateSpecs<FooAggregate>();

        sb.AppendLine($"    private readonly AggregateSpecs<{d.Name}> _aggregateSpecs = context.AggregateSpecs<{d.Name}>();");
        foreach (var g in d.Givens)
        {
            GenerateGivenWithIdentifier(g, d, sb);
            GenerateSingleGivens(g, d, sb);
            GenerateManyGivens(g, d, sb);
            GenerateAnotherGivens(g, d, sb);
            GenerateExpected(g, d, sb);
            
        }
        GenerateExpectedState(d, sb);
        sb.AppendLine("}");
        return sb.ToString();
    }
private static void GenerateManyGivens(AggregateGivenDescriptor g, AggregateDescriptor agg, StringBuilder sb)
    {
        Sentence words = g.EventType.Name;
        Sentence subject = agg.Name;

        var coreName = subject + words;
        coreName = coreName.ToLower()
            .Remove("aggregate")
            .RemoveDuplicates()
            .ChangeWord(0, x => x.Pluralize());

        var regex = coreName
            .ToCapitalizedRegex()
            .InsertBackwards(1, "were");


        sb.AppendLine($"    [Given(@\"{regex}[:]?\")]");
        sb.AppendLine($"    [Given(@\"Some {regex}[:]?\")]");
        sb.AppendLine($"    public async Task {coreName.Dehumanize()}(Table s)");
        sb.AppendLine("    {");

        sb.AppendLine(
            $"        var ev = _aggregateSpecs.ArgumentProvider.RecognizeManyFromTable<{g.EventType.Name}>(s);");
        sb.AppendLine($"        foreach (var i in ev)");
        sb.AppendLine($"             await _aggregateSpecs.Given(i.Id, i.Data);");

        sb.AppendLine("    }");
    }

    private static void GenerateExpected(AggregateGivenDescriptor g, AggregateDescriptor agg, StringBuilder sb)
    {
        Sentence words = g.EventType.Name;
        Sentence subject = agg.Name;

        var coreName = subject + words;
        coreName = coreName.ToLower()
            .Remove("aggregate")
            .RemoveDuplicates();

        var regex = coreName
            .ToCapitalizedRegex()
            .InsertBackwards(1, "was");


        sb.AppendLine($"    [Then(@\"I expect[,] that {regex} with[:]?\")]");
        sb.AppendLine($"    public async Task Expect{coreName.Dehumanize()}(object s)");
        sb.AppendLine("    {");

        sb.AppendLine($"        var ev = _aggregateSpecs.ArgumentProvider.Recognize<{g.EventType.Name}>(s);");
        sb.AppendLine($"        await _aggregateSpecs.ExpectedPublished(ev);");

        sb.AppendLine("    }");
    }
    private static void GenerateExpectedState(AggregateDescriptor agg, StringBuilder sb)
    {
        Sentence subject = agg.Name;

        var coreName = subject;
        coreName = coreName.ToLower()
            .Remove("aggregate")
            .RemoveDuplicates();

        var regex = coreName
            .ToCapitalizedRegex();
        
        sb.AppendLine($"    [Then(@\"I expect[,] that {regex}'s state is set with[:]?\")]");
        sb.AppendLine($"    public async Task Expect{coreName.Dehumanize()}State(object s)");
        sb.AppendLine("    {");

        sb.AppendLine($"        var anonymous = _aggregateSpecs.ArgumentProvider.Recognize(s);");
        sb.AppendLine($"        await _aggregateSpecs.Then(x => x.State().Should().BeEquivalentTo(anonymous));");

        sb.AppendLine("    }");
    }
    private static void GenerateSingleGivens(AggregateGivenDescriptor g, AggregateDescriptor agg, StringBuilder sb)
    {
        Sentence words = g.EventType.Name;
        Sentence subject = agg.Name;

        var coreName = subject + words;
        coreName = coreName.ToLower()
            .Remove("aggregate")
            .RemoveDuplicates();

        var regex1 = coreName
            .ToCapitalizedRegex()
            .InsertBackwards(1, "was");

        var regex2 = coreName
            .ToCapitalizedRegex();


        sb.AppendLine($"    [Given(@\"{regex1}[:]?\")]");
        sb.AppendLine($"    [Given(@\"{regex2}[:]?\")]");
        sb.AppendLine($"    public async Task {coreName.Dehumanize()}(object evt)");
        sb.AppendLine("    {");

        sb.AppendLine($"        var ev = _aggregateSpecs.ArgumentProvider.Recognize<{g.EventType.Name}>(evt);");
        sb.AppendLine($"        await _aggregateSpecs.Given(ev);");

        sb.AppendLine("    }");
    }

    private static void GenerateAnotherGivens(AggregateGivenDescriptor g, AggregateDescriptor agg, StringBuilder sb)
    {
        Sentence words = g.EventType.Name;
        Sentence subject = agg.Name;

        var coreName = subject + words;
        coreName = coreName.ToLower()
            .Insert(0, "Another")
            .Remove("aggregate")
            .RemoveDuplicates();

        var regex = coreName
            .ToCapitalizedRegex()
            .InsertBackwards(1, "was");


        sb.AppendLine($"    [Given(@\"{regex}[:]?\")]");
        sb.AppendLine($"    public async Task {coreName.Dehumanize()}(object evt)");
        sb.AppendLine("    {");

        sb.AppendLine($"        var ev = _aggregateSpecs.ArgumentProvider.Recognize<{g.EventType.Name}>(evt);");
        sb.AppendLine($"        await _aggregateSpecs.Given(_aggregateSpecs.AnotherSubject(), ev);");

        sb.AppendLine("    }");
    }

    private static void GenerateGivenWithIdentifier(AggregateGivenDescriptor g, AggregateDescriptor agg,
        StringBuilder sb)
    {
        //[Given(@"[F|f]oo '(.*)' was [C|c]reated[:]?")]
        //public async Task GivenFooCreated(string id, object s)
        //{
        //    var ev = _aggregateSpecs.ArgumentProvider.Recognize<FooCreated>(s);
        //    await _aggregateSpecs.Given(id, ev);
        //}
        Sentence words = g.EventType.Name;
        Sentence subject = agg.Name;

        var coreName = subject + words;
        coreName = coreName.ToLower()
            .Remove("aggregate")
            .RemoveDuplicates();

        var regex = coreName
            .ToCapitalizedRegex()
            .Insert(1, "'(.*)'")
            .InsertBackwards(1, "was");


        sb.AppendLine($"    [Given(@\"{regex}[:]?\")]");
        sb.AppendLine($"    public async Task {coreName.Dehumanize()}(string id, object evt)");
        sb.AppendLine("     {");

        sb.AppendLine($"        var ev = _aggregateSpecs.ArgumentProvider.Recognize<{g.EventType.Name}>(evt);");
        sb.AppendLine($"        await _aggregateSpecs.Given(id, ev);");

        sb.AppendLine("     }");
    }
}