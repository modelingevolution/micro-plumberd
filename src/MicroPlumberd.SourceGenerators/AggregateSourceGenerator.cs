using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MicroPlumberd.SourceGenerators
{
    [Generator]
    public class AggregateSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required for this example
            //System.Diagnostics.Debugger.Launch();
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var syntaxTrees = context.Compilation.SyntaxTrees;

            foreach (var syntaxTree in syntaxTrees)
            {
                var usingDirectives = syntaxTree.GetRoot().DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(u => u.ToString())
                    .Distinct()
                    .ToList();
                var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                var classDeclarations = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (var classDecl in classDeclarations)
                {
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                    if (classSymbol == null) continue;


                    var baseType = classDecl.BaseList?.Types
                        .Select(bt => bt.Type)
                        .OfType<GenericNameSyntax>()
                        .FirstOrDefault(gn => gn.Identifier.ValueText.Contains("AggregateBase"));

                    if (baseType == null) continue;
                    var stateClassName = baseType.TypeArgumentList.Arguments.FirstOrDefault();
                    if (stateClassName == null) continue;

                    if (classSymbol.GetAttributes().Any(ad => ad.AttributeClass.Name == "AggregateAttribute" || ad.AttributeClass.Name == "Aggregate"))
                    {
                        var namespaceDecl = classDecl.Parent as NamespaceDeclarationSyntax;
                        var fileScopedNamespace = classDecl.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                        var namespaceName = namespaceDecl?.Name.ToString() ?? fileScopedNamespace?.Name.ToString() ?? string.Empty;
                        var className = classDecl.Identifier.ValueText;
                        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                            .Where(m => m.Identifier.ValueText == "Given" &&
                                        m.ParameterList.Parameters.Count == 2 &&
                                        m.ParameterList.Parameters[1].Type.ToString() != "object" )
                            .ToList();

                        var sb = new StringBuilder();
                        var givenTypes = methods.Select(x => x.ParameterList.Parameters[1].Type).ToArray();
                        // Add using directives
                        foreach (var usingDirective in usingDirectives)
                        {
                            sb.AppendLine(usingDirective);
                        }
                        sb.AppendLine(); // Add a line break after using directives

                        if (!string.IsNullOrEmpty(namespaceName))
                        {
                            sb.AppendLine($"namespace {namespaceName};");
                        }

                        
                        sb.AppendLine($"partial class {className} : IAggregate<{className}>, ITypeRegister ");
                        sb.AppendLine("{");

                        //[AcceptedType(typeof(FooUpdated)), AcceptedType(typeof(FooCategory))]
                        var attrs = givenTypes.Select(x => $"AcceptedType(typeof({x.ToString()}))");
                        sb.AppendLine($"[{string.Join(", ",attrs) }]");
                        sb.AppendLine($"    protected override {stateClassName} Given({stateClassName} state, object ev)");
                        sb.AppendLine("    {");
                        sb.AppendLine("        switch(ev)");
                        sb.AppendLine("        {");

                        foreach (var eventType in givenTypes)
                        {
                            sb.AppendLine($"            case {eventType.ToString()} e: return Given(state, e);");
                        }

                        sb.AppendLine("            default: return state;");
                        
                        sb.AppendLine("        };");
                        sb.AppendLine("    }");

                        sb.AppendLine($"   public static {className} New(Guid id) => new {className}(id);");


                        var typeOfs = methods.Select(x => x.ParameterList.Parameters[1].Type)
                            .Select(x => $"typeof({x})");
                        var events = string.Join(",", typeOfs);
                        sb.AppendLine($"    static IEnumerable<Type> ITypeRegister.Types => [{events}];");

                        
                        sb.AppendLine("}");
                        context.AddSource($"{className}_Aggregate.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
                    }
                }
            }
        }
    }
}