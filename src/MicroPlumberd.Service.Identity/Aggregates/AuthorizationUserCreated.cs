using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Event raised when a new authorization profile is created for a user.
    /// </summary>
    [OutputStream("Authorization")]
    public record AuthorizationUserCreated
    {
        /// <summary>
        /// Gets the unique identifier for the user's authorization profile.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

    }
}