using System;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Service.Identity.Aggregates
{
    [OutputStream("Identity")]
    public record IdentityUserCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public UserIdentifier UserId { get; init; }
        public string PasswordHash { get; init; }
        public string SecurityStamp { get; init; }
        public bool LockoutEnabled { get; init; }
        public string ConcurrencyStamp { get; init; }
    }

    // Exception for concurrency conflicts
}