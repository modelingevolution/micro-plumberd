using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    [OutputStream("Identity")]
    public record IdentityUserCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public UserIdentifier UserId { get; init; }
        public string PasswordHash { get; init; }
        public string SecurityStamp { get; init; }
        public bool LockoutEnabled { get; init; }
        
    }

    // Exception for concurrency conflicts
}