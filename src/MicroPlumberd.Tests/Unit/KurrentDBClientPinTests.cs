using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using KurrentDB.Client;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// card microplumberd-pin-kurrentdb-client: KurrentDB.Client was referenced as "1.*", so a client bump could arrive on any
/// restore — under behaviour MicroPlumberd measured on one version (IsConflict's shape). The client is now pinned ONCE
/// ($(KurrentDBClientVersion), src/Directory.Build.props) and these rows hold the pin, the resolved client and the
/// measurement together.
/// </summary>
public class KurrentDBClientPinTests : IDisposable
{
    private const string Pin = "$(KurrentDBClientVersion)";
    private readonly string _planted = Directory.CreateTempSubdirectory("mp-pin-").FullName;

    public void Dispose() => Directory.Delete(_planted, recursive: true);

    private static string Metadata(string key) =>
        typeof(KurrentDBClientPinTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    /// <summary>
    /// The MSBuild files that can reference a package: every project under src, and every Directory.*.props/.targets
    /// (Directory.Build.*, Directory.Packages.props) under src and at the repository root.
    /// </summary>
    internal static IEnumerable<string> BuildFiles(string src)
    {
        bool NotOutput(string f) => !f.Split(Path.DirectorySeparatorChar).Any(d => d is "obj" or "bin");
        bool IsDirectoryFile(string f) => Path.GetFileName(f).StartsWith("Directory.", StringComparison.Ordinal)
                                          && Path.GetExtension(f) is ".props" or ".targets";
        var inSrc = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(NotOutput).Where(f => f.EndsWith(".csproj", StringComparison.Ordinal) || IsDirectoryFile(f));
        var atRoot = Directory.GetParent(Path.GetFullPath(src)) is { } root
            ? Directory.EnumerateFiles(root.FullName).Where(IsDirectoryFile) : [];
        return inSrc.Concat(atRoot).Distinct();
    }

    /// <summary>
    /// Every reference to KurrentDB.Client in <paramref name="file"/> — a PackageReference or a central PackageVersion whose
    /// Include OR Update names it (ids are case-insensitive), in any attribute order or quoting — with its version from the
    /// attribute or a child element; null when it has none.
    /// </summary>
    internal static IEnumerable<(string File, string? Version)> ClientReferences(string file) =>
        XDocument.Load(file).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion")
            .Where(e => string.Equals((string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"), "KurrentDB.Client",
                StringComparison.OrdinalIgnoreCase))
            .Select(e => (file, (string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value));

    [Fact]
    public void TheResolvedClient_IsThePinnedVersion_AndTheOneTheConflictShapeWasMeasuredOn()
    {
        var pinned = Metadata("PinnedKurrentDBClientVersion");
        var resolved = typeof(KurrentDBClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        pinned.Should().NotBeNullOrEmpty("the pin is baked in from $(KurrentDBClientVersion)");
        resolved.Should().Be(pinned, "a restore resolves exactly the pinned client — never a floating bump");
        resolved.Should().Be(KurrentDBProjectionManagementClientExtensions.MeasuredOnClientVersion,
            "IsConflict's shape was measured on this client; a bump re-measures and moves MeasuredOnClientVersion with the pin");
    }

    [Fact]
    public void EveryReferenceToTheClient_InTheRepository_UsesThePin()
    {
        var references = BuildFiles(Metadata("MicroPlumberdSourceDir")).SelectMany(ClientReferences).ToList();

        references.Should().HaveCountGreaterThanOrEqualTo(3, "MicroPlumberd, MicroPlumberd.Testing and MicroPlumberd.Migration reference it");
        references.Where(r => r.Version != Pin).Should().BeEmpty("every reference uses the one pin");
    }

    /// <summary>rev-reach 269e9cf45: shapes a text match missed, and the files it did not read — each planted, each refused.</summary>
    [Theory]
    [InlineData("App.csproj", """<Project><ItemGroup><PackageReference Version="1.*" Include="KurrentDB.Client" /></ItemGroup></Project>""")]
    [InlineData("App.csproj", """<Project><ItemGroup><PackageReference Update="KurrentDB.Client" Version="1.*" /></ItemGroup></Project>""")]
    [InlineData("App.csproj", """<Project><ItemGroup><PackageReference Include='KurrentDB.Client' Version='1.*' /></ItemGroup></Project>""")]
    [InlineData("App.csproj", """<Project><ItemGroup><PackageReference Include="kurrentdb.client"><Version>1.*</Version></PackageReference></ItemGroup></Project>""")]
    [InlineData("App.csproj", """<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemGroup><PackageReference Include="KurrentDB.Client" Version="1.*" /></ItemGroup></Project>""")]
    [InlineData("App.csproj", """<Project><ItemGroup><PackageReference Include="KurrentDB.Client" /></ItemGroup></Project>""")]
    [InlineData("Directory.Packages.props", """<Project><ItemGroup><PackageVersion Include="KurrentDB.Client" Version="1.*" /></ItemGroup></Project>""")]
    [InlineData("Directory.Build.targets", """<Project><ItemGroup><PackageReference Update="KurrentDB.Client" Version="1.*" /></ItemGroup></Project>""")]
    public void AFloatingOrUnpinnedReference_InAnyShapeOrBuildFile_IsRefused(string fileName, string content)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_planted, "src", "App")).FullName;
        File.WriteAllText(Path.Combine(dir, fileName), content);

        var found = BuildFiles(Path.Combine(_planted, "src")).SelectMany(ClientReferences).ToList();

        found.Should().ContainSingle($"the reference in {fileName} is read");
        found.Single().Version.Should().NotBe(Pin, "it is not the pin, so the repository row refuses it");
    }

    /// <summary>The control: the pinned form, in a project and at the repository root, is read and accepted.</summary>
    [Fact]
    public void ThePinnedForm_IsReadAndAccepted_InAProjectAndAtTheRoot()
    {
        var src = Directory.CreateDirectory(Path.Combine(_planted, "src", "App")).Parent!.FullName;
        File.WriteAllText(Path.Combine(src, "App", "App.csproj"),
            """<Project><ItemGroup><PackageReference Include="KurrentDB.Client" Version="$(KurrentDBClientVersion)" /></ItemGroup></Project>""");
        File.WriteAllText(Path.Combine(_planted, "Directory.Packages.props"),
            """<Project><ItemGroup><PackageVersion Include="KurrentDB.Client" Version="$(KurrentDBClientVersion)" /></ItemGroup></Project>""");

        var found = BuildFiles(src).SelectMany(ClientReferences).ToList();

        found.Should().HaveCount(2).And.OnlyContain(r => r.Version == Pin);
    }
}
