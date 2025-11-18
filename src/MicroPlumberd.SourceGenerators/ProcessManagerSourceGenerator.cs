using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MicroPlumberd.SourceGenerators
{
    [Generator]
    public class ProcessManagerSourceGenerator : IIncrementalGenerator
    {
        private const string ProcessManagerAttributeName = "ProcessManagerAttribute";
        private const string ProcessManagerAttributeShortName = "ProcessManager";

        // Diagnostic descriptors
        private static readonly DiagnosticDescriptor NoWhenMethodsRule = new DiagnosticDescriptor(
            id: "MPLUMB004",
            title: "No When methods found",
            messageFormat: "ProcessManager '{0}' has [ProcessManager] attribute but no When methods were found. Add at least one When(Metadata m, TEvent evt) method.",
            category: "MicroPlumberd.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Filter classes with [ProcessManager] attribute and collect results
            var processManagerResults = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, _) => GetProcessManagerResult(ctx))
                .Where(static result => result is not null);

            // Generate source and diagnostics
            context.RegisterSourceOutput(processManagerResults, (spc, result) =>
            {
                if (result is null) return;

                if (result.Value.Diagnostic is not null)
                {
                    spc.ReportDiagnostic(result.Value.Diagnostic);
                }

                if (result.Value.ManagerInfo is not null)
                {
                    GenerateProcessManagerCode(spc, result.Value.ManagerInfo.Value);
                }
            });
        }

        private static bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl
                   && classDecl.AttributeLists.Count > 0;
        }

        private static ProcessManagerResult? GetProcessManagerResult(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);

            if (classSymbol is null) return null;

            // Check if class has [ProcessManager] attribute
            bool hasProcessManagerAttribute = classSymbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.Name == ProcessManagerAttributeName ||
                          ad.AttributeClass?.Name == ProcessManagerAttributeShortName);

            if (!hasProcessManagerAttribute) return null;

            // Find When methods (command generation)
            var whenMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "When" &&
                           m.ParameterList.Parameters.Count == 2 &&
                           m.ParameterList.Parameters[0].Type?.ToString() == "Metadata")
                .ToImmutableArray();

            // Find Given methods (state updates)
            var givenMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "Given" &&
                           m.ParameterList.Parameters.Count == 2 &&
                           m.ParameterList.Parameters[0].Type?.ToString() == "Metadata")
                .ToImmutableArray();

            // Find StartWhen method (optional)
            var startMethod = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == "StartWhen" &&
                                    m.ParameterList.Parameters.Count == 2 &&
                                    m.ParameterList.Parameters[0].Type?.ToString() == "Metadata");

            if (whenMethods.IsEmpty && givenMethods.IsEmpty && startMethod is null)
            {
                var diagnostic = Diagnostic.Create(
                    NoWhenMethodsRule,
                    classDecl.Identifier.GetLocation(),
                    classSymbol.Name);

                return new ProcessManagerResult(null, diagnostic);
            }

            // Collect all event handling methods
            var allMethods = startMethod != null
                ? whenMethods.Union(givenMethods).Append(startMethod).ToImmutableArray()
                : whenMethods.Union(givenMethods).ToImmutableArray();

            var eventTypes = allMethods
                .Select(m => m.ParameterList.Parameters[1].Type!.GetFriendlyTypeName())
                .Where(t => t != "object")
                .Distinct()
                .ToImmutableArray();

            // Extract command types from When methods
            var commandTypes = whenMethods
                .Where(m => m.ReturnType is GenericNameSyntax gn && gn.Identifier.ValueText == "Task")
                .Select(m => m.ReturnType)
                .OfType<GenericNameSyntax>()
                .Select(gn => gn.TypeArgumentList.Arguments[0])
                .OfType<GenericNameSyntax>()
                .Where(gn => gn.Identifier.ValueText == "ICommandRequest")
                .Select(gn => gn.TypeArgumentList.Arguments[0].GetFriendlyTypeName())
                .Distinct()
                .ToImmutableArray();

            var whenEventTypes = whenMethods
                .Select(m => m.ParameterList.Parameters[1].Type!.GetFriendlyTypeName())
                .Distinct()
                .ToImmutableArray();

            var givenEventTypes = givenMethods
                .Select(m => m.ParameterList.Parameters[1].Type!.GetFriendlyTypeName())
                .Distinct()
                .ToImmutableArray();

            string? startEventType = startMethod != null
                ? startMethod.ParameterList.Parameters[1].Type!.GetFriendlyTypeName()
                : null;

            // Get namespace
            var namespaceName = classSymbol.ContainingNamespace?.IsGlobalNamespace == false
                ? classSymbol.ContainingNamespace.ToDisplayString()
                : string.Empty;

            // Collect using directives
            var usings = classDecl.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .Distinct()
                .ToImmutableArray();

            var managerInfo = new ProcessManagerInfo(
                ClassName: classSymbol.Name,
                Namespace: namespaceName,
                EventTypes: eventTypes,
                CommandTypes: commandTypes,
                WhenEventTypes: whenEventTypes,
                GivenEventTypes: givenEventTypes,
                StartEventType: startEventType,
                Usings: usings
            );

            return new ProcessManagerResult(managerInfo, null);
        }

        private static void GenerateProcessManagerCode(SourceProductionContext context, ProcessManagerInfo info)
        {
            var sb = new StringBuilder();

            // Add using directives
            foreach (var usingDirective in info.Usings)
            {
                sb.AppendLine(usingDirective);
            }
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using MicroPlumberd.Services;");
            sb.AppendLine();

            // Add namespace
            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace};");
                sb.AppendLine();
            }

            // Generate partial class
            sb.AppendLine($"partial class {info.ClassName} : ProcessManagerBase<Guid>, IProcessManager, ITypeRegister");
            sb.AppendLine("{");

            // Generate CommandTypes property
            sb.AppendLine("    static IEnumerable<System.Type> IProcessManager.CommandTypes");
            sb.AppendLine("    {");
            sb.AppendLine("        get");
            sb.AppendLine("        {");
            foreach (var cmdType in info.CommandTypes)
            {
                sb.AppendLine($"            yield return typeof(CommandEnqueued<{cmdType}>);");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate StartEvent and StartWhen if present
            if (info.StartEventType != null)
            {
                sb.AppendLine($"    static System.Type IProcessManager.StartEvent => typeof({info.StartEventType});");
                sb.AppendLine($"    async Task<ICommandRequest> IProcessManager.StartWhen(Metadata m, object evt) => await StartWhen(m, ({info.StartEventType})evt);");
                sb.AppendLine();
            }

            // Generate ITypeRegister.Types
            var typeOfs = info.EventTypes.Select(t => $"typeof({t})");
            sb.AppendLine($"    static IEnumerable<System.Type> ITypeRegister.Types => [{string.Join(", ", typeOfs)}];");
            sb.AppendLine();

            // Generate IEventHandler.Handle (Given dispatcher)
            sb.AppendLine("    async Task IEventHandler.Handle(Metadata m, object evt)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch(evt)");
            sb.AppendLine("        {");
            foreach (var eventType in info.GivenEventTypes)
            {
                sb.AppendLine($"            case {eventType} e: await Given(m, e); return;");
            }
            sb.AppendLine("            default: return;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate IProcessManager.When (command generation dispatcher)
            sb.AppendLine("    async Task<ICommandRequest?> IProcessManager.When(Metadata m, object evt)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch(evt)");
            sb.AppendLine("        {");
            foreach (var eventType in info.WhenEventTypes)
            {
                sb.AppendLine($"            case {eventType} e: return await When(m, e);");
            }
            sb.AppendLine("            default: return null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            // Add source to compilation
            var source = SourceText.From(sb.ToString(), Encoding.UTF8);
            context.AddSource($"{info.ClassName}_ProcessManager.g.cs", source);
        }

        private record struct ProcessManagerInfo(
            string ClassName,
            string Namespace,
            ImmutableArray<string> EventTypes,
            ImmutableArray<string> CommandTypes,
            ImmutableArray<string> WhenEventTypes,
            ImmutableArray<string> GivenEventTypes,
            string? StartEventType,
            ImmutableArray<string> Usings
        );

        private record struct ProcessManagerResult(
            ProcessManagerInfo? ManagerInfo,
            Diagnostic? Diagnostic
        );
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
