using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.SourceGenerators.Tests;

/// <summary>
/// The generated code must compile clean in a consumer that builds with Nullable=enable and warnings as errors
/// (the ModelingEvolution simulator: CS0105, CS8669 and CS1066 from the [CommandHandler] output of 1.3.4).
/// Each fixture is compiled against the real MicroPlumberd assemblies, so the whole compilation (user code plus
/// generated code) must carry no diagnostic of severity warning or above. The control in the same test plants a
/// duplicate using (CS0105) in the user source and requires the same harness to report it.
/// </summary>
public class GeneratedCodeDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public GeneratedCodeDiagnosticsTests(ITestOutputHelper output) => _output = output;

    // What <ImplicitUsings>enable</ImplicitUsings> of Microsoft.NET.Sdk adds to a consumer.
    private const string ImplicitUsings = @"
global using global::System;
global using global::System.Collections.Generic;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Net.Http;
global using global::System.Threading;
global using global::System.Threading.Tasks;
";

    // An identifier in the org shape (standards/dotnet.md, Identifiers: THE ONE SHAPE); the handler's TId. The converter is
    // named in full because MicroPlumberd also declares a JsonParsableConverter<T> (CS0104 with both namespaces imported).
    private const string FooId = @"
[JsonConverter(typeof(ModelingEvolution.JsonParsableConverter.JsonParsableConverter<FooId>))]
public readonly record struct FooId(Guid Value) : IParsable<FooId>
{
    private const string Prefix = ""fo-"";

    public static FooId New() => new(Guid.NewGuid());
    public static implicit operator Guid(FooId id) => id.Value;
    public static explicit operator FooId(Guid value) => new(value);

    public static FooId Parse(string s, IFormatProvider? provider = null)
        => TryParse(s, provider, out var r) ? r : throw new FormatException($""Invalid {nameof(FooId)}: {s}"");
    public static bool TryParse(string? s, IFormatProvider? provider, out FooId result)
    {
        if (s is null) { result = default; return false; }
        var span = s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? s.AsSpan(Prefix.Length) : s.AsSpan();
        if (Guid.TryParse(span, out var g)) { result = new(g); return true; }
        result = default; return false;
    }
    public override string ToString() => $""{Prefix}{Value}"";
}
";

    // The simulator's reproduction: the user file already imports MicroPlumberd.Services (it must, for the attribute).
    private const string CommandHandlerSource = @"
using System.Text.Json.Serialization;
using MicroPlumberd;
using MicroPlumberd.Services;
//PLANTED

namespace Consumer.Handlers;
" + FooId + @"
public sealed record CreateFoo(string Name)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed record RenameFoo(string Name)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

[CommandHandler]
public partial class FooCommandHandler(IPlumberInstance plumber)
{
    public Task Handle(FooId id, CreateFoo cmd) => Task.CompletedTask;

    public Task<string> Handle(FooId id, RenameFoo cmd) => Task.FromResult(cmd.Name + plumber.GetHashCode());
}
";

    // The ProcessManager shape (MicroPlumberd.Tests.App XooProcessManager), user file importing MicroPlumberd.Services.
    private const string ProcessManagerSource = @"
using MicroPlumberd;
using MicroPlumberd.Services;
//PLANTED

namespace Consumer.Processes;

public sealed record OrderPlaced(string Number);
public sealed record PaymentReceived(string Number);
public sealed record ShipOrder(string Number);
public sealed record ChargeOrder(string Number);

[ProcessManager]
public partial class OrderProcessManager
{
    public Task<ICommandRequest<ChargeOrder>> StartWhen(Metadata m, OrderPlaced ev)
        => Task.FromResult(CommandRequest.Create(Guid.NewGuid(), new ChargeOrder(ev.Number)));

    private Task<ICommandRequest<ShipOrder>> When(Metadata m, PaymentReceived ev)
        => Task.FromResult(CommandRequest.Create(Guid.NewGuid(), new ShipOrder(ev.Number)));

    private Task Given(Metadata m, OrderPlaced ev) => Task.CompletedTask;
}
";

    // The two generators whose output the simulator also builds with warnings as errors today.
    private const string EventHandlerSource = @"
using MicroPlumberd;
//PLANTED

namespace Consumer.Views;

public sealed record FooCreated(string Name);

[EventHandler]
public partial class FooView
{
    private Task Given(Metadata m, FooCreated ev) => Task.CompletedTask;
}
";

    private const string AggregateSource = @"
using MicroPlumberd;
//PLANTED

namespace Consumer.Domain;

public sealed record FooOpened(string Name);
public sealed record FooState
{
    public string Name { get; init; } = string.Empty;
}

[Aggregate]
public partial class FooAggregate(Guid id) : AggregateBase<Guid, FooState>(id)
{
    private static FooState Given(FooState state, FooOpened ev) => state with { Name = ev.Name };
}
";

    public static TheoryData<string> Shapes => new() { "CommandHandler", "ProcessManager", "EventHandler", "Aggregate" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void GeneratedCode_UnderNullableEnable_HasNoWarningOrError(string shape)
    {
        var (source, generator) = Fixture(shape);

        var clean = Compile(source.Replace("//PLANTED", ""), generator, out var generated);
        Assert.EndsWith($"_{shape}.g.cs", Assert.Single(generated).FilePath);
        Assert.Empty(clean.Select(Describe));

        // Control: the same harness must see a warning planted in the user file, or the empty list above means nothing.
        var planted = Compile(source.Replace("//PLANTED", "using MicroPlumberd;"), generator, out _);
        var cs0105 = Assert.Single(planted);
        Assert.Equal("CS0105", cs0105.Id);
        Assert.Equal("Consumer.cs", Path.GetFileName(cs0105.Location.SourceTree?.FilePath));
    }

    private static (string Source, IIncrementalGenerator Generator) Fixture(string shape) => shape switch
    {
        "CommandHandler" => (CommandHandlerSource, new CommandHandlerSourceGenerator()),
        "ProcessManager" => (ProcessManagerSource, new ProcessManagerSourceGenerator()),
        "EventHandler" => (EventHandlerSource, new EventHandlerSourceGenerator()),
        "Aggregate" => (AggregateSource, new AggregateSourceGenerator()),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    // A consumer is built by Microsoft.NET.Sdk, whose Microsoft.NET.Sdk.CSharp.props starts every project with
    // <NoWarn>1701;1702</NoWarn> (assembly-version unification notes; here ModelingEvolution.JsonParsableConverter 1.0.1
    // is built against System.Text.Json 9 and the host is 10). The harness compiles as that consumer does; nothing else.
    private static readonly ImmutableDictionary<string, ReportDiagnostic> SdkDefaultNoWarn =
        ImmutableDictionary<string, ReportDiagnostic>.Empty
            .Add("CS1701", ReportDiagnostic.Suppress)
            .Add("CS1702", ReportDiagnostic.Suppress);

    /// <summary>Runs the generator and returns every diagnostic of severity warning or above of the resulting compilation.</summary>
    private ImmutableArray<Diagnostic> Compile(string source, IIncrementalGenerator generator, out ImmutableArray<SyntaxTree> generated)
    {
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "Consumer",
            new[]
            {
                CSharpSyntaxTree.ParseText(ImplicitUsings, parse, path: "GlobalUsings.g.cs"),
                CSharpSyntaxTree.ParseText(source, parse, path: "Consumer.cs")
            },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                warningLevel: 9999,
                specificDiagnosticOptions: SdkDefaultNoWarn));

        var driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parse);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        generated = output.SyntaxTrees.Except(compilation.SyntaxTrees).ToImmutableArray();
        foreach (var tree in generated) _output.WriteLine($"// {tree.FilePath}\n{tree}");

        var all = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();
        foreach (var d in all) _output.WriteLine(Describe(d));
        return all;
    }

    private static string Describe(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        return $"{d.Id} {Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1}): {d.GetMessage()}";
    }

    // Every assembly the test host loads from (the runtime plus MicroPlumberd, MicroPlumberd.Services, Scrutor, DI ...).
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray());
}

