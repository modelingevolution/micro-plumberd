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
    public class EventHandlerSourceGenerator : IIncrementalGenerator
    {
        private const string EventHandlerAttributeName = "EventHandlerAttribute";
        private const string EventHandlerAttributeShortName = "EventHandler";

        // Diagnostic descriptors
        private static readonly DiagnosticDescriptor NoGivenMethodsRule = new DiagnosticDescriptor(
            id: "MPLUMB003",
            title: "No Given methods found",
            messageFormat: "EventHandler '{0}' has [EventHandler] attribute but no private Given methods were found. Add at least one private Given(Metadata m, TEvent evt) method.",
            category: "MicroPlumberd.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Filter classes with [EventHandler] attribute and collect results
            var eventHandlerResults = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, _) => GetEventHandlerResult(ctx))
                .Where(static result => result is not null);

            // Generate source and diagnostics
            context.RegisterSourceOutput(eventHandlerResults, (spc, result) =>
            {
                if (result is null) return;

                if (result.Value.Diagnostic is not null)
                {
                    spc.ReportDiagnostic(result.Value.Diagnostic);
                }

                if (result.Value.HandlerInfo is not null)
                {
                    GenerateEventHandlerCode(spc, result.Value.HandlerInfo.Value);
                }
            });
        }

        private static bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl
                   && classDecl.AttributeLists.Count > 0;
        }

        private static EventHandlerResult? GetEventHandlerResult(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);

            if (classSymbol is null) return null;

            // Check if class has [EventHandler] attribute
            bool hasEventHandlerAttribute = classSymbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.Name == EventHandlerAttributeName ||
                          ad.AttributeClass?.Name == EventHandlerAttributeShortName);

            if (!hasEventHandlerAttribute) return null;

            // Find all private Given methods with 2 parameters (Metadata, TEvent)
            var givenMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "Given" &&
                           m.ParameterList.Parameters.Count == 2 &&
                           m.ParameterList.Parameters[0].Type?.ToString() == "Metadata" &&
                           m.Modifiers.Any(SyntaxKind.PrivateKeyword))
                .ToImmutableArray();

            if (givenMethods.IsEmpty)
            {
                var diagnostic = Diagnostic.Create(
                    NoGivenMethodsRule,
                    classDecl.Identifier.GetLocation(),
                    classSymbol.Name);

                return new EventHandlerResult(null, diagnostic);
            }

            var eventTypes = givenMethods
                .Select(m => m.ParameterList.Parameters[1].Type!.ToString())
                .ToImmutableArray();

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

            var handlerInfo = new EventHandlerInfo(
                ClassName: classSymbol.Name,
                Namespace: namespaceName,
                EventTypes: eventTypes,
                Usings: usings
            );

            return new EventHandlerResult(handlerInfo, null);
        }

        private static void GenerateEventHandlerCode(SourceProductionContext context, EventHandlerInfo info)
        {
            var sb = new StringBuilder();

            // Add using directives
            foreach (var usingDirective in info.Usings)
            {
                sb.AppendLine(usingDirective);
            }
            sb.AppendLine();

            // Add namespace
            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace};");
                sb.AppendLine();
            }

            // Generate partial class
            sb.AppendLine($"partial class {info.ClassName} : IEventHandler, ITypeRegister");
            sb.AppendLine("{");

            // Generate Handle dispatcher
            sb.AppendLine("    Task IEventHandler.Handle(Metadata m, object ev) => Given(m, ev);");
            sb.AppendLine();
            sb.AppendLine("    public async Task Given(Metadata m, object ev)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (ev)");
            sb.AppendLine("        {");

            foreach (var eventType in info.EventTypes)
            {
                sb.AppendLine($"            case {eventType} e: await Given(m, e); break;");
            }

            sb.AppendLine("            default:");
            sb.AppendLine("                throw new ArgumentException(\"Unknown event type\", ev.GetType().Name);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate ITypeRegister implementation
            var typeOfs = info.EventTypes.Select(t => $"typeof({t})");
            sb.AppendLine($"    static IEnumerable<System.Type> ITypeRegister.Types => [{string.Join(", ", typeOfs)}];");

            sb.AppendLine("}");

            // Add source to compilation
            var source = SourceText.From(sb.ToString(), Encoding.UTF8);
            context.AddSource($"{info.ClassName}_EventHandler.g.cs", source);
        }

        private record struct EventHandlerInfo(
            string ClassName,
            string Namespace,
            ImmutableArray<string> EventTypes,
            ImmutableArray<string> Usings
        );

        private record struct EventHandlerResult(
            EventHandlerInfo? HandlerInfo,
            Diagnostic? Diagnostic
        );
    }
}
