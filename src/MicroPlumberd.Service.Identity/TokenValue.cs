using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Represents the actual value/content of a token in the identity system.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<TokenValue>))]
public readonly record struct TokenValue : IParsable<TokenValue>, IComparable<TokenValue>
{
    /// <summary>
    /// Gets the token value.
    /// </summary>
    /// <value>A normalized lowercase string representing the token's value.</value>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenValue"/> struct.
    /// </summary>
    /// <param name="value">The token value. Cannot be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public TokenValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token value cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the token value.
    /// </summary>
    /// <returns>The token value.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Parses a string into a <see cref="TokenValue"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="TokenValue"/> instance.</returns>
    public static TokenValue Parse(string s, IFormatProvider? provider)
    {
        return new(s);
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="TokenValue"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="TokenValue"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TokenValue result)
    {
        result = new(s);
        return true;
    }

    /// <summary>
    /// Compares this token value to another token value.
    /// </summary>
    /// <param name="other">The token value to compare to.</param>
    /// <returns>A value indicating the relative order of the token values.</returns>
    public int CompareTo(TokenValue other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}