using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            
            foreach (var reference in compilation.ExternalReferences)
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;

                stringBuilder.AppendLine("// " + assemblySymbol.Identity);
                if (assemblySymbol.Identity.ToString().Contains(dslAssemblyFilter))
                {

                    if (assemblySymbol != null)
                    {
                        // Now, with assemblySymbol, you can explore its namespaces and types
                        ExploreNamespace(assemblySymbol.GlobalNamespace, stringBuilder);
                    }
                }

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

        void ExploreNamespace(INamespaceSymbol namespaceSymbol, StringBuilder stringBuilder)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceSymbol subNamespace)
                {
                    ExploreNamespace(subNamespace, stringBuilder);
                }
                else if (member is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Class)
                {
                    var attrs = typeSymbol.GetAttributes();
                    var hasAttribute = attrs.Any(attr => attr.AttributeClass?.Name.Contains("CommandHandler") ?? false);
                    if (hasAttribute)
                    {
                        stringBuilder.AppendLine($"// Found class: {typeSymbol.ToDisplayString()}");
                        foreach (var i in member.GetMembers().OfType<IMethodSymbol>())
                        {

                        }
                    }
                }
            }
        }
    }
}
