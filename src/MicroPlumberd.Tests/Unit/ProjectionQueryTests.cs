using FluentAssertions;
using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// Pins the JavaScript emitted for the continuous "join" projections. A source event whose target was
/// scavenged reaches the handler as streamId == null / sequenceNumber == -1; linking it faults the whole
/// projection ("Invalid link to event -1@null") and with it every subscription on the output stream.
/// </summary>
[TestCategory("Unit")]
public class ProjectionQueryTests
{
    [Fact]
    public void CreateJoinQuery_Should_Guard_LinkTo_Against_Unresolved_Events()
    {
        // Arrange & Act
        var query = KurrentDBProjectionManagementClientExtensions.CreateJoinQuery(">AppName", ["FooCreated", "FooUpdated"]);

        // Assert
        query.Should().Be(
            "fromStreams(['$et-FooCreated','$et-FooUpdated']).when( { " +
            "\n    $any : function(s,e) { if(e && e.streamId !== null && e.sequenceNumber >= 0) linkTo('>AppName', e) }" +
            "\n});");
    }

    [Fact]
    public void CreateJoinQuery_Should_Not_Link_Before_Checking_Event_Validity()
    {
        // Arrange & Act
        var query = KurrentDBProjectionManagementClientExtensions.CreateJoinQuery(">AppName", ["FooCreated"]);

        // Assert
        query.Should().Contain("e.streamId !== null").And.Contain("e.sequenceNumber >= 0");
        query.IndexOf("e.streamId !== null", StringComparison.Ordinal)
            .Should().BePositive().And.BeLessThan(query.IndexOf("linkTo(", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateLookupQuery_Should_Guard_LinkTo_Against_Unresolved_Events()
    {
        // Arrange & Act
        var query = KurrentDBProjectionManagementClientExtensions.CreateLookupQuery("Foo", "UserId", "byUser");

        // Assert
        query.Should().Be(
            "fromStreams(['$ce-Foo']).when( { \n    $any : function(s,e) { " +
            "\n        if(e && e.streamId !== null && e.sequenceNumber >= 0 && e.body && e.body.UserId) {" +
            "\n            linkTo('byUser-' + e.body.UserId, e) \n        }\n        \n    }\n});");
    }

    [Fact]
    public void CreateLookupQuery_Should_Check_Event_Validity_Before_Touching_Body()
    {
        // Arrange & Act
        var query = KurrentDBProjectionManagementClientExtensions.CreateLookupQuery("Foo", "UserId", "byUser");

        // Assert
        query.Should().Contain("e.sequenceNumber >= 0");
        query.IndexOf("e.sequenceNumber >= 0", StringComparison.Ordinal)
            .Should().BePositive().And.BeLessThan(query.IndexOf("e.body", StringComparison.Ordinal));
    }
}
