using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Names a token
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<TokenName>))]
public readonly record struct TokenName : IParsable<TokenName>, IComparable<TokenName>
{
    public string Value { get; }

    public TokenName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token name cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;

    // Common token names
    public static TokenName RefreshToken => new("RefreshToken");
    public static TokenName EmailConfirmation => new("EmailConfirmation");
    public static TokenName ResetPassword => new("ResetPassword");
    public static TokenName Parse(string s, IFormatProvider? provider)
    {
        return new TokenName(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TokenName result)
    {
        result = new TokenName(s);
        return true;
    }

    public int CompareTo(TokenName other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}