using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Represents a claim value in the identity system. Claim values contain the actual data associated with a claim type.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ClaimValue>))]
public readonly record struct ClaimValue : IParsable<ClaimValue>, IComparable<ClaimValue>
{
    /// <summary>
    /// Gets the string value of the claim.
    /// </summary>
    /// <value>A normalized lowercase string representing the claim value.</value>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimValue"/> struct.
    /// </summary>
    /// <param name="value">The claim value. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public ClaimValue(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the claim value.
    /// </summary>
    /// <returns>The claim value.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Parses a string into a <see cref="ClaimValue"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="ClaimValue"/> instance.</returns>
    public static ClaimValue Parse(string s, IFormatProvider? provider)
    {
        return new ClaimValue(s);
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="ClaimValue"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="ClaimValue"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClaimValue result)
    {
        result = new ClaimValue(s);
        return true;
    }

    /// <summary>
    /// Compares this claim value to another claim value.
    /// </summary>
    /// <param name="other">The claim value to compare to.</param>
    /// <returns>A value indicating the relative order of the claim values.</returns>
    public int CompareTo(ClaimValue other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}