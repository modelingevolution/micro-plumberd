using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading.Tasks;
using Humanizer;

namespace MicroPlumberd.SpecFlow.SourceGenerators
{
    [Generator]
    public class StepsGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            //System.Diagnostics.Debugger.Launch();
        }
        public static string FindAssemblyNamePattern(SyntaxTree syntaxTree)
        {
            // Find all AttributeListSyntax nodes in the syntax tree
            var attributeLists = syntaxTree.GetRoot().DescendantNodes().OfType<AttributeListSyntax>();

            foreach (var attributeList in attributeLists)
            {
                // Find all AttributeSyntax nodes within each attribute list
                foreach (var attribute in attributeList.Attributes)
                {
                    // Check if the attribute is DslFromAssembly2
                    if (attribute.Name.ToString().Contains("DslFromAssembly") || attribute.Name.ToString().Contains("DslFromAssemblyAttribute"))
                    {
                        // Check for the constructor argument
                        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();
                        // Attempt to retrieve the string value
                        if (argument?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                        {
                            return literal.Token.ValueText;
                        }
                    }
                }
            }

            // Return null or an appropriate value if the specific attribute or its argument is not found
            return null;
        }
        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            var stringBuilder = new StringBuilder();
            var dslAssemblyFilter = GetDslAssemblyFilterPattern(compilation);
            if (dslAssemblyFilter == null) return;
            GenerationContext genContext = new GenerationContext();
            foreach (var reference in compilation.ExternalReferences)
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;

                stringBuilder.AppendLine("// " + assemblySymbol.Identity);
                if (assemblySymbol.Identity.ToString().Contains(dslAssemblyFilter))
                {
                    // Now, with assemblySymbol, you can explore its namespaces and types
                    ExploreNamespace(assemblySymbol.GlobalNamespace, stringBuilder, genContext);
                }
            }
            
            foreach (var a in genContext.Aggregates)
            {
                var c = a.Generate(context.Compilation.Assembly);
                context.AddSource($"{a.Name}Steps",c);
            }
            Debug.WriteLine(stringBuilder.ToString());
        }

       

        private static string GetDslAssemblyFilterPattern(Compilation compilation)
        {
            string filter = null;

            var attrs = compilation.Assembly.GetAttributes();
            foreach (var a in attrs)
            {
                var syntaxNode = a.ApplicationSyntaxReference.GetSyntax();
                var syntaxTree = syntaxNode.SyntaxTree;
                var className = FindAssemblyNamePattern(syntaxTree);
                if (className != null)
                {
                    filter = className;
                    break;
                }

            }

            return filter;
        }

        void ExploreNamespace(INamespaceSymbol namespaceSymbol, StringBuilder stringBuilder, GenerationContext context)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceSymbol subNamespace)
                {
                    ExploreNamespace(subNamespace, stringBuilder, context);
                }
                else if (member is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Class)
                {
                    var members = member.GetMembers().ToArray();

                    if (typeSymbol.HasAttribute("CommandHandler"))
                    {
                        var ch = new CommandHandlerDescriptor();
                        ch.Name = typeSymbol.Name;
                        stringBuilder.AppendLine($"// Found class: {typeSymbol.ToDisplayString()}");
                        foreach (var i in members.OfType<IMethodSymbol>())
                        {
                            if (i.Name == "Handle" && i.Parameters.Length == 2) 
                                ch.Handles.Add(new HandleDescriptor(){ Method = i});
                        }
                        context.CommandHandlers.Add(ch);
                    } 
                    else if(typeSymbol.HasAttribute("Aggregate"))
                    {
                        var agg = new AggregateDescriptor(typeSymbol);
                        agg.Name = typeSymbol.Name;

                        var st = typeSymbol.Interfaces.FirstOrDefault(x => x.Name == "IAggregateStateAccessor");
                        for (ITypeSymbol c = typeSymbol; c != null; c = c.BaseType)
                        {
                            var i = c.Interfaces.FirstOrDefault(x => x.Name.Contains("IAggregateStateAccessor"));
                            if (i != null)
                            {
                                agg.StateType =i.TypeArguments[0];
                                break;
                            }
                        }

                        foreach (var m in members)
                        {
                            if (!(m is IMethodSymbol i)) continue;

                            if (i.Name == "Given" && i.Parameters.Length == 2 && i.Parameters[1].Type.ToString() == "object")
                            {
                                var attrs = i.GetAcceptedTypes();
                                foreach(var e in attrs)
                                    agg.Givens.Add(new AggregateGivenDescriptor() { EventType = e});
                            }
                            else if (i.DeclaredAccessibility == Accessibility.Public &&
                                     !i.IsStatic &&
                                     !i.IsAsync &&
                                     i.MethodKind != MethodKind.PropertyGet &&
                                     i.MethodKind != MethodKind.PropertySet &&
                                     !string.IsNullOrWhiteSpace(i.Name) &&
                                     i.MethodKind != MethodKind.Constructor)
                                agg.PublicMethods.Add(i);
                        }
                        context.Aggregates.Add(agg);
                    }
                    else if (typeSymbol.HasAttribute("EventHandler"))
                    {
                        var agg = new EventHandlerDescriptor();
                        agg.Name = typeSymbol.Name;
                        foreach (var i in members.OfType<IMethodSymbol>())
                        {
                            if (i.Name == "Given" && i.Parameters.Length == 2 && i.IsAsync)
                                agg.Givens.Add(new ModelGivenDescriptor() { Method = i });
                           
                        }
                        context.EventHandlers.Add(agg);
                    }
                }
            }
        }
    }

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
    

    static class Extensions
    {
        public static bool HasAttribute(this INamedTypeSymbol type, string name)
        {
            var attrs = type.GetAttributes();
            return attrs.Any(attr => attr.AttributeClass?.Name.Contains(name) ?? false);
        }

        public static IEnumerable<INamedTypeSymbol> GetAcceptedTypes(this IMethodSymbol method)
        {
            var acceptedTypeAttributes= method.GetAttributes()
                .Where(attr => attr.AttributeClass?.Name.StartsWith("AcceptedType") ?? false);
            foreach(var acceptedTypeAttribute in acceptedTypeAttributes){
                
                var acceptedTypeArgument = acceptedTypeAttribute?.ConstructorArguments.FirstOrDefault();

                if (acceptedTypeArgument?.Value is INamedTypeSymbol acceptedTypeSymbol)
                    yield return acceptedTypeSymbol;
            }
        }
    }

    class GenerationContext
    {
        public readonly List<AggregateDescriptor> Aggregates = new List<AggregateDescriptor>();
        public readonly List<CommandHandlerDescriptor> CommandHandlers = new List<CommandHandlerDescriptor>();
        public readonly List<EventHandlerDescriptor> EventHandlers = new List<EventHandlerDescriptor>();
    }
    [DebuggerDisplay("{Name}")]
    class AggregateDescriptor
    {
        public ITypeSymbol StateType { get; set; }
        public string Name { get; set; }
        public readonly List<AggregateGivenDescriptor> Givens = new List<AggregateGivenDescriptor>();
        public readonly List<IMethodSymbol> PublicMethods = new List<IMethodSymbol>();

        public AggregateDescriptor(INamedTypeSymbol type)
        {
            this.Type = type;
        }

        public INamedTypeSymbol Type { get; set; }
    }
    [DebuggerDisplay("{EventType.Name}")]
    class AggregateGivenDescriptor
    {
        public ITypeSymbol EventType { get; set; }
    }
    [DebuggerDisplay("{EventType.Name}")]
    class ModelGivenDescriptor
    {
        public IMethodSymbol Method { get; set; }
        public ITypeSymbol EventType => Method.Parameters[1].Type;
    }
    [DebuggerDisplay("{Name}")]
    class CommandHandlerDescriptor
    {
        public readonly List<HandleDescriptor> Handles = new List<HandleDescriptor>();
        public string Name { get; set; }
    }
    [DebuggerDisplay("{CommandType.Name}")]
    class HandleDescriptor
    {
        public ITypeSymbol CommandType => Method.Parameters[1].Type;
        public IMethodSymbol Method { get; set; }
    }

    [DebuggerDisplay("{Name}")]
    class EventHandlerDescriptor
    {
        public string Name { get; set; }
        public readonly List<ModelGivenDescriptor> Givens = new List<ModelGivenDescriptor>();
    }
}
