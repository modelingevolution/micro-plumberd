using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Uniquely identifies a role
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<RoleIdentifier>))]
public readonly record struct RoleIdentifier : IParsable<RoleIdentifier> , IComparable<RoleIdentifier>
{
    public Guid Id { get; }

    public RoleIdentifier(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Role identifier cannot be empty", nameof(id));

        Id = id;
    }

    public static RoleIdentifier New() => new(Guid.NewGuid());

    public static RoleIdentifier Parse(string value, IFormatProvider? provider = null) => new(Guid.Parse(value));

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

    public override string ToString() => Id.ToString();

    public int CompareTo(RoleIdentifier other)
    {
        return Id.CompareTo(other.Id);
    }
}