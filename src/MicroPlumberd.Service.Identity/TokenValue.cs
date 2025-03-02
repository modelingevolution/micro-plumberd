using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Service.Identity;

/// <summary>
/// Holds a token's value
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<TokenValue>))]
public readonly record struct TokenValue : IParsable<TokenValue>, IComparable<TokenValue>
{
    public string Value { get; }

    public TokenValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token value cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;
    public static TokenValue Parse(string s, IFormatProvider? provider)
    {
        return new(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TokenValue result)
    {
        result = new(s);
        return true;
    }

    public int CompareTo(TokenValue other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}