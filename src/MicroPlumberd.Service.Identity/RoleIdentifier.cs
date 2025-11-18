using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Uniquely identifies a role in the identity system using a GUID.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<RoleIdentifier>))]
public readonly record struct RoleIdentifier : IParsable<RoleIdentifier> , IComparable<RoleIdentifier>
{
    /// <summary>
    /// Gets the unique identifier for the role.
    /// </summary>
    /// <value>A GUID that uniquely identifies the role.</value>
    public Guid Id { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleIdentifier"/> struct.
    /// </summary>
    /// <param name="id">The unique identifier. Cannot be an empty GUID.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is an empty GUID.</exception>
    public RoleIdentifier(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Role identifier cannot be empty", nameof(id));

        Id = id;
    }

    /// <summary>
    /// Creates a new role identifier with a randomly generated GUID.
    /// </summary>
    /// <returns>A new <see cref="RoleIdentifier"/> instance.</returns>
    public static RoleIdentifier New() => new(Guid.NewGuid());

    /// <summary>
    /// Parses a string into a <see cref="RoleIdentifier"/>.
    /// </summary>
    /// <param name="value">The string representation of a GUID to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="RoleIdentifier"/> instance.</returns>
    /// <exception cref="FormatException">Thrown when the string is not a valid GUID format.</exception>
    public static RoleIdentifier Parse(string value, IFormatProvider? provider = null) => new(Guid.Parse(value));

    /// <summary>
    /// Attempts to parse a string into a <see cref="RoleIdentifier"/>.
    /// </summary>
    /// <param name="value">The string representation of a GUID to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="RoleIdentifier"/> if successful; otherwise, the default value.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    public static bool TryParse(string value, IFormatProvider? provider, out RoleIdentifier result)
    {
        if (Guid.TryParse(value, out var id))
        {
            result = new RoleIdentifier(id);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Returns the string representation of the role identifier.
    /// </summary>
    /// <returns>The GUID as a string.</returns>
    public override string ToString() => Id.ToString();

    /// <summary>
    /// Compares this role identifier to another role identifier.
    /// </summary>
    /// <param name="other">The role identifier to compare to.</param>
    /// <returns>A value indicating the relative order of the identifiers.</returns>
    public int CompareTo(RoleIdentifier other)
    {
        return Id.CompareTo(other.Id);
    }
}