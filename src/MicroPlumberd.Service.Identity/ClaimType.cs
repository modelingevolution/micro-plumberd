using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Represents a user in the identity system
/// </summary>
public class User : IdentityUser
{
    // Additional properties can be added here if needed
    // This class inherits all the standard properties from IdentityUser
}

/// <summary>
/// Represents a role in the identity system
/// </summary>
public class Role : IdentityRole
{
    // Additional properties can be added here if needed
    // This class inherits all the standard properties from IdentityRole
}
/// <summary>
/// Represents a claim type in the identity system. Claim types identify the kind of information a claim contains.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ClaimType>))]
public readonly record struct ClaimType : IParsable<ClaimType>, IComparable<ClaimType>
{
    /// <summary>
    /// Gets the string value of the claim type.
    /// </summary>
    /// <value>A normalized lowercase string representing the claim type.</value>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimType"/> struct.
    /// </summary>
    /// <param name="value">The claim type value. Cannot be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public ClaimType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Claim type cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the claim type.
    /// </summary>
    /// <returns>The claim type value.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Gets the 'name' claim type.
    /// </summary>
    public static ClaimType Name => new("name");

    /// <summary>
    /// Gets the 'role' claim type.
    /// </summary>
    public static ClaimType Role => new("role");

    /// <summary>
    /// Gets the 'email' claim type.
    /// </summary>
    public static ClaimType Email => new("email");

    /// <summary>
    /// Parses a string into a <see cref="ClaimType"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="ClaimType"/> instance.</returns>
    public static ClaimType Parse(string s, IFormatProvider? provider)
    {
        return new ClaimType(s);
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="ClaimType"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="ClaimType"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClaimType result)
    {
        result = new ClaimType(s);
        return true;
    }

    /// <summary>
    /// Compares this claim type to another claim type.
    /// </summary>
    /// <param name="other">The claim type to compare to.</param>
    /// <returns>A value indicating the relative order of the claim types.</returns>
    public int CompareTo(ClaimType other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}