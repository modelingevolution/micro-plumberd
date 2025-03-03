using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Identifies a user within an external provider
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ExternalLoginKey>))]
public readonly record struct ExternalLoginKey : IParsable<ExternalLoginKey>, IComparable<ExternalLoginKey>
{
    public string Value { get; }

    public ExternalLoginKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("External login key cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;
    public static ExternalLoginKey Parse(string s, IFormatProvider? provider)
    {
        return new ExternalLoginKey(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExternalLoginKey result)
    {
        result = new ExternalLoginKey(s);
        return true;
    }

    public int CompareTo(ExternalLoginKey other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}