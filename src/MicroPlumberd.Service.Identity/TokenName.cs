using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Represents the name/type of a token in the identity system (e.g., refresh token, email confirmation token).
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<TokenName>))]
public readonly record struct TokenName : IParsable<TokenName>, IComparable<TokenName>
{
    /// <summary>
    /// Gets the token name value.
    /// </summary>
    /// <value>A normalized lowercase string representing the token name.</value>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenName"/> struct.
    /// </summary>
    /// <param name="value">The token name. Cannot be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public TokenName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token name cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the token name.
    /// </summary>
    /// <returns>The token name value.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Gets the refresh token name.
    /// </summary>
    public static TokenName RefreshToken => new("RefreshToken");

    /// <summary>
    /// Gets the email confirmation token name.
    /// </summary>
    public static TokenName EmailConfirmation => new("EmailConfirmation");

    /// <summary>
    /// Gets the password reset token name.
    /// </summary>
    public static TokenName ResetPassword => new("ResetPassword");

    /// <summary>
    /// Parses a string into a <see cref="TokenName"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="TokenName"/> instance.</returns>
    public static TokenName Parse(string s, IFormatProvider? provider)
    {
        return new TokenName(s);
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="TokenName"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="TokenName"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TokenName result)
    {
        result = new TokenName(s);
        return true;
    }

    /// <summary>
    /// Compares this token name to another token name.
    /// </summary>
    /// <param name="other">The token name to compare to.</param>
    /// <returns>A value indicating the relative order of the token names.</returns>
    public int CompareTo(TokenName other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}