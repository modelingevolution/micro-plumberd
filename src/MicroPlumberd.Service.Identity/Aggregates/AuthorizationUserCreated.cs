using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;


namespace MicroPlumberd.Services.Identity.Aggregates
{
    // Events
    [OutputStream("Authorization")]
    public record AuthorizationUserCreated
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public UserIdentifier UserId { get; init; }
    }
}