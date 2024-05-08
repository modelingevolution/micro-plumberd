using System.Text.Json;
using FluentAssertions;

namespace MicroPlumberd.Tests.Unit;

public class OptionTests
{
    record Foo(Option<int> SomeCoolValue);

    [Fact]
    public void CanSerializeDeserialize()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new OptionConverterFactory());

        var origin = new Foo(10);
        var json = JsonSerializer.Serialize(origin, options);

        var actual = JsonSerializer.Deserialize<Foo>(json, options);
        actual.Should().Be(origin);
    }
}