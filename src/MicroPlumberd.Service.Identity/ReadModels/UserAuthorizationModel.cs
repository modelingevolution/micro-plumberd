using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Collections;
using MicroPlumberd.Service.Identity.Aggregates;


namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserAuthorizationModel_v1")]
    public partial class UserAuthorizationModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, UserAuthData> _authDataByUserId = new();
        private readonly ConcurrentDictionary<RoleIdentifier, ConcurrentHashSet<UserIdentifier>> _usersByRole = new();

        public record UserAuthData
        {
            public UserIdentifier Id { get; init; }
            public ImmutableList<RoleIdentifier> Roles { get; init; } = ImmutableList<RoleIdentifier>.Empty;
            public ImmutableList<Claim> Claims { get; init; } = ImmutableList<Claim>.Empty;
        }

        private async Task Given(Metadata m, AuthorizationUserCreated ev)
        {
            _authDataByUserId[ev.UserId] = new UserAuthData
            {
                Id = ev.UserId
            };

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleAdded ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                // Update roles for user
                _authDataByUserId[userId] = authData with
                {
                    Roles = authData.Roles.Add(ev.RoleId)
                };

                // Update users in role
                _usersByRole.GetOrAdd(ev.RoleId, _ => new ConcurrentHashSet<UserIdentifier>())
                    .Add(userId);
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleRemoved ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_authDataByUserId.TryGetValue(userId, out var authData))
            {
                // Update roles for user
                _authDataByUserId[userId] = authData with
                {
                    Roles = authData.Roles.Remove(ev.RoleId)
                };

                // Update users in role
                if (_usersByRole.TryGetValue(ev.RoleId, out var usersInRole))
                {
                    usersInRole.TryRemove(userId);

                    // If no more users in this role, remove the role entry
                    if (usersInRole.IsEmpty)
                    {
                        _usersByRole.TryRemove(ev.RoleId, out _);
                    }
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ClaimAdded ev)
        {
            var userId = new UserIdentifier(m.Id);

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
            var userId = new UserIdentifier(m.Id);

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
            var userId = new UserIdentifier(m.Id);

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
            var userId = new UserIdentifier(m.Id);

            if (_authDataByUserId.TryRemove(userId, out var authData))
            {
                // Remove user from role mappings
                foreach (var roleId in authData.Roles)
                {
                    if (_usersByRole.TryGetValue(roleId, out var usersInRole))
                    {
                        usersInRole.TryRemove(userId);

                        // If no more users in this role, remove the role entry
                        if (usersInRole.IsEmpty)
                        {
                            _usersByRole.TryRemove(roleId, out _);
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        // Query methods
        public UserAuthData GetById(UserIdentifier id)
        {
            _authDataByUserId.TryGetValue(id, out var authData);
            return authData;
        }

        public ImmutableList<UserIdentifier> GetUsersInRole(RoleIdentifier roleId)
        {
            if (_usersByRole.TryGetValue(roleId, out var usersInRole))
            {
                return usersInRole.ToImmutableList();
            }

            return ImmutableList<UserIdentifier>.Empty;
        }

        public ImmutableList<Claim> GetClaims(UserIdentifier id)
        {
            if (_authDataByUserId.TryGetValue(id, out var authData))
            {
                return authData.Claims;
            }

            return ImmutableList<Claim>.Empty;
        }

        public bool IsInRole(UserIdentifier id, RoleIdentifier roleId)
        {
            if (_authDataByUserId.TryGetValue(id, out var authData))
            {
                return authData.Roles.Contains(roleId);
            }

            return false;
        }
    }
}