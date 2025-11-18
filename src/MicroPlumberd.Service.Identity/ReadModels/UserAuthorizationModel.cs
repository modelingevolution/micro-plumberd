using MicroPlumberd.Collections;
using MicroPlumberd.Services.Identity.Aggregates;
using MicroPlumberd.Services.Identity;
using MicroPlumberd;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Claims;

/// <summary>
/// Read model maintaining user authorization data including roles and claims with efficient lookups.
/// </summary>
[EventHandler]
[OutputStream("UserAuthorizationModel_v1")]
public partial class UserAuthorizationModel
{
    // Primary user authorization data store
    private readonly ConcurrentDictionary<UserIdentifier, UserAuthData> _userAuthData = new();

    // Role information store (maintained from role events)
    private readonly ConcurrentDictionary<RoleIdentifier, RoleInfo> _roleInfo = new();

    // Lookup indices
    private readonly ConcurrentDictionary<string, RoleIdentifier> _roleIdsByNormalizedName = new();
    private readonly ConcurrentDictionary<RoleIdentifier, ConcurrentHashSet<UserIdentifier>> _usersByRole = new();

    /// <summary>
    /// Represents user authorization data including roles and claims.
    /// </summary>
    record UserAuthData
    {
        /// <summary>
        /// Gets the user identifier.
        /// </summary>
        public UserIdentifier Id { get; init; }

        /// <summary>
        /// Gets the set of role identifiers assigned to the user.
        /// </summary>
        public ImmutableHashSet<RoleIdentifier> RoleIds { get; init; } = ImmutableHashSet<RoleIdentifier>.Empty;

        /// <summary>
        /// Gets the claims organized by type, with each type containing a set of values.
        /// </summary>
        public ImmutableDictionary<string, ImmutableHashSet<string>> ClaimsByType { get; init; } =
            ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;
    }

    /// <summary>
    /// Represents role information maintained in the read model.
    /// </summary>
    record RoleInfo
    {
        /// <summary>
        /// Gets the role name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the normalized role name for case-insensitive lookups.
        /// </summary>
        public string NormalizedName { get; init; }
    }

    #region Authorization User Events

    private async Task Given(Metadata m, AuthorizationUserCreated ev)
    {
        var userId = m.StreamId<UserIdentifier>();
        _userAuthData[userId] = new UserAuthData { Id = userId };
        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, RoleAdded ev)
    {
        var userId = m.StreamId<UserIdentifier>();
        var roleId = ev.RoleId;

        if (!_userAuthData.TryGetValue(userId, out var userData))
            return;

        // Only add if not already present
        if (!userData.RoleIds.Contains(roleId))
        {
            // Update user's roles
            _userAuthData[userId] = userData with
            {
                RoleIds = userData.RoleIds.Add(roleId)
            };

            // Update users-by-role index
            var usersInRole = _usersByRole.GetOrAdd(roleId, _ => new ConcurrentHashSet<UserIdentifier>());
            usersInRole.Add(userId);
        }

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, RoleRemoved ev)
    {
        var userId = m.StreamId<UserIdentifier>();
        var roleId = ev.RoleId;

        if (!_userAuthData.TryGetValue(userId, out var userData))
            return;

        // Only remove if actually present
        if (userData.RoleIds.Contains(roleId))
        {
            // Update user's roles
            _userAuthData[userId] = userData with
            {
                RoleIds = userData.RoleIds.Remove(roleId)
            };

            // Update users-by-role index
            if (_usersByRole.TryGetValue(roleId, out var usersInRole))
            {
                usersInRole.TryRemove(userId);
            }
        }

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, ClaimAdded ev)
    {
        var userId = m.StreamId<UserIdentifier>();
        var claimType = ev.ClaimType.Value;
        var claimValue = ev.ClaimValue.Value;

        if (!_userAuthData.TryGetValue(userId, out var userData))
            return;

        // Get existing claims for this type
        userData.ClaimsByType.TryGetValue(claimType, out var existingValues);
        var values = existingValues ?? ImmutableHashSet<string>.Empty;

        // Add the new claim value if not already present
        if (!values.Contains(claimValue))
        {
            var newValues = values.Add(claimValue);
            var newClaimsByType = userData.ClaimsByType.SetItem(claimType, newValues);

            _userAuthData[userId] = userData with
            {
                ClaimsByType = newClaimsByType
            };
        }

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, ClaimRemoved ev)
    {
        var userId = m.StreamId<UserIdentifier>();
        var claimType = ev.ClaimType.Value;
        var claimValue = ev.ClaimValue.Value;

        if (!_userAuthData.TryGetValue(userId, out var userData))
            return;

        // Get existing claims for this type
        if (userData.ClaimsByType.TryGetValue(claimType, out var values))
        {
            // Remove the claim value
            var newValues = values.Remove(claimValue);

            var newClaimsByType = userData.ClaimsByType;
            if (newValues.IsEmpty)
            {
                // If no more values of this type, remove the type entry
                newClaimsByType = newClaimsByType.Remove(claimType);
            }
            else
            {
                // Otherwise update with new values
                newClaimsByType = newClaimsByType.SetItem(claimType, newValues);
            }

            _userAuthData[userId] = userData with
            {
                ClaimsByType = newClaimsByType
            };
        }

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, ClaimsReplaced ev)
    {
        var userId = m.StreamId<UserIdentifier>();

        if (!_userAuthData.TryGetValue(userId, out var userData))
            return;

        // Group claims by type for efficient storage
        var claimsByType = ev.Claims
            .GroupBy(c => c.Type)
            .ToImmutableDictionary(
                g => g.Key,
                g => g.Select(c => c.Value).ToImmutableHashSet());

        _userAuthData[userId] = userData with
        {
            ClaimsByType = claimsByType
        };

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, AuthorizationUserDeleted ev)
    {
        var userId = m.StreamId<UserIdentifier>();

        if (_userAuthData.TryRemove(userId, out var userData))
        {
            // Remove user from all role indices
            foreach (var roleId in userData.RoleIds)
            {
                if (_usersByRole.TryGetValue(roleId, out var usersInRole))
                {
                    usersInRole.TryRemove(userId);
                }
            }
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Role Events

    // Subscribe directly to role events to maintain role information
    private async Task Given(Metadata m, RoleCreated ev)
    {
        var roleId = m.StreamId<RoleIdentifier>(); // Using the event payload directly since this is not from role's stream

        _roleInfo[roleId] = new RoleInfo
        {
            Name = ev.Name,
            NormalizedName = ev.NormalizedName
        };

        if (!string.IsNullOrEmpty(ev.NormalizedName))
            _roleIdsByNormalizedName[ev.NormalizedName] = roleId;

        // Initialize empty users set for this role
        _usersByRole.TryAdd(roleId, new ConcurrentHashSet<UserIdentifier>());

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, RoleNameChanged ev)
    {
        var roleId = m.StreamId<RoleIdentifier>();

        if (_roleInfo.TryGetValue(roleId, out var oldRoleInfo))
        {
            // Remove old normalized name entry if it exists
            if (!string.IsNullOrEmpty(oldRoleInfo.NormalizedName))
                _roleIdsByNormalizedName.TryRemove(oldRoleInfo.NormalizedName, out _);
        }

        // Add new role info
        _roleInfo[roleId] = new RoleInfo
        {
            Name = ev.Name,
            NormalizedName = ev.NormalizedName
        };

        // Update normalized name index
        if (!string.IsNullOrEmpty(ev.NormalizedName))
            _roleIdsByNormalizedName[ev.NormalizedName] = roleId;

        await Task.CompletedTask;
    }

    private async Task Given(Metadata m, RoleDeleted ev)
    {
        var roleId = m.StreamId<RoleIdentifier>();

        // Remove role information
        if (_roleInfo.TryRemove(roleId, out var roleInfo))
        {
            // Remove from normalized name index
            if (!string.IsNullOrEmpty(roleInfo.NormalizedName))
                _roleIdsByNormalizedName.TryRemove(roleInfo.NormalizedName, out _);
        }

        // Get users in this role before removing the index
        if (_usersByRole.TryRemove(roleId, out var usersInRole))
        {
            // Remove role from all users who had it
            foreach (var userId in usersInRole)
            {
                if (_userAuthData.TryGetValue(userId, out var userData))
                {
                    _userAuthData[userId] = userData with
                    {
                        RoleIds = userData.RoleIds.Remove(roleId)
                    };
                }
            }
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Query Methods

    /// <summary>
    /// Determines whether a user is in a specific role by normalized role name.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="normalizedRoleName">The normalized role name to check.</param>
    /// <returns>True if the user is in the role; otherwise, false.</returns>
    public bool IsInRole(UserIdentifier userId, string normalizedRoleName)
    {
        // First, get the role ID from the normalized name
        if (!_roleIdsByNormalizedName.TryGetValue(normalizedRoleName, out var roleId))
            return false;

        // Then check if the user has this role ID
        return IsInRole(userId, roleId);
    }

    /// <summary>
    /// Determines whether a user is in a specific role by role identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleId">The role identifier to check.</param>
    /// <returns>True if the user is in the role; otherwise, false.</returns>
    public bool IsInRole(UserIdentifier userId, RoleIdentifier roleId)
    {
        return _userAuthData.TryGetValue(userId, out var userData) &&
               userData.RoleIds.Contains(roleId);
    }

    /// <summary>
    /// Gets the names of all roles assigned to a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>An immutable list of role names.</returns>
    public ImmutableList<string> GetRoleNames(UserIdentifier userId)
    {
        if (!_userAuthData.TryGetValue(userId, out var userData))
            return ImmutableList<string>.Empty;

        return userData.RoleIds
            .Select(roleId => _roleInfo.TryGetValue(roleId, out var info) ? info.Name : null)
            .Where(name => name != null)
            .ToImmutableList();
    }

    /// <summary>
    /// Gets all users assigned to a specific role by role identifier.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <returns>An immutable list of user identifiers.</returns>
    public ImmutableList<UserIdentifier> GetUsersInRole(RoleIdentifier roleId)
    {
        if (_usersByRole.TryGetValue(roleId, out var users))
            return users.ToImmutableList();

        return ImmutableList<UserIdentifier>.Empty;
    }

    /// <summary>
    /// Gets all users assigned to a specific role by normalized role name.
    /// </summary>
    /// <param name="normalizedRoleName">The normalized role name.</param>
    /// <returns>An immutable list of user identifiers.</returns>
    public ImmutableList<UserIdentifier> GetUsersInRole(string normalizedRoleName)
    {
        if (_roleIdsByNormalizedName.TryGetValue(normalizedRoleName, out var roleId))
            return GetUsersInRole(roleId);

        return ImmutableList<UserIdentifier>.Empty;
    }

    /// <summary>
    /// Gets all claims for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>An immutable list of claims.</returns>
    public ImmutableList<Claim> GetClaims(UserIdentifier userId)
    {
        if (!_userAuthData.TryGetValue(userId, out var userData))
            return ImmutableList<Claim>.Empty;

        return userData.ClaimsByType
            .SelectMany(kvp =>
                kvp.Value.Select(value => new Claim(kvp.Key, value)))
            .ToImmutableList();
    }

    #endregion
}