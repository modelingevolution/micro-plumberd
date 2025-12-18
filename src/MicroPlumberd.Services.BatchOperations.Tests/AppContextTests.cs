using FluentAssertions;
using Xunit;

namespace MicroPlumberd.Services.BatchOperations.Tests;

/// <summary>
/// Unit tests for <see cref="AppContext"/> and <see cref="AppContextProvider{T}"/>.
/// </summary>
public class AppContextTests
{
    [Fact]
    public void Empty_HasEmptyInstance()
    {
        // Act
        var empty = AppContext.Empty;

        // Assert
        empty.AppInstance.Name.Should().BeEmpty();
        empty.AppInstance.Version.Should().BeEmpty();
        empty.AppInstance.Host.Should().BeEmpty();
        empty.AppInstance.Node.Should().Be(0u);
        empty.AppSession.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        // Arrange
        var instance = new AppInstance("MyApp", "1.0.0", "localhost", 1);
        var session = Guid.NewGuid();

        // Act
        var context = new AppContext(instance, session);

        // Assert
        context.AppInstance.Should().Be(instance);
        context.AppSession.Should().Be(session);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var instance = new AppInstance("MyApp", "1.0.0", "localhost", 0);
        var session = Guid.Parse("12345678-1234-1234-1234-123456789abc");

        // Act
        var context1 = new AppContext(instance, session);
        var context2 = new AppContext(instance, session);

        // Assert
        context1.Should().Be(context2);
        (context1 == context2).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentSession_AreNotEqual()
    {
        // Arrange
        var instance = new AppInstance("MyApp", "1.0.0", "localhost", 0);

        // Act
        var context1 = new AppContext(instance, Guid.NewGuid());
        var context2 = new AppContext(instance, Guid.NewGuid());

        // Assert
        context1.Should().NotBe(context2);
    }
}

/// <summary>
/// Unit tests for <see cref="AppContextProvider{T}"/>.
/// </summary>
public class AppContextProviderTests
{
    // Marker type for testing
    private class TestAppMarker { }

    [Fact]
    public void Context_DerivesFromAssembly()
    {
        // Arrange
        var provider = new AppContextProvider<TestAppMarker>();

        // Act
        var context = provider.Context;

        // Assert
        context.AppInstance.Name.Should().NotBeNullOrEmpty();
        context.AppInstance.Version.Should().NotBeNullOrEmpty();
        context.AppInstance.Host.Should().Be(Environment.MachineName);
        context.AppInstance.Node.Should().Be(0u);
        context.AppSession.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Context_ReturnsSameSession_OnMultipleCalls()
    {
        // Arrange
        var provider = new AppContextProvider<TestAppMarker>();

        // Act
        var context1 = provider.Context;
        var context2 = provider.Context;

        // Assert
        context1.AppSession.Should().Be(context2.AppSession);
        context1.Should().Be(context2);
    }

    [Fact]
    public void Context_WithNodeProvider_UsesProvidedNode()
    {
        // Arrange
        uint expectedNode = 42;
        var provider = new AppContextProvider<TestAppMarker>(() => expectedNode);

        // Act
        var context = provider.Context;

        // Assert
        context.AppInstance.Node.Should().Be(expectedNode);
    }

    [Fact]
    public void Context_DifferentProviderInstances_HaveDifferentSessions()
    {
        // Arrange
        var provider1 = new AppContextProvider<TestAppMarker>();
        var provider2 = new AppContextProvider<TestAppMarker>();

        // Act
        var context1 = provider1.Context;
        var context2 = provider2.Context;

        // Assert
        context1.AppSession.Should().NotBe(context2.AppSession);
    }

    [Fact]
    public void Context_ImplementsIAppContextProvider()
    {
        // Arrange
        IAppContextProvider provider = new AppContextProvider<TestAppMarker>();

        // Act
        var context = provider.Context;

        // Assert
        context.AppSession.Should().NotBe(Guid.Empty);
    }
}
