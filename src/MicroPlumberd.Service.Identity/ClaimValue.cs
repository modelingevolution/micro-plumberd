using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Represents a claim value
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ClaimValue>))]
public readonly record struct ClaimValue : IParsable<ClaimValue>, IComparable<ClaimValue>
{
    public string Value { get; }

    public ClaimValue(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;
    public static ClaimValue Parse(string s, IFormatProvider? provider)
    {
        return new ClaimValue(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClaimValue result)
    {
        result = new ClaimValue(s);
        return true;
    }

    public int CompareTo(ClaimValue other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}