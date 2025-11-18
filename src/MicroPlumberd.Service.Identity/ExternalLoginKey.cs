using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Identifies a user within an external authentication provider. This is the unique identifier assigned by the external provider.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ExternalLoginKey>))]
public readonly record struct ExternalLoginKey : IParsable<ExternalLoginKey>, IComparable<ExternalLoginKey>
{
    /// <summary>
    /// Gets the external login key value.
    /// </summary>
    /// <value>A normalized lowercase string representing the user's unique identifier in the external provider.</value>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLoginKey"/> struct.
    /// </summary>
    /// <param name="value">The external login key. Cannot be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public ExternalLoginKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("External login key cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the external login key.
    /// </summary>
    /// <returns>The external login key value.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Parses a string into an <see cref="ExternalLoginKey"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="ExternalLoginKey"/> instance.</returns>
    public static ExternalLoginKey Parse(string s, IFormatProvider? provider)
    {
        return new ExternalLoginKey(s);
    }

    /// <summary>
    /// Attempts to parse a string into an <see cref="ExternalLoginKey"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="ExternalLoginKey"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExternalLoginKey result)
    {
        result = new ExternalLoginKey(s);
        return true;
    }

    /// <summary>
    /// Compares this external login key to another key.
    /// </summary>
    /// <param name="other">The external login key to compare to.</param>
    /// <returns>A value indicating the relative order of the keys.</returns>
    public int CompareTo(ExternalLoginKey other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}