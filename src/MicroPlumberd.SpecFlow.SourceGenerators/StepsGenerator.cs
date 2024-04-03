using Microsoft.CodeAnalysis;
using System;
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
}
