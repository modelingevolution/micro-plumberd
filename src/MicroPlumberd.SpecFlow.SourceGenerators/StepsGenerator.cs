using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MicroPlumberd.SpecFlow.SourceGenerators
{
    [Generator]
    public class StepsGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //System.Diagnostics.Debugger.Launch();

            var generationDataProvider = context.CompilationProvider
                .Select((compilation, cancellationToken) =>
                {
                    var dslAssemblyFilter = GetDslAssemblyFilterPattern(compilation);
                    if (dslAssemblyFilter == null)
                        return default((Compilation Compilation, GenerationContext GenContext)?);

                    var genContext = new GenerationContext();
                    foreach (var reference in compilation.ExternalReferences)
                    {
                        var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                        if (assemblySymbol != null && assemblySymbol.Identity.ToString().Contains(dslAssemblyFilter))
                        {
                            ExploreNamespace(assemblySymbol.GlobalNamespace, genContext);
                        }
                    }

                    return ((Compilation Compilation, GenerationContext GenContext)?)(compilation, genContext);
                })
                .Where(static x => x.HasValue)
                .Select(static (x, _) => x!.Value);

            context.RegisterSourceOutput(generationDataProvider, (sourceProductionContext, data) =>
            {
                var assemblySymbol = data.Compilation.Assembly;
                foreach (var a in data.GenContext.Aggregates)
                {
                    var c = a.Generate(assemblySymbol);
                    sourceProductionContext.AddSource($"{a.Name}Steps", c);
                }
            });
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
                    // Check if the attribute is DslFromAssembly or DslFromAssemblyAttribute
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

        private static string GetDslAssemblyFilterPattern(Compilation compilation)
        {
            string filter = null;

            var attrs = compilation.Assembly.GetAttributes();
            foreach (var a in attrs)
            {
                var syntaxReference = a.ApplicationSyntaxReference;
                if (syntaxReference == null) continue;
                var syntaxNode = syntaxReference.GetSyntax();
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

        void ExploreNamespace(INamespaceSymbol namespaceSymbol, GenerationContext context)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceSymbol subNamespace)
                {
                    ExploreNamespace(subNamespace, context);
                }
                else if (member is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Class)
                {
                    var members = member.GetMembers().ToArray();

                    if (typeSymbol.HasAttribute("CommandHandler"))
                    {
                        var ch = new CommandHandlerDescriptor();
                        ch.Name = typeSymbol.Name;
                        foreach (var i in members.OfType<IMethodSymbol>())
                        {
                            if (i.Name == "Handle" && i.Parameters.Length == 2)
                                ch.Handles.Add(new HandleDescriptor() { Method = i });
                        }
                        context.CommandHandlers.Add(ch);
                    }
                    else if (typeSymbol.HasAttribute("Aggregate"))
                    {
                        var agg = new AggregateDescriptor(typeSymbol);
                        agg.Name = typeSymbol.Name;

                        var st = typeSymbol.Interfaces.FirstOrDefault(x => x.Name == "IAggregateStateAccessor");
                        for (ITypeSymbol c = typeSymbol; c != null; c = c.BaseType)
                        {
                            var i = c.Interfaces.FirstOrDefault(x => x.Name.Contains("IAggregateStateAccessor"));
                            if (i != null)
                            {
                                agg.StateType = i.TypeArguments[0];
                                break;
                            }
                        }

                        foreach (var m in members)
                        {
                            if (!(m is IMethodSymbol i)) continue;

                            if (i.Name == "Given" && i.Parameters.Length == 2 && i.Parameters[1].Type.ToString() == "object")
                            {
                                var attrs = i.GetAcceptedTypes();
                                foreach (var e in attrs)
                                    agg.Givens.Add(new AggregateGivenDescriptor() { EventType = e });
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
}