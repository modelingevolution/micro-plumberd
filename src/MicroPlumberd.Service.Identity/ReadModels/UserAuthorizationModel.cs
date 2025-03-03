using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Collections;
using MicroPlumberd.Services.Identity.Aggregates;

namespace MicroPlumberd.Services.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserAuthorizationModel_v1")]
    public partial class UserAuthorizationModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, UserAuthData> _authDataByUserId = new();
        private readonly ConcurrentDictionary<RoleIdentifier, RoleData> _roleData = new();
        private readonly RolesModel _rolesModel;

        public UserAuthorizationModel(RolesModel rolesModel)
        {
            _rolesModel = rolesModel ?? throw new ArgumentNullException(nameof(rolesModel));
        }

        public record RoleData
        {
            public RoleIdentifier Id { get; init; }
            public string Name { get; init; }
            public ConcurrentHashSet<UserIdentifier> Users { get; } = new();
        }

        public record UserAuthData
        {
            public UserIdentifier Id { get; init; }
            public ImmutableList<RoleData> Roles { get; init; } = ImmutableList<RoleData>.Empty;
            public ImmutableList<Claim> Claims { get; init; } = ImmutableList<Claim>.Empty;
        }

        #region Authorization User Events

        private async Task Given(Metadata m, AuthorizationUserCreated ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _authDataByUserId[userId] = new UserAuthData
            {
                Id = userId
            };

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleAdded ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                // Get or create role data
                var roleData = _roleData.GetOrAdd(ev.RoleId, id =>
                {
                    var role = _rolesModel.GetById(id);
                    return new RoleData
                    {
                        Id = id,
                        Name = role?.Name ?? string.Empty
                    };
                });

                // Only add if not already present
                bool roleExists = authData.Roles.Any(r => r.Id.Equals(ev.RoleId));

                if (!roleExists)
                {
                    // Update roles for user
                    _authDataByUserId[userId] = authData with
                    {
                        Roles = authData.Roles.Add(roleData)
                    };

                    // Add user to role's user collection
                    roleData.Users.Add(userId);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleRemoved ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                // Find the role
                var roleToRemove = authData.Roles.FirstOrDefault(r => r.Id.Equals(ev.RoleId));

                if (roleToRemove != null)
                {
                    // Update roles for user
                    _authDataByUserId[userId] = authData with
                    {
                        Roles = authData.Roles.Remove(roleToRemove)
                    };

                    // Remove user from role's user collection
                    if (_roleData.TryGetValue(ev.RoleId, out var roleData))
                    {
                        roleData.Users.TryRemove(userId);
                    }
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ClaimAdded ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                var newClaim = new Claim(ev.ClaimType.Value, ev.ClaimValue.Value);

                // Check if claim already exists
                bool claimExists = authData.Claims.Any(c =>
                    c.Type == ev.ClaimType.Value &&
                    c.Value == ev.ClaimValue.Value);

                if (!claimExists)
                {
                    _authDataByUserId[userId] = authData with
                    {
                        Claims = authData.Claims.Add(newClaim)
                    };
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ClaimRemoved ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                // Find claims to remove
                var claimsToRemove = authData.Claims
                    .Where(c =>
                        c.Type == ev.ClaimType.Value &&
                        c.Value == ev.ClaimValue.Value)
                    .ToList();

                if (claimsToRemove.Any())
                {
                    var newClaims = authData.Claims;
                    foreach (var claim in claimsToRemove)
                    {
                        newClaims = newClaims.Remove(claim);
                    }

                    _authDataByUserId[userId] = authData with
                    {
                        Claims = newClaims
                    };
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ClaimsReplaced ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                _authDataByUserId[userId] = authData with
                {
                    Claims = ev.Claims.ToImmutableList()
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, AuthorizationUserDeleted ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryRemove(userId, out var authData))
            {
                // Remove user from all roles
                foreach (var roleData in authData.Roles)
                {
                    roleData.Users.TryRemove(userId);
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Role Events

        private async Task Given(Metadata m, RoleCreated ev)
        {
            var roleId = m.StreamId<RoleIdentifier>();
            // Add or update role data
            _roleData.AddOrUpdate(
                roleId,
                new RoleData { Id = roleId, Name = ev.Name },
                (_, existing) => existing with { Name = ev.Name }
            );

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleNameChanged ev)
        {
            var roleId = m.StreamId<RoleIdentifier>();

            // Update role data
            if (_roleData.TryGetValue(roleId, out var roleData))
            {
                // Create new role data with updated name
                var updatedRoleData = new RoleData
                {
                    Id = roleId,
                    Name = ev.Name
                };

                // Copy users from old role data
                foreach (var userId in roleData.Users)
                {
                    updatedRoleData.Users.Add(userId);
                }

                // Replace role data
                _roleData[roleId] = updatedRoleData;

                // Update role name in all user auth data that contains this role
                foreach (var userId in roleData.Users)
                {
                    if (_authDataByUserId.TryGetValue(userId, out var userData))
                    {
                        // Find and replace the role
                        var roleIndex = userData.Roles.FindIndex(r => r.Id.Equals(roleId));
                        if (roleIndex >= 0)
                        {
                            var newRoles = userData.Roles.RemoveAt(roleIndex).Add(updatedRoleData);
                            _authDataByUserId[userId] = userData with { Roles = newRoles };
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleDeleted ev)
        {
            var roleId = m.StreamId<RoleIdentifier>();

            // Remove role from all users
            if (_roleData.TryRemove(roleId, out var roleData))
            {
                foreach (var userId in roleData.Users)
                {
                    if (_authDataByUserId.TryGetValue(userId, out var userData))
                    {
                        // Find the role
                        var roleToRemove = userData.Roles.FirstOrDefault(r => r.Id.Equals(roleId));
                        if (roleToRemove != null)
                        {
                            // Update roles for user
                            _authDataByUserId[userId] = userData with
                            {
                                Roles = userData.Roles.Remove(roleToRemove)
                            };
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Query Methods
        /// <summary>
        /// Checks if a user is in a role by role name
        /// </summary>
        public bool IsInRole(UserIdentifier userId, string normalizedRoleName)
        {
            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                return authData.Roles.Any(r =>
                    string.Equals(r.Name, normalizedRoleName, StringComparison.OrdinalIgnoreCase) ||
                    (r.Name != null && string.Equals(r.Name.ToUpperInvariant(), normalizedRoleName, StringComparison.Ordinal)));
            }

            return false;
        }

        /// <summary>
        /// Gets authorization data for a user
        /// </summary>
        public UserAuthData GetById(UserIdentifier id)
        {
            _authDataByUserId.TryGetValue(id, out var authData);
            return authData;
        }

        /// <summary>
        /// Gets all users in a role
        /// </summary>
        public ImmutableList<UserIdentifier> GetUsersInRole(RoleIdentifier roleId)
        {
            if (_roleData.TryGetValue(roleId, out var roleData))
            {
                return roleData.Users.ToImmutableList();
            }

            return ImmutableList<UserIdentifier>.Empty;
        }

        /// <summary>
        /// Gets all users with a specific role name
        /// </summary>
        public ImmutableList<UserIdentifier> GetUsersInRole(string normalizedRoleName)
        {
            var role = _rolesModel.GetByNormalizedName(normalizedRoleName);
            if (role != null)
            {
                var roleId = RoleIdentifier.Parse(role.Id, null);
                return GetUsersInRole(roleId);
            }

            return ImmutableList<UserIdentifier>.Empty;
        }

        /// <summary>
        /// Gets all claims for a user
        /// </summary>
        public ImmutableList<Claim> GetClaims(UserIdentifier id)
        {
            if (_authDataByUserId.TryGetValue(id, out var authData))
            {
                return authData.Claims;
            }

            return ImmutableList<Claim>.Empty;
        }

        /// <summary>
        /// Gets all role names for a user
        /// </summary>
        public ImmutableList<string> GetRoleNames(UserIdentifier id)
        {
            if (_authDataByUserId.TryGetValue(id, out var authData))
            {
                return authData.Roles.Select(r => r.Name).ToImmutableList();
            }

            return ImmutableList<string>.Empty;
        }

        /// <summary>
        /// Checks if a user is in a role
        /// </summary>
        public bool IsInRole(UserIdentifier id, RoleIdentifier roleId)
        {
            if (_authDataByUserId.TryGetValue(id, out var authData))
            {
                return authData.Roles.Any(r => r.Id.Equals(roleId));
            }

            return false;
        }

        #endregion
    }
}