using System.Collections.Generic;
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
    public class AggregateSourceGenerator : IIncrementalGenerator
    {
        private const string AggregateAttributeName = "AggregateAttribute";
        private const string AggregateAttributeShortName = "Aggregate";

        // Diagnostic descriptors
        private static readonly DiagnosticDescriptor NoGivenMethodsRule = new DiagnosticDescriptor(
            id: "MPLUMB001",
            title: "No Given methods found",
            messageFormat: "Aggregate '{0}' has [Aggregate] attribute but no Given methods were found. Add at least one Given(TState state, TEvent evt) method.",
            category: "MicroPlumberd.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Filter classes with [Aggregate] attribute and collect results
            var aggregateResults = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, _) => GetAggregateResult(ctx))
                .Where(static result => result is not null);

            // Generate source and diagnostics
            context.RegisterSourceOutput(aggregateResults, (spc, result) =>
            {
                if (result is null) return;

                if (result.Value.Diagnostic is not null)
                {
                    spc.ReportDiagnostic(result.Value.Diagnostic);
                }

                if (result.Value.AggregateInfo is not null)
                {
                    GenerateAggregateCode(spc, result.Value.AggregateInfo.Value);
                }
            });
        }

        private static bool IsCandidateClass(SyntaxNode node)
        {
            // Quick syntactic check: is it a class with attributes?
            return node is ClassDeclarationSyntax classDecl
                   && classDecl.AttributeLists.Count > 0;
        }

        private static AggregateResult? GetAggregateResult(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);

            if (classSymbol is null) return null;

            // Check if class has [Aggregate] attribute
            bool hasAggregateAttribute = classSymbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.Name == AggregateAttributeName ||
                          ad.AttributeClass?.Name == AggregateAttributeShortName);

            if (!hasAggregateAttribute) return null;

            // Find base type AggregateBase<TId, TState>
            var baseType = classDecl.BaseList?.Types
                .Select(bt => bt.Type)
                .OfType<GenericNameSyntax>()
                .FirstOrDefault(gn => gn.Identifier.ValueText.Contains("AggregateBase"));

            if (baseType is null) return null;

            var typeArgs = baseType.TypeArgumentList.Arguments;
            if (typeArgs.Count < 2) return null;

            var idType = typeArgs[0].ToString();
            var stateType = typeArgs[typeArgs.Count - 1].ToString();

            // Find all Given methods with 2 parameters
            var givenMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "Given" &&
                           m.ParameterList.Parameters.Count == 2 &&
                           m.ParameterList.Parameters[1].Type?.ToString() != "object")
                .ToImmutableArray();

            if (givenMethods.IsEmpty)
            {
                // Report diagnostic for missing Given methods
                var diagnostic = Diagnostic.Create(
                    NoGivenMethodsRule,
                    classDecl.Identifier.GetLocation(),
                    classSymbol.Name);

                return new AggregateResult(null, diagnostic);
            }

            var eventTypes = givenMethods
                .Select(m => m.ParameterList.Parameters[1].Type!.ToString())
                .ToImmutableArray();

            // Get namespace
            var namespaceName = classSymbol.ContainingNamespace?.IsGlobalNamespace == false
                ? classSymbol.ContainingNamespace.ToDisplayString()
                : string.Empty;

            // Collect using directives from syntax tree
            var usings = classDecl.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .Distinct()
                .ToImmutableArray();

            var aggregateInfo = new AggregateInfo(
                ClassName: classSymbol.Name,
                Namespace: namespaceName,
                IdType: idType,
                StateType: stateType,
                EventTypes: eventTypes,
                Usings: usings
            );

            return new AggregateResult(aggregateInfo, null);
        }

        private static void GenerateAggregateCode(SourceProductionContext context, AggregateInfo info)
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
            sb.AppendLine($"partial class {info.ClassName} : IAggregate<{info.ClassName}>, ITypeRegister");
            sb.AppendLine("{");

            // Add AcceptedType attributes
            var attrs = info.EventTypes.Select(t => $"AcceptedType(typeof({t}))");
            sb.AppendLine($"    [{string.Join(", ", attrs)}]");

            // Generate Given dispatcher
            sb.AppendLine($"    protected override {info.StateType} Given({info.StateType} state, object ev)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch(ev)");
            sb.AppendLine("        {");

            foreach (var eventType in info.EventTypes)
            {
                sb.AppendLine($"            case {eventType} e: return Given(state, e);");
            }

            sb.AppendLine("            default: return state;");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate Empty factory method
            sb.AppendLine($"    public static {info.ClassName} Empty(object id) => new {info.ClassName}(({info.IdType})id);");
            sb.AppendLine();

            // Generate ITypeRegister implementation
            var typeOfs = info.EventTypes.Select(t => $"typeof({t})");
            sb.AppendLine($"    static IEnumerable<Type> ITypeRegister.Types => [{string.Join(", ", typeOfs)}];");

            sb.AppendLine("}");

            // Add source to compilation
            var source = SourceText.From(sb.ToString(), Encoding.UTF8);
            context.AddSource($"{info.ClassName}_Aggregate.g.cs", source);
        }

        private record struct AggregateInfo(
            string ClassName,
            string Namespace,
            string IdType,
            string StateType,
            ImmutableArray<string> EventTypes,
            ImmutableArray<string> Usings
        );

        private record struct AggregateResult(
            AggregateInfo? AggregateInfo,
            Diagnostic? Diagnostic
        );
    }
}
