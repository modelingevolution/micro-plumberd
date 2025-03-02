using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Service.Identity.Aggregates
{
    // Events
    public record RoleCreated
    {
        public Guid Id { get; init; }
        public RoleIdentifier RoleId { get; init; }
        public string Name { get; init; }
        public string NormalizedName { get; init; }
        public string ConcurrencyStamp { get; init; }
    }
}