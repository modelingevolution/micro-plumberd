using FluentAssertions;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using MicroPlumberd.Services;
using Xunit;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// Packages compiled against 1.2.x (ModelingEvolution.Booking, .Chat) bind to the 3-parameter registrations
/// (services, bool persistently, FromStream? start). 1.3.0 removed them and those packages threw MissingMethodException
/// at startup (erp, 2026-11-18). The exact old signatures must exist.
/// </summary>
public class ContainerExtensionsBinaryCompatTests
{
    [Theory]
    [InlineData("AddEventHandler")]
    [InlineData("AddScopedEventHandler")]
    [InlineData("AddSingletonEventHandler")]
    public void The_1_2_x_three_parameter_signature_exists(string name)
    {
        var old = typeof(MicroPlumberd.Services.ContainerExtensions).GetMethods()
            .Where(m => m.Name == name && m.IsGenericMethodDefinition)
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .Where(p => p.Length == 3);

        old.Should().ContainSingle(p => p[0] == typeof(IServiceCollection) && p[1] == typeof(bool) && p[2] == typeof(FromStream?),
            "{0}(IServiceCollection, bool, FromStream?) is what 1.2.x binaries call", name);
    }
}
