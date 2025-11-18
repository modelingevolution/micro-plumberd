using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Event raised when a new identity user is created in the system.
    /// </summary>
    [OutputStream("Identity")]
    public record IdentityUserCreated
    {
        /// <summary>
        /// Gets the unique identifier for the user.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>
        /// Gets the hashed password for the user.
        /// </summary>
        public string PasswordHash { get; init; }

        /// <summary>
        /// Gets a value indicating whether lockout is enabled for this user.
        /// </summary>
        public bool LockoutEnabled { get; init; }

    }
}