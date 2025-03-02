using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace MicroPlumberd.Service.Identity;

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
/// Represents a claim type
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ClaimType>))]
public readonly record struct ClaimType : IParsable<ClaimType>, IComparable<ClaimType>
{
    public string Value { get; }

    public ClaimType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Claim type cannot be empty", nameof(value));

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;

    // Common claim types
    public static ClaimType Name => new("name");
    public static ClaimType Role => new("role");
    public static ClaimType Email => new("email");
    public static ClaimType Parse(string s, IFormatProvider? provider)
    {
        return new ClaimType(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ClaimType result)
    {
        result = new ClaimType(s);
        return true;
    }

    public int CompareTo(ClaimType other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }
}