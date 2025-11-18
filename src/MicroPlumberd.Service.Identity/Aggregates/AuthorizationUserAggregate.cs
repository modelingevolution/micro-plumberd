using System.Collections.Immutable;
using System.Security.Claims;

namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Aggregate root managing user authorization including roles and claims.
/// </summary>
[Aggregate]
[OutputStream("Authorization")]
public partial class AuthorizationUserAggregate : AggregateBase<UserIdentifier, AuthorizationUserAggregate.AuthorizationUserState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationUserAggregate"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    public AuthorizationUserAggregate(UserIdentifier id) : base(id) { }

    /// <summary>
    /// Represents the authorization state of a user including their roles and claims.
    /// </summary>
    public record AuthorizationUserState
    {
        /// <summary>
        /// Gets the list of role identifiers assigned to the user.
        /// </summary>
        public ImmutableList<RoleIdentifier> Roles { get; init; } = ImmutableList<RoleIdentifier>.Empty;

        /// <summary>
        /// Gets the list of claims assigned to the user.
        /// </summary>
        public ImmutableList<ClaimRecord> Claims { get; init; } = ImmutableList<ClaimRecord>.Empty;

        /// <summary>
        /// Gets a value indicating whether this user's authorization profile has been deleted.
        /// </summary>
        public bool IsDeleted { get; init; }
    }

    /// <summary>
    /// Represents a claim record with type and value.
    /// </summary>
    public record ClaimRecord
    {
        /// <summary>
        /// Gets the type of the claim.
        /// </summary>
        public ClaimType Type { get; init; }

        /// <summary>
        /// Gets the value of the claim.
        /// </summary>
        public ClaimValue Value { get; init; }
    }

    // Event application methods
    private static AuthorizationUserState Given(AuthorizationUserState state, AuthorizationUserCreated ev)
    {
        return new AuthorizationUserState
        {
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
        };
    }

    private static AuthorizationUserState Given(AuthorizationUserState state, RoleRemoved ev)
    {
        return state with
        {
            Roles = state.Roles.Remove(ev.RoleId),
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
        };
    }


    private static AuthorizationUserState Given(AuthorizationUserState state, AuthorizationUserDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    /// <summary>
    /// Creates a new authorization user aggregate for the specified user.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    /// <returns>A new <see cref="AuthorizationUserAggregate"/> instance.</returns>
    public static AuthorizationUserAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new AuthorizationUserCreated());

        return aggregate;
    }

    /// <summary>
    /// Adds a role to the user's authorization profile.
    /// </summary>
    /// <param name="roleId">The identifier of the role to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify authorization data of a deleted user.</exception>
    public void AddRole(RoleIdentifier roleId)
    {
        EnsureNotDeleted();

        if (State.Roles.Contains(roleId))
            return; // Role already added

        AppendPendingChange(new RoleAdded
        {
            RoleId = roleId,
            
        });
    }

    /// <summary>
    /// Removes a role from the user's authorization profile.
    /// </summary>
    /// <param name="roleId">The identifier of the role to remove.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify authorization data of a deleted user.</exception>
    public void RemoveRole(RoleIdentifier roleId)
    {
        EnsureNotDeleted();
        
        if (!State.Roles.Contains(roleId))
            return; // Role not present

        AppendPendingChange(new RoleRemoved
        {
            RoleId = roleId,
            
        });
    }

    /// <summary>
    /// Adds a claim to the user's authorization profile.
    /// </summary>
    /// <param name="claimType">The type of the claim to add.</param>
    /// <param name="claimValue">The value of the claim to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify authorization data of a deleted user.</exception>
    public void AddClaim(ClaimType claimType, ClaimValue claimValue)
    {
        EnsureNotDeleted();
        
        // Check if claim already exists
        bool claimExists = State.Claims.Any(c =>
            c.Type.Value == claimType.Value &&
            c.Value.Value == claimValue.Value);

        if (claimExists)
            return; // Claim already added

        AppendPendingChange(new ClaimAdded
        {
            ClaimType = claimType,
            ClaimValue = claimValue,
            
        });
    }

    /// <summary>
    /// Removes a claim from the user's authorization profile.
    /// </summary>
    /// <param name="claimType">The type of the claim to remove.</param>
    /// <param name="claimValue">The value of the claim to remove.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify authorization data of a deleted user.</exception>
    public void RemoveClaim(ClaimType claimType, ClaimValue claimValue)
    {
        EnsureNotDeleted();
        

        // Check if any claim matches
        bool claimExists = State.Claims.Any(c =>
            c.Type.Value == claimType.Value &&
            c.Value.Value == claimValue.Value);

        if (!claimExists)
            return; // No matching claim

        AppendPendingChange(new ClaimRemoved
        {
            ClaimType = claimType,
            ClaimValue = claimValue,
            
        });
    }

    /// <summary>
    /// Replaces all claims for the user with the specified collection of claims.
    /// </summary>
    /// <param name="claims">The new collection of claims to set for the user.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify authorization data of a deleted user.</exception>
    public void ReplaceClaims(IEnumerable<Claim> claims)
    {
        EnsureNotDeleted();
        

        AppendPendingChange(new ClaimsReplaced
        {
            Claims = claims.ToList(),
            
        });
    }


    /// <summary>
    /// Marks the authorization user aggregate as deleted.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted)
            return;

        AppendPendingChange(new AuthorizationUserDeleted
        {
        });
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify authorization data of a deleted user");
    }

   
}