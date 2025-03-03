using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    // Events
    [OutputStream("Role")]
    public record RoleCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; init; }
        public string NormalizedName { get; init; }
        
    }
}