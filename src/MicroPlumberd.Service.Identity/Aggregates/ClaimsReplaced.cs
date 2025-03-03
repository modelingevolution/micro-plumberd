using System.Security.Claims;

namespace MicroPlumberd.Service.Identity.Aggregates;

[OutputStream("Authorization")]
public record ClaimsReplaced
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<Claim> Claims { get; init; }
    
}