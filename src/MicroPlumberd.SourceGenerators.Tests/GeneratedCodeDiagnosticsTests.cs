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
/// (observed in a consumer build: CS0105, CS8669 and CS1066 from the [CommandHandler] output of 1.3.4).
/// Each fixture is compiled against the real MicroPlumberd assemblies, so the whole compilation (user code plus
/// generated code) must carry no diagnostic of severity warning or above. The control in the same test plants a
/// duplicate using (CS0105) in the user source and requires the same harness to report it.
/// The same holds for a consumer that also builds its documentation file (GenerateDocumentationFile=true, which compiles
/// with DocumentationMode.Diagnose): every public or protected member the generators write must carry an XML comment, or
/// the consumer gets CS1591 from the generated file (issue #54, observed on 1.3.7). The fixtures' own code is fully
/// documented, so any CS1591 comes from the generated code; the control plants an undocumented public member in the user
/// source and requires the same harness to report it as CS1591.
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

    // A strongly typed identifier (prefixed, IParsable, JSON as one string); the handler's and the aggregate's TId. The converter is
    // named in full because MicroPlumberd also declares a JsonParsableConverter<T> (CS0104 with both namespaces imported).
    private const string FooId = @"
/// <summary>Identifies a foo.</summary>
/// <param name=""Value"">The guid behind the id.</param>
[JsonConverter(typeof(ModelingEvolution.JsonParsableConverter.JsonParsableConverter<FooId>))]
public readonly record struct FooId(Guid Value) : IParsable<FooId>
{
    private const string Prefix = ""fo-"";

    /// <summary>A new unique id.</summary>
    public static FooId New() => new(Guid.NewGuid());
    /// <summary>The guid behind the id.</summary>
    public static implicit operator Guid(FooId id) => id.Value;
    /// <summary>The id over a guid.</summary>
    public static explicit operator FooId(Guid value) => new(value);

    /// <inheritdoc/>
    public static FooId Parse(string s, IFormatProvider? provider = null)
        => TryParse(s, provider, out var r) ? r : throw new FormatException($""Invalid {nameof(FooId)}: {s}"");
    /// <inheritdoc/>
    public static bool TryParse(string? s, IFormatProvider? provider, out FooId result)
    {
        if (s is null) { result = default; return false; }
        var span = s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? s.AsSpan(Prefix.Length) : s.AsSpan();
        if (Guid.TryParse(span, out var g)) { result = new(g); return true; }
        result = default; return false;
    }
    /// <inheritdoc/>
    public override string ToString() => $""{Prefix}{Value}"";
}
";

    // A consumer's command handler: the user file already imports MicroPlumberd.Services (it must, for the attribute).
    private const string CommandHandlerSource = @"
using System.Text.Json.Serialization;
using MicroPlumberd;
using MicroPlumberd.Services;
//PLANTED

namespace Consumer.Handlers;
" + FooId + @"
/// <summary>Creates a foo.</summary>
/// <param name=""Name"">The foo's name.</param>
public sealed record CreateFoo(string Name)
{
    /// <summary>The command's exactly-once key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Renames a foo.</summary>
/// <param name=""Name"">The new name.</param>
public sealed record RenameFoo(string Name)
{
    /// <summary>The command's exactly-once key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Handles the foo commands.</summary>
[CommandHandler]
public partial class FooCommandHandler(IPlumberInstance plumber)
{
    /// <summary>Handles <see cref=""CreateFoo""/>.</summary>
    public Task Handle(FooId id, CreateFoo cmd) => Task.CompletedTask;

    /// <summary>Handles <see cref=""RenameFoo""/>.</summary>
    public Task<string> Handle(FooId id, RenameFoo cmd) => Task.FromResult(cmd.Name + plumber.GetHashCode());
}
";

    // The ProcessManager shape (MicroPlumberd.Tests.App XooProcessManager), user file importing MicroPlumberd.Services.
    // The commands carry the exactly-once Id every command carries; the facts carry no domain key (the test needs none);
    // the recipient is a Guid because CommandRequest.Create takes one.
    private const string ProcessManagerSource = @"
using MicroPlumberd;
using MicroPlumberd.Services;
//PLANTED

namespace Consumer.Processes;

/// <summary>An order was placed.</summary>
public sealed record OrderPlaced;
/// <summary>The order's payment arrived.</summary>
public sealed record PaymentReceived;
/// <summary>Ships an order.</summary>
public sealed record ShipOrder
{
    /// <summary>The command's exactly-once key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}
/// <summary>Charges an order.</summary>
public sealed record ChargeOrder
{
    /// <summary>The command's exactly-once key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>Takes an order from placement to shipping.</summary>
[ProcessManager]
public partial class OrderProcessManager
{
    /// <summary>Starts the process: charges the order.</summary>
    public Task<ICommandRequest<ChargeOrder>> StartWhen(Metadata m, OrderPlaced ev)
        => Task.FromResult(CommandRequest.Create(Guid.NewGuid(), new ChargeOrder()));

    private Task<ICommandRequest<ShipOrder>> When(Metadata m, PaymentReceived ev)
        => Task.FromResult(CommandRequest.Create(Guid.NewGuid(), new ShipOrder()));

    private Task Given(Metadata m, OrderPlaced ev) => Task.CompletedTask;
}
";

    // The two other generators, under the same consumer settings.
    private const string EventHandlerSource = @"
using MicroPlumberd;
//PLANTED

namespace Consumer.Views;

/// <summary>A foo was created.</summary>
/// <param name=""Name"">The foo's name.</param>
public sealed record FooCreated(string Name);

/// <summary>A view of the foos.</summary>
[EventHandler]
public partial class FooView
{
    private Task Given(Metadata m, FooCreated ev) => Task.CompletedTask;
}
";

    private const string AggregateSource = @"
using System.Text.Json.Serialization;
using MicroPlumberd;
//PLANTED

namespace Consumer.Domain;
" + FooId + @"

/// <summary>A foo was opened.</summary>
/// <param name=""Name"">The foo's name.</param>
public sealed record FooOpened(string Name);
/// <summary>What the foo aggregate remembers.</summary>
public sealed record FooState
{
    /// <summary>The foo's name.</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>A foo.</summary>
/// <param name=""id"">The foo's id.</param>
[Aggregate]
public partial class FooAggregate(FooId id) : AggregateBase<FooId, FooState>(id)
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

    [Theory]
    [MemberData(nameof(Shapes))]
    public void GeneratedCode_WithDocumentationFile_HasNoWarningOrError(string shape)
    {
        var (source, generator) = Fixture(shape);

        var clean = Compile(source.Replace("//PLANTED", ""), generator, out var generated, DocumentationMode.Diagnose);
        Assert.EndsWith($"_{shape}.g.cs", Assert.Single(generated).FilePath);
        // Control: the GENERATED tree is parsed with documentation diagnostics on; in any other mode the empty list below passes over nothing.
        Assert.Equal(DocumentationMode.Diagnose, generated[0].Options.DocumentationMode);
        Assert.Empty(clean.Select(Describe));

        // Control: the same harness must report an undocumented public member of the user file as CS1591.
        var planted = Compile(source.Replace("//PLANTED", "") + PlantedUndocumentedMember, generator, out _, DocumentationMode.Diagnose);
        var cs1591 = Assert.Single(planted);
        Assert.Equal("CS1591", cs1591.Id);
        Assert.Equal("Consumer.cs", Path.GetFileName(cs1591.Location.SourceTree?.FilePath));
        Assert.Contains("Planted.Undocumented()", cs1591.GetMessage());
    }

    // Appended after the fixture (inside its file-scoped namespace): a documented type with one undocumented public member.
    private const string PlantedUndocumentedMember = @"
/// <summary>The control's documented type.</summary>
public static class Planted
{
    public static void Undocumented() { }
}
";

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
    /// <param name="documentation">DocumentationMode.Diagnose is what GenerateDocumentationFile=true gives a consumer; it applies
    /// to the generated files too, because the driver parses them with the same options (asserted on the generated tree in
    /// GeneratedCode_WithDocumentationFile_HasNoWarningOrError).</param>
    private ImmutableArray<Diagnostic> Compile(string source, IIncrementalGenerator generator, out ImmutableArray<SyntaxTree> generated,
        DocumentationMode documentation = DocumentationMode.Parse)
    {
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithDocumentationMode(documentation);
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

