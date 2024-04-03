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
        sb.AppendLine($"using MicroPlumberd.Tests.App.Domain;");
        sb.AppendLine($"using TechTalk.SpecFlow;");

        sb.AppendLine($"namespace {dstNs};");
        sb.AppendLine("[Binding]");
        sb.AppendLine($"public partial class {d.Name}Steps(AppStepsContext context)");
        sb.AppendLine("{");

        //private readonly AggregateSpecs<FooAggregate> _aggregateSpecs = context.AggregateSpecs<FooAggregate>();

        sb.AppendLine($"    private readonly AggregateSpecs<{d.Name}> _aggregateSpecs = context.AggregateSpecs<{d.Name}>();");
        foreach (var g in d.Givens)
        {
            GenerateGivenWithIdentifier(g,d,sb);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void GenerateGivenWithIdentifier(AggregateGivenDescriptor g, AggregateDescriptor agg, StringBuilder sb)
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
            .InsertBackwards(1, "was")
            .InsertBackwards(0, "[:]?");

            
        sb.AppendLine($"    [Given(@\"{regex}\")]");
        sb.AppendLine($"    public async Task {coreName.Dehumanize()}(string id, object evt)");
        sb.AppendLine("     {");

        sb.AppendLine($"        var ev = _aggregateSpecs.ArgumentProvider.Recognize<{g.EventType.Name}>(evt);");
        sb.AppendLine($"        await _aggregateSpecs.Given(id, ev);");

        sb.AppendLine("     }");
    }

}