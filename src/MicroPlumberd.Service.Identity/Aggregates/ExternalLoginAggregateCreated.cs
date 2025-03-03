using System;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    // Events
    [OutputStream("ExternalLogin")]
    public record ExternalLoginAggregateCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        
        
    }
}