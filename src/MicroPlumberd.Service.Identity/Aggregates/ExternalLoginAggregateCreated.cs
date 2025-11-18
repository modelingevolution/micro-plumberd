using System;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Event raised when a new external login aggregate is created.
    /// </summary>
    [OutputStream("ExternalLogin")]
    public record ExternalLoginAggregateCreated
    {
        /// <summary>
        /// Gets the unique identifier for the external login aggregate.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();


    }
}