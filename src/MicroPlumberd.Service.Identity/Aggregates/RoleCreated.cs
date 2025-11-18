using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Event raised when a new role is created in the identity system.
    /// </summary>
    [OutputStream("Role")]
    public record RoleCreated
    {
        /// <summary>
        /// Gets the unique identifier for the role.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>
        /// Gets the name of the role.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the normalized name of the role for case-insensitive comparisons.
        /// </summary>
        public string NormalizedName { get; init; }

    }
}