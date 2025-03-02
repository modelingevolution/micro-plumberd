using System.Collections.Immutable;
using System.Security.Claims;

namespace MicroPlumberd.Service.Identity.Aggregates;

[Aggregate]
public partial class AuthorizationUserAggregate : AggregateBase<UserIdentifier, AuthorizationUserAggregate.AuthorizationUserState>
{
    public AuthorizationUserAggregate(UserIdentifier id) : base(id) { }

    public record AuthorizationUserState
    {
        public UserIdentifier Id { get; init; }
        public ImmutableList<RoleIdentifier> Roles { get; init; } = ImmutableList<RoleIdentifier>.Empty;
        public ImmutableList<ClaimRecord> Claims { get; init; } = ImmutableList<ClaimRecord>.Empty;
        public string ConcurrencyStamp { get; init; }
        public bool IsDeleted { get; init; }
    }

    public record ClaimRecord
    {
        public ClaimType Type { get; init; }
        public ClaimValue Value { get; init; }
    }

    // Event application methods
    private static AuthorizationUserState Given(AuthorizationUserState state, AuthorizationUserCreated ev)
    {
        return new AuthorizationUserState
        {
            Id = ev.UserId,
            ConcurrencyStamp = ev.ConcurrencyStamp,
            IsDeleted = false
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, RoleAdded ev)
    {
        if (state.Roles.Contains(ev.RoleId))
            return state;

        return state with
        {
            Roles = state.Roles.Add(ev.RoleId),
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, RoleRemoved ev)
    {
        return state with
        {
            Roles = state.Roles.Remove(ev.RoleId),
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, ClaimAdded ev)
    {
        var claimRecord = new ClaimRecord
        {
            Type = ev.ClaimType,
            Value = ev.ClaimValue
        };

        // Check if the claim already exists with this type and value
        bool claimExists = state.Claims.Any(c =>
            c.Type.Value == ev.ClaimType.Value &&
            c.Value.Value == ev.ClaimValue.Value);

        if (claimExists)
            return state;

        return state with
        {
            Claims = state.Claims.Add(claimRecord),
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, ClaimRemoved ev)
    {
        // Find all claims with matching type and value
        var claimsToRemove = state.Claims
            .Where(c =>
                c.Type.Value == ev.ClaimType.Value &&
                c.Value.Value == ev.ClaimValue.Value)
            .ToList();

        if (!claimsToRemove.Any())
            return state;

        var newClaims = state.Claims;
        foreach (var claim in claimsToRemove)
        {
            newClaims = newClaims.Remove(claim);
        }

        return state with
        {
            Claims = newClaims,
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, ClaimsReplaced ev)
    {
        // Convert the list of claims to ClaimRecord objects
        var newClaims = ev.Claims.Select(c => new ClaimRecord
        {
            Type = new ClaimType(c.Type),
            Value = new ClaimValue(c.Value)
        }).ToImmutableList();

        return state with
        {
            Claims = newClaims,
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, ConcurrencyStampChanged ev)
    {
        return state with { ConcurrencyStamp = ev.ConcurrencyStamp };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, AuthorizationUserDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    // Command methods
    public static AuthorizationUserAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new AuthorizationUserCreated
        {
            Id = Guid.NewGuid(),
            UserId = id,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });

        return aggregate;
    }

    public void AddRole(RoleIdentifier roleId, string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        if (State.Roles.Contains(roleId))
            return; // Role already added

        AppendPendingChange(new RoleAdded
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void RemoveRole(RoleIdentifier roleId, string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        if (!State.Roles.Contains(roleId))
            return; // Role not present

        AppendPendingChange(new RoleRemoved
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void AddClaim(ClaimType claimType, ClaimValue claimValue, string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        // Check if claim already exists
        bool claimExists = State.Claims.Any(c =>
            c.Type.Value == claimType.Value &&
            c.Value.Value == claimValue.Value);

        if (claimExists)
            return; // Claim already added

        AppendPendingChange(new ClaimAdded
        {
            Id = Guid.NewGuid(),
            ClaimType = claimType,
            ClaimValue = claimValue,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void RemoveClaim(ClaimType claimType, ClaimValue claimValue, string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        // Check if any claim matches
        bool claimExists = State.Claims.Any(c =>
            c.Type.Value == claimType.Value &&
            c.Value.Value == claimValue.Value);

        if (!claimExists)
            return; // No matching claim

        AppendPendingChange(new ClaimRemoved
        {
            Id = Guid.NewGuid(),
            ClaimType = claimType,
            ClaimValue = claimValue,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void ReplaceClaims(IEnumerable<Claim> claims, string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        AppendPendingChange(new ClaimsReplaced
        {
            Id = Guid.NewGuid(),
            Claims = claims.ToList(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void UpdateConcurrencyStamp()
    {
        EnsureNotDeleted();

        AppendPendingChange(new ConcurrencyStampChanged
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void Delete(string expectedConcurrencyStamp)
    {
        if (State.IsDeleted)
            return;

        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        AppendPendingChange(new AuthorizationUserDeleted
        {
            Id = Guid.NewGuid()
        });
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify authorization data of a deleted user");
    }

    private void ValidateConcurrencyStamp(string expectedConcurrencyStamp)
    {
        if (expectedConcurrencyStamp != null &&
            State.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            throw new ConcurrencyException("User authorization data was modified by another process");
        }
    }
}