using System;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    /// <summary>
    /// Event raised when a new token aggregate is created.
    /// </summary>
    [OutputStream("Token")]
    public record TokenAggregateCreated
    {
        /// <summary>
        /// Gets the unique identifier for the token aggregate.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();


    }
}