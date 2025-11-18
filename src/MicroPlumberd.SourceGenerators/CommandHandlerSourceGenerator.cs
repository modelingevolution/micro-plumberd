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
    public class CommandHandlerSourceGenerator : IIncrementalGenerator
    {
        private const string CommandHandlerAttributeName = "CommandHandlerAttribute";
        private const string CommandHandlerAttributeShortName = "CommandHandler";

        // Diagnostic descriptors
        private static readonly DiagnosticDescriptor NoHandleMethodsRule = new DiagnosticDescriptor(
            id: "MPLUMB002",
            title: "No Handle methods found",
            messageFormat: "CommandHandler '{0}' has [CommandHandler] attribute but no public Handle methods were found. Add at least one public Handle(TId id, TCommand cmd) method.",
            category: "MicroPlumberd.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Filter classes with [CommandHandler] attribute and collect results
            var commandHandlerResults = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, _) => GetCommandHandlerResult(ctx))
                .Where(static result => result is not null);

            // Generate source and diagnostics
            context.RegisterSourceOutput(commandHandlerResults, (spc, result) =>
            {
                if (result is null) return;

                if (result.Value.Diagnostic is not null)
                {
                    spc.ReportDiagnostic(result.Value.Diagnostic);
                }

                if (result.Value.HandlerInfo is not null)
                {
                    GenerateCommandHandlerCode(spc, result.Value.HandlerInfo.Value);
                }
            });
        }

        private static bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl
                   && classDecl.AttributeLists.Count > 0;
        }

        private static CommandHandlerResult? GetCommandHandlerResult(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);

            if (classSymbol is null) return null;

            // Check if class has [CommandHandler] attribute
            bool hasCommandHandlerAttribute = classSymbol.GetAttributes()
                .Any(ad => ad.AttributeClass?.Name == CommandHandlerAttributeName ||
                          ad.AttributeClass?.Name == CommandHandlerAttributeShortName);

            if (!hasCommandHandlerAttribute) return null;

            // Find all public Handle methods with 2 parameters
            var handleMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "Handle" &&
                           m.ParameterList.Parameters.Count == 2 &&
                           m.Modifiers.Any(SyntaxKind.PublicKeyword))
                .ToImmutableArray();

            if (handleMethods.IsEmpty)
            {
                var diagnostic = Diagnostic.Create(
                    NoHandleMethodsRule,
                    classDecl.Identifier.GetLocation(),
                    classSymbol.Name);

                return new CommandHandlerResult(null, diagnostic);
            }

            // Extract command and id types
            var handlerArgs = handleMethods.Select(m => new HandlerMethodInfo(
                IdType: m.ParameterList.Parameters[0].Type!.ToString(),
                CommandType: m.ParameterList.Parameters[1].Type!.ToString(),
                ReturnType: m.ReturnType.ToString()
            )).ToImmutableArray();

            // Extract fault exception types
            var faultTypes = handleMethods
                .SelectMany(method => method.AttributeLists.SelectMany(attrList => attrList.Attributes))
                .Where(attribute => attribute.Name.ToString().Contains("ThrowsFaultException<"))
                .SelectMany(attribute =>
                {
                    if (attribute.Name is GenericNameSyntax genericName)
                    {
                        var arg = genericName.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                        return arg != null ? new[] { arg } : System.Array.Empty<string>();
                    }
                    return System.Array.Empty<string>();
                })
                .Distinct()
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

            var handlerInfo = new CommandHandlerInfo(
                ClassName: classSymbol.Name,
                Namespace: namespaceName,
                Methods: handlerArgs,
                FaultTypes: faultTypes,
                Usings: usings
            );

            return new CommandHandlerResult(handlerInfo, null);
        }

        private static void GenerateCommandHandlerCode(SourceProductionContext context, CommandHandlerInfo info)
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

            // Get unique command types and their handlers
            var commandTypes = info.Methods.Select(m => m.CommandType).Distinct().ToArray();
            var returnTypes = info.Methods
                .Select(m => GetGenericArgument(m.ReturnType))
                .Where(t => t != null && t != "Task")
                .Distinct()
                .ToArray();

            // Generate partial class
            var handlerInterfaces = info.Methods
                .Select(m => $"ICommandHandler<{m.IdType},{m.CommandType}>")
                .Distinct();

            sb.AppendLine($"partial class {info.ClassName} : IServiceTypeRegister, {string.Join(", ", handlerInterfaces)}");
            sb.AppendLine("{");

            // Generate ICommandHandler<TId, TCommand>.Execute methods
            var distinctMethods = info.Methods
                .GroupBy(m => (m.IdType, m.CommandType))
                .Select(g => g.First());

            foreach (var method in distinctMethods)
            {
                var hasResult = method.ReturnType.Contains("Task<") && !method.ReturnType.Contains("Task<object");

                if (hasResult)
                {
                    sb.AppendLine($"    async Task<object?> ICommandHandler<{method.IdType},{method.CommandType}>.Execute({method.IdType} id, {method.CommandType} cmd) => await this.Handle(id, cmd);");
                }
                else
                {
                    sb.AppendLine($"    async Task<object?> ICommandHandler<{method.IdType},{method.CommandType}>.Execute({method.IdType} id, {method.CommandType} cmd) {{ await this.Handle(id, cmd); return null; }}");
                }
            }
            sb.AppendLine();

            // Generate Execute dispatcher
            sb.AppendLine("    public async Task<object?> Execute(string id, object command) => command switch");
            sb.AppendLine("    {");
            foreach (var cmdType in commandTypes)
            {
                sb.AppendLine($"        {cmdType} c => await ((ICommandHandler<{cmdType}>)this).Execute(id, c),");
            }
            sb.AppendLine($"        _ => null");
            sb.AppendLine("    };");
            sb.AppendLine();

            // Generate RegisterHandlers method
            sb.AppendLine("    static IServiceCollection IServiceTypeRegister.RegisterHandlers(IServiceCollection services, bool scoped=true)");
            sb.AppendLine("    {");
            foreach (var cmdType in commandTypes)
            {
                sb.AppendLine($"        if(scoped) services.AddScoped<ICommandHandler<{cmdType}>, {info.ClassName}>();");
                sb.AppendLine($"        else services.AddSingleton<ICommandHandler<{cmdType}>, {info.ClassName}>();");
                sb.AppendLine($"        services.Decorate<ICommandHandler<{cmdType}>, CommandHandlerAttributeValidator<{cmdType}>>();");
            }
            sb.AppendLine("        return services;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate type arrays
            if (commandTypes.Any())
                sb.AppendLine($"    private static readonly System.Type[] _commandTypes = new[] {{ {string.Join(", ", commandTypes.Select(t => $"typeof({t})"))} }};");
            else
                sb.AppendLine($"    private static readonly System.Type[] _commandTypes = System.Array.Empty<System.Type>();");

            if (returnTypes.Any())
                sb.AppendLine($"    private static readonly System.Type[] _returnTypes = new[] {{ {string.Join(", ", returnTypes.Select(t => $"typeof({t})"))} }};");
            else
                sb.AppendLine($"    private static readonly System.Type[] _returnTypes = System.Array.Empty<System.Type>();");

            if (info.FaultTypes.Any())
                sb.AppendLine($"    private static readonly System.Type[] _faultTypes = new[] {{ {string.Join(", ", info.FaultTypes.Select(t => $"typeof({t})"))} }};");
            else
                sb.AppendLine($"    private static readonly System.Type[] _faultTypes = System.Array.Empty<System.Type>();");

            sb.AppendLine();
            sb.AppendLine("    static IEnumerable<System.Type> IServiceTypeRegister.CommandTypes => _commandTypes;");
            sb.AppendLine("    static IEnumerable<System.Type> IServiceTypeRegister.ReturnTypes => _returnTypes;");
            sb.AppendLine("    static IEnumerable<System.Type> IServiceTypeRegister.FaultTypes => _faultTypes;");

            sb.AppendLine("}");

            // Add source to compilation
            var source = SourceText.From(sb.ToString(), Encoding.UTF8);
            context.AddSource($"{info.ClassName}_CommandHandler.g.cs", source);
        }

        private static string GetGenericArgument(string genericType)
        {
            try
            {
                var start = genericType.IndexOf('<') + 1;
                var length = genericType.IndexOf('>') - start;
                return start > 0 && length > 0 ? genericType.Substring(start, length) : genericType;
            }
            catch
            {
                return null;
            }
        }

        private record struct HandlerMethodInfo(
            string IdType,
            string CommandType,
            string ReturnType
        );

        private record struct CommandHandlerInfo(
            string ClassName,
            string Namespace,
            ImmutableArray<HandlerMethodInfo> Methods,
            ImmutableArray<string> FaultTypes,
            ImmutableArray<string> Usings
        );

        private record struct CommandHandlerResult(
            CommandHandlerInfo? HandlerInfo,
            Diagnostic? Diagnostic
        );
    }
}
