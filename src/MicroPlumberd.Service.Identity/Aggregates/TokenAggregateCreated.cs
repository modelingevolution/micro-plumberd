using System;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Service.Identity.Aggregates
{
    // Events with Id initialized directly in the record
    [OutputStream("Token")]
    public record TokenAggregateCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public UserIdentifier UserId { get; init; }
        
    }
}