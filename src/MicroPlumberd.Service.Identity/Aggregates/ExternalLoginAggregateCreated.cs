using System;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Service.Identity.Aggregates
{
    // Events
    [OutputStream("ExternalLogin")]
    public record ExternalLoginAggregateCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public UserIdentifier UserId { get; init; }
        
    }
}