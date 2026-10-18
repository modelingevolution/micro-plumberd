using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using KurrentDB.Client;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// card microplumberd-pin-kurrentdb-client: KurrentDB.Client was referenced as "1.*", so a client bump could arrive on any
/// restore — under behaviour MicroPlumberd measured on one version (IsConflict's shape). The client is now pinned ONCE
/// ($(KurrentDBClientVersion), src/Directory.Build.props) and these rows hold the pin, the resolved client and the
/// measurement together.
/// </summary>
public class KurrentDBClientPinTests
{
    private static string Metadata(string key) =>
        typeof(KurrentDBClientPinTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

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
    public void EveryProjectReferencingTheClient_UsesThePin()
    {
        var src = Metadata("MicroPlumberdSourceDir");
        var references = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"<PackageReference\s+Include=""KurrentDB\.Client""[^>]*>")
                .Select(m => (Project: Path.GetFileName(f), Reference: m.Value)))
            .ToList();

        references.Should().HaveCountGreaterThanOrEqualTo(3, "MicroPlumberd, MicroPlumberd.Testing and MicroPlumberd.Migration reference it");
        references.Where(r => !r.Reference.Contains(@"Version=""$(KurrentDBClientVersion)""")).Should().BeEmpty(
            "every direct reference uses the one pin");
    }
}
