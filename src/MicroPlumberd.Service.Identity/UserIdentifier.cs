// ValueTypes.cs

using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity
{
    /// <summary>
    /// Uniquely identifies a user in the identity system using a GUID.
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<UserIdentifier>))]
    public readonly record struct UserIdentifier : IParsable<UserIdentifier>, IComparable<UserIdentifier>
    {
        /// <summary>
        /// Gets the unique identifier for the user.
        /// </summary>
        /// <value>A GUID that uniquely identifies the user.</value>
        public Guid Id { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserIdentifier"/> struct.
        /// </summary>
        /// <param name="id">The unique identifier. Cannot be an empty GUID.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is an empty GUID.</exception>
        public UserIdentifier(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("User identifier cannot be empty", nameof(id));

            Id = id;
        }

        /// <summary>
        /// Creates a new user identifier with a randomly generated GUID.
        /// </summary>
        /// <returns>A new <see cref="UserIdentifier"/> instance.</returns>
        public static UserIdentifier New() => new(Guid.NewGuid());

        /// <summary>
        /// Parses a string into a <see cref="UserIdentifier"/>.
        /// </summary>
        /// <param name="value">The string representation of a GUID to parse.</param>
        /// <param name="provider">An optional format provider (not used).</param>
        /// <returns>A new <see cref="UserIdentifier"/> instance.</returns>
        /// <exception cref="FormatException">Thrown when the string is not a valid GUID format.</exception>
        public static UserIdentifier Parse(string value, IFormatProvider? provider = null) => new(Guid.Parse(value));

        /// <summary>
        /// Attempts to parse a string into a <see cref="UserIdentifier"/>.
        /// </summary>
        /// <param name="value">The string representation of a GUID to parse.</param>
        /// <param name="provider">An optional format provider (not used).</param>
        /// <param name="result">The parsed <see cref="UserIdentifier"/> if successful; otherwise, the default value.</param>
        /// <returns>True if parsing was successful; otherwise, false.</returns>
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

        /// <summary>
        /// Returns the string representation of the user identifier.
        /// </summary>
        /// <returns>The GUID as a string.</returns>
        public override string ToString() => Id.ToString();

        /// <summary>
        /// Compares this user identifier to another user identifier.
        /// </summary>
        /// <param name="other">The user identifier to compare to.</param>
        /// <returns>A value indicating the relative order of the identifiers.</returns>
        public int CompareTo(UserIdentifier other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}