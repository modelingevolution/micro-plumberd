using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MicroPlumberd.SourceGenerators
{
    [Generator]
    public class CommandHandlerSourceGenerator : ISourceGenerator
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
                    

                    if (classSymbol.GetAttributes().Any(ad => ad.AttributeClass.Name == "CommandHandlerAttribute" || ad.AttributeClass.Name == "CommandHandler"))
                    {
                        var namespaceDecl = classDecl.Parent as NamespaceDeclarationSyntax;
                        var fileScopedNamespace = classDecl.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                        var namespaceName = namespaceDecl?.Name.ToString() ?? fileScopedNamespace?.Name.ToString() ?? string.Empty;
                        var className = classDecl.Identifier.ValueText;
                        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                            .Where(m => m.Identifier.ValueText == "Handle" &&
                                        m.ParameterList.Parameters.Count == 2 &&
                                        m.ParameterList.Parameters[0].Type.ToString() == "Guid" &&
                                        m.Modifiers.Any(SyntaxKind.PublicKeyword))
                            .ToList();


                        var errorMsg = methods.SelectMany(method => method.AttributeLists.SelectMany(attrList => attrList.Attributes))
                            .Where(attribute => attribute.Name.ToString().Contains("ThrowsFaultException<") )
                            .SelectMany(attribute =>
                            {
                                var genericName = attribute.Name as GenericNameSyntax;
                                return genericName != null ?
                                    new[] { genericName.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() } :
                                    new string[] { };
                            })
                            .Where(typeName => typeName != null)
                            .Distinct()
                            .ToArray();

                        var sb = new StringBuilder();

                        // Add using directives
                        foreach (var usingDirective in usingDirectives) sb.AppendLine(usingDirective);
                        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
                        sb.AppendLine("using MicroPlumberd.Services;");
                        //sb.AppendLine("using MicroPlumberd.DirectConnect;");

                        sb.AppendLine(); // Add a line break after using directives

                        if (!string.IsNullOrEmpty(namespaceName)) sb.AppendLine($"namespace {namespaceName};");

                        
                        var commands =  methods.Select(x => x.ParameterList.Parameters[1].Type.ToString()).ToArray();
                        var handerArgs = methods.Select(x => new
                        {
                            CommandType=x.ParameterList.Parameters[1].Type.ToString(),
                            IdType = x.ParameterList.Parameters[0].Type.ToString(),
                        }).ToArray();
                        var returnTypes = methods.Select(x => GetGenericArgument(x.ReturnType.ToString()))
                            .Where(x=>x!= null && x != "Task")
                            .Distinct()
                            .ToArray();

                        var args = methods.Select(x =>
                            {
                                var idType = x.ParameterList.Parameters[0].Type.ToString();
                                var cmdType = x.ParameterList.Parameters[1].Type.ToString();
                                if (x.ReturnType is GenericNameSyntax gn)
                                {
                                    var resultType = gn.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                                    return (cmdType: cmdType, resultType, idType);
                                }
                                else if (x.ReturnType.ToString() == "Task")
                                {
                                    return (cmdType: cmdType, null, idType);
                                }
                                else return (null,null, idType);

                            })
                            .Where(x => x.cmdType != null)
                            .Distinct()
                            .ToArray();

                        sb.AppendLine($"partial class {className} : IServiceTypeRegister, {string.Join(", ", handerArgs.Select(x=> $"ICommandHandler<{x.IdType},{x.CommandType}>"))} ");
                        sb.AppendLine("{");
                        foreach(var a in args) { 
                            if(a.resultType != null)
                                sb.AppendLine($"    async Task<object> ICommandHandler<{a.idType},{a.cmdType}>.Execute({a.idType} id, {a.cmdType} cmd) => await this.Handle(id, cmd);");
                            else
                                sb.AppendLine($"    async Task<object> ICommandHandler<{a.idType},{a.cmdType}>.Execute({a.idType} id, {a.cmdType} cmd) {{ await this.Handle(id, cmd); return MicroPlumberd.DirectConnect.HandlerOperationStatus.Ok(); }}");
                        }



                        sb.AppendLine("    public async Task<object?> Execute(string id, object command) => command switch");
                        sb.AppendLine("    {");
                        foreach (var command in commands)
                        {
                            sb.AppendLine($"        {command} c => await ((ICommandHandler<{command}>)this).Execute(id, c),");
                        }
                        sb.AppendLine($"        _ => null");
                        sb.AppendLine("    };");

                        sb.AppendLine("    static IServiceCollection IServiceTypeRegister.RegisterHandlers(IServiceCollection services)");
                        sb.AppendLine("    {");
                        foreach (var command in commands)
                        {
                            //.Decorate<ICommandHandler<CreateFoo>, CommandHandlerAttributeValidator<CreateFoo>>()
                            sb.AppendLine($"        services.AddScoped<ICommandHandler<{command}>, {className}>();");
                            sb.AppendLine($"        services.Decorate<ICommandHandler<{command}>, CommandHandlerAttributeValidator<{command}>>();");
                        }
                        sb.AppendLine("    return services;");
                        sb.AppendLine("    }");

                        if(commands.Any())
                            sb.AppendLine($"   private static readonly Type[] _commandTypes = new[] {{ {string.Join(",", commands.Select(x=>$"typeof({x})")) } }};");
                        else
                            sb.AppendLine($"   private static readonly Type[] _commandTypes = Array.Empty<Type>();");


                        if (returnTypes.Any())
                            sb.AppendLine($"   private static readonly Type[] _returnTypes = new[] {{ {string.Join(",", returnTypes.Select(x => $"typeof({x})"))} }};");
                        else
                            sb.AppendLine($"   private static readonly Type[] _returnTypes = Array.Empty<Type>();");
                        
                        if(errorMsg.Any())
                            sb.AppendLine($"   private static readonly Type[] _faultTypes = new[] {{ {string.Join(",", errorMsg.Select(x => $"typeof({x})"))} }};");
                        else
                            sb.AppendLine($"   private static readonly Type[] _faultTypes = Array.Empty<Type>();");

                        sb.AppendLine("    static IEnumerable<Type> IServiceTypeRegister.CommandTypes => _commandTypes;");
                        sb.AppendLine("    static IEnumerable<Type> IServiceTypeRegister.ReturnTypes => _returnTypes;");
                        sb.AppendLine("    static IEnumerable<Type> IServiceTypeRegister.FaultTypes => _faultTypes;");

                        sb.AppendLine("}");
                        context.AddSource($"{className}_CommandHandler.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
                    }
                }
            }
        }
        private string GetGenericArgument(string genericType)
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
    }
}