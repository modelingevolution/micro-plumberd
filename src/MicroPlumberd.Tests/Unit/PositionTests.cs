using EventStore.Client;
using FluentAssertions;
using MicroPlumberd.Services;

using MicroPlumberd.Tests.Integration.Services;
using MicroPlumberd.Tests.Utils;
using ModelingEvolution.DirectConnect;
using ProtoBuf;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Unit;

[TestCategory("Unit")]
public class PositionTests
{
    [Fact]
    public void CanConvertFromToStreamPositionStart()
    {
        var sp = FromStream.Start.ToStreamPosition();
        sp.Should().Be(StreamPosition.Start);
    }
    [Fact]
    public void CanConvertFromToStreamPositionEnd()
    {
        var sp = FromStream.End.ToStreamPosition();
        sp.Should().Be(StreamPosition.End);
    }
    [Fact]
    public void CanConvertFromToStreamPositionCustom()
    {
        var sp = FromStream.After(StreamPosition.FromInt64(1000)).ToStreamPosition();
        sp.ToInt64().Should().Be(1000);
    }
}


