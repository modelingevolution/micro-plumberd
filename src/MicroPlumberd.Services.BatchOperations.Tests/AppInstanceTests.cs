using FluentAssertions;
using Xunit;

namespace MicroPlumberd.Services.BatchOperations.Tests;

/// <summary>
/// Unit tests for <see cref="AppInstance"/> parsing and serialization.
/// </summary>
public class AppInstanceTests
{
    [Fact]
    public void ToString_FormatsCorrectly()
    {
        // Arrange
        var instance = new AppInstance("MyApp", "1.0.0", "server01", 3);

        // Act
        var result = instance.ToString();

        // Assert
        result.Should().Be("MyApp:1.0.0/server01.3");
    }

    [Fact]
    public void ToString_WithZeroNode_IncludesNode()
    {
        // Arrange
        var instance = new AppInstance("MyApp", "1.0.0", "localhost", 0);

        // Act
        var result = instance.ToString();

        // Assert
        result.Should().Be("MyApp:1.0.0/localhost.0");
    }

    [Fact]
    public void Parse_ValidFormat_ReturnsInstance()
    {
        // Arrange
        var input = "MyApp:1.0.0/server01.3";

        // Act
        var result = AppInstance.Parse(input);

        // Assert
        result.Name.Should().Be("MyApp");
        result.Version.Should().Be("1.0.0");
        result.Host.Should().Be("server01");
        result.Node.Should().Be(3u);
    }

    [Fact]
    public void Parse_WithZeroNode_ParsesCorrectly()
    {
        // Arrange
        var input = "TestApp:2.1.0/localhost.0";

        // Act
        var result = AppInstance.Parse(input);

        // Assert
        result.Name.Should().Be("TestApp");
        result.Version.Should().Be("2.1.0");
        result.Host.Should().Be("localhost");
        result.Node.Should().Be(0u);
    }

    [Fact]
    public void Parse_WithoutNode_DefaultsToZero()
    {
        // Arrange
        var input = "TestApp:1.0.0/localhost";

        // Act
        var result = AppInstance.Parse(input);

        // Assert
        result.Name.Should().Be("TestApp");
        result.Host.Should().Be("localhost");
        result.Node.Should().Be(0u);
    }

    [Fact]
    public void TryParse_ValidFormat_ReturnsTrue()
    {
        // Arrange
        var input = "MyApp:1.0.0/server01.3";

        // Act
        var success = AppInstance.TryParse(input, null, out var result);

        // Assert
        success.Should().BeTrue();
        result.Name.Should().Be("MyApp");
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        // Act
        var success = AppInstance.TryParse(null, null, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().Be(default(AppInstance));
    }

    [Fact]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        // Act
        var success = AppInstance.TryParse(string.Empty, null, out var result);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_InvalidFormat_MissingColon_ReturnsFalse()
    {
        // Act
        var success = AppInstance.TryParse("MyApp1.0.0/server01.3", null, out _);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_InvalidFormat_MissingSlash_ReturnsFalse()
    {
        // Act
        var success = AppInstance.TryParse("MyApp:1.0.0server01.3", null, out _);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_InvalidNode_ReturnsFalse()
    {
        // Act
        var success = AppInstance.TryParse("MyApp:1.0.0/server01.abc", null, out _);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidFormat_ThrowsFormatException()
    {
        // Act
        var act = () => AppInstance.Parse("invalid");

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void RoundTrip_ParseThenToString_PreservesValue()
    {
        // Arrange
        var original = new AppInstance("Platform", "3.2.1", "prod-server-01", 5);

        // Act
        var serialized = original.ToString();
        var deserialized = AppInstance.Parse(serialized);

        // Assert
        deserialized.Should().Be(original);
    }

    [Theory]
    [InlineData("App:1.0/host.0", "App", "1.0", "host", 0u)]
    [InlineData("My-App:1.0.0-beta/my-host.99", "My-App", "1.0.0-beta", "my-host", 99u)]
    [InlineData("a:b/c.1", "a", "b", "c", 1u)]
    public void Parse_VariousFormats_ParsesCorrectly(
        string input, string expectedName, string expectedVersion, string expectedHost, uint expectedNode)
    {
        // Act
        var result = AppInstance.Parse(input);

        // Assert
        result.Name.Should().Be(expectedName);
        result.Version.Should().Be(expectedVersion);
        result.Host.Should().Be(expectedHost);
        result.Node.Should().Be(expectedNode);
    }
}
