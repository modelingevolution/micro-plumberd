using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MicroPlumberd.SourceGenerators
{
    [Generator]
    public class ProcessManagerSourceGenerator : ISourceGenerator
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
                    

                    if (classSymbol.GetAttributes().Any(ad => ad.AttributeClass.Name == "ProcessManagerAttribute" || ad.AttributeClass.Name == "ProcessManager"))
                    {
                        var namespaceDecl = classDecl.Parent as NamespaceDeclarationSyntax;
                        var fileScopedNamespace = classDecl.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                        var namespaceName = namespaceDecl?.Name.ToString() ?? fileScopedNamespace?.Name.ToString() ?? string.Empty;
                        var className = classDecl.Identifier.ValueText;
                        var whenMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                            .Where(m => m.Identifier.ValueText == "When" &&
                                        m.ParameterList.Parameters.Count == 2 &&
                                        m.ParameterList.Parameters[0].Type.ToString() == "Metadata" &&
                                        m.ParameterList.Parameters.Count == 2)
                            .ToList();

                        var givenMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                            .Where(m => m.Identifier.ValueText == "Given" &&
                                        m.ParameterList.Parameters.Count == 2 &&
                                        m.ParameterList.Parameters[0].Type.ToString() == "Metadata" &&
                                        m.ParameterList.Parameters.Count == 2)
                            .ToList();

                        var startMethod = classDecl.Members
                            .OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.ValueText == "StartWhen" &&
                                                 m.ParameterList.Parameters.Count == 2 &&
                                                 m.ParameterList.Parameters[0].Type.ToString() == "Metadata" &&
                                                 m.ParameterList.Parameters.Count == 2);

                        var sb = new StringBuilder();

                        // Add using directives
                        foreach (var usingDirective in usingDirectives) sb.AppendLine(usingDirective);
                        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
                        sb.AppendLine("using MicroPlumberd.Services;");
                        sb.AppendLine(); // Add a line break after using directives

                        if (!string.IsNullOrEmpty(namespaceName)) sb.AppendLine($"namespace {namespaceName};");

                        var ehMethods = startMethod != null ? 
                            whenMethods.Union(givenMethods).Union(new []{ startMethod}) : 
                            whenMethods.Union(givenMethods);

                        var events = ehMethods
                            .Select(x => x.ParameterList.Parameters[1].Type.GetFriendlyTypeName())
                            .Where(x=>x!= "object")
                            .Distinct()
                            .ToArray();

                        var returnTypes = ehMethods
                            .Select(x=>x.ReturnType)
                            .OfType<GenericNameSyntax>()
                            .Where(x=>x.Identifier.ValueText == "Task")
                            .Select(x => x.TypeArgumentList.Arguments[0])
                            .Distinct()
                            .ToArray();

                        var commandTypes = returnTypes
                            .OfType<GenericNameSyntax>()
                            .Where(x=>x.Identifier.ValueText == "ICommandRequest")
                            .Select(x => x.TypeArgumentList.Arguments[0].GetFriendlyTypeName())
                            .Distinct()
                            .ToArray();

                        sb.AppendLine($"partial class {className} : ProcessManagerBase<Guid>, IProcessManager, ITypeRegister");
                        sb.AppendLine("{");

                        sb.AppendLine("static IEnumerable<Type> IProcessManager.CommandTypes");
                        sb.AppendLine("{");
                        sb.AppendLine("    get");
                        sb.AppendLine("    {");
                        foreach (var i in commandTypes)
                        {
                            sb.AppendLine($"        yield return typeof(CommandEnqueued<{i}>);");
                        }
                        sb.AppendLine("    }");
                        sb.AppendLine("}");

                        if (startMethod != null)
                        {
                            var startEvt = startMethod.ParameterList.Parameters[1].Type.GetFriendlyTypeName();
                            sb.AppendLine($"static Type IProcessManager.StartEvent => typeof({startEvt});");
                            sb.AppendLine($"async Task<ICommandRequest> IProcessManager.StartWhen(Metadata m, object evt) => await StartWhen(m, ({startEvt})evt);");
                        }

                        sb.AppendLine($"    static IEnumerable<Type> ITypeRegister.Types => [{string.Join(",", events.Select(x=> $"typeof({x})"))}];");

                        sb.AppendLine("async Task IEventHandler.Handle(Metadata m, object evt)");
                        sb.AppendLine("{");
                        sb.AppendLine("    switch(evt)");
                        sb.AppendLine("    {");
                        foreach (var i in givenMethods.Select(x => x.ParameterList.Parameters[1].Type.GetFriendlyTypeName())) 
                            sb.AppendLine($"        case {i} e: await Given(m, e); return;");
                        sb.AppendLine($"        default: return;");
                        sb.AppendLine("    }");
                        sb.AppendLine("}");

                        sb.AppendLine("async Task<ICommandRequest?> IProcessManager.When(Metadata m, object evt)");
                        sb.AppendLine("{");
                        sb.AppendLine("    switch(evt)");
                        sb.AppendLine("    {");
                        foreach (var i in whenMethods.Select(x => x.ParameterList.Parameters[1].Type.GetFriendlyTypeName()))
                            sb.AppendLine($"        case {i} e: return await When(m, e);");
                        sb.AppendLine($"        default: return null;");
                        sb.AppendLine("    }");
                        sb.AppendLine("}");


                        sb.AppendLine("}");
                        context.AddSource($"{className}_ProcessManager.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
                    }
                }
            }
        }

        
        
    }
    public static class TypeSyntaxExtensions
    {
        public static string GetFriendlyTypeName(this TypeSyntax typeSyntax)
        {
            // Check if the type is a generic type
            if (typeSyntax is GenericNameSyntax genericName)
            {
                // Retrieve the generic type identifier (e.g., "Vector")
                string identifier = genericName.Identifier.ValueText;

                // Retrieve the type arguments (e.g., "<int>")
                string typeArguments = string.Join(", ", genericName.TypeArgumentList.Arguments.Select(arg => arg.ToString()));

                // Construct and return the friendly generic type name (e.g., "Vector<int>")
                return $"{identifier}<{typeArguments}>";
            }

            // If not a generic type, just return the type syntax's text
            return typeSyntax.ToString();
        }
    }
}