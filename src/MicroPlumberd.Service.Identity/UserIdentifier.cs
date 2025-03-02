// ValueTypes.cs

using System.Text.Json.Serialization;

namespace MicroPlumberd.Service.Identity
{
    /// <summary>
    /// Uniquely identifies a user
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<UserIdentifier>))]
    public readonly record struct UserIdentifier : IParsable<UserIdentifier>, IComparable<UserIdentifier>
    {
        public Guid Id { get; }

        public UserIdentifier(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("User identifier cannot be empty", nameof(id));

            Id = id;
        }

        public static UserIdentifier New() => new(Guid.NewGuid());

        public static UserIdentifier Parse(string value, IFormatProvider? provider = null) => new(Guid.Parse(value));

        public static bool TryParse(string? value, IFormatProvider? provider, out UserIdentifier result)
        {
            if (Guid.TryParse(value, out var id))
            {
                result = new UserIdentifier(id);
                return true;
            }

            result = default;
            return false;
        }

        public override string ToString() => Id.ToString();

        public int CompareTo(UserIdentifier other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}