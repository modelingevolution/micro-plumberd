using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    /// <summary>
    /// Consolidated read model for users with multiple lookup dictionaries
    /// </summary>
    [EventHandler]
    [OutputStream("UsersModel_v1")]
    public partial class UsersModel
    {
        // Primary collection - clustered index
        private readonly ConcurrentDictionary<UserIdentifier, User> _usersById = new();

        // Lookup dictionaries - direct references to the same User objects
        private readonly ConcurrentDictionary<string, User> _usersByNormalizedName = new();
        private readonly ConcurrentDictionary<string, User> _usersByNormalizedEmail = new();
        private readonly ConcurrentDictionary<string, User> _usersByExternalLogin = new();

        // Returns all users (for IQueryable interface)
        public ImmutableList<User> GetAllUsers() => _usersById.Values.ToImmutableList();

        #region Event Handlers

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            // Create a new user object
            var user = new User
            {
                Id = ev.UserId.ToString(),
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = ev.PhoneNumber,
                PhoneNumberConfirmed = false
            };

            // Add to primary collection
            if (_usersById.TryAdd(ev.UserId, user))
            {
                // Add to lookups - same reference
                if (!string.IsNullOrEmpty(ev.NormalizedUserName))
                {
                    _usersByNormalizedName.TryAdd(ev.NormalizedUserName, user);
                }

                if (!string.IsNullOrEmpty(ev.NormalizedEmail))
                {
                    _usersByNormalizedEmail.TryAdd(ev.NormalizedEmail, user);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, IdentityUserCreated ev)
        {
            if (_usersById.TryGetValue(ev.UserId, out var user))
            {
                user.PasswordHash = ev.PasswordHash;
                user.SecurityStamp = ev.SecurityStamp;
                user.LockoutEnabled = ev.LockoutEnabled;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserNameChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                // Remove from old lookup
                if (!string.IsNullOrEmpty(user.NormalizedUserName))
                {
                    _usersByNormalizedName.TryRemove(user.NormalizedUserName, out _);
                }

                // Update the user
                user.UserName = ev.UserName;
                user.NormalizedUserName = ev.NormalizedUserName;

                // Add to lookup with updated normalized name
                if (!string.IsNullOrEmpty(ev.NormalizedUserName))
                {
                    _usersByNormalizedName.TryAdd(ev.NormalizedUserName, user);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                // Remove from old lookup
                if (!string.IsNullOrEmpty(user.NormalizedEmail))
                {
                    _usersByNormalizedEmail.TryRemove(user.NormalizedEmail, out _);
                }

                // Update the user
                user.Email = ev.Email;
                user.NormalizedEmail = ev.NormalizedEmail;
                user.EmailConfirmed = false; // Reset confirmation on email change

                // Add to lookup with updated normalized email
                if (!string.IsNullOrEmpty(ev.NormalizedEmail))
                {
                    _usersByNormalizedEmail.TryAdd(ev.NormalizedEmail, user);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.EmailConfirmed = true;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PhoneNumber = ev.PhoneNumber;
                user.PhoneNumberConfirmed = false; // Reset confirmation on phone change
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberConfirmed ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PhoneNumberConfirmed = true;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PasswordChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.PasswordHash = ev.PasswordHash;
                user.SecurityStamp = ev.SecurityStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, SecurityStampChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.SecurityStamp = ev.SecurityStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TwoFactorChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.TwoFactorEnabled = ev.TwoFactorEnabled;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEnabledChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.LockoutEnabled = ev.LockoutEnabled;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEndChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.LockoutEnd = ev.LockoutEnd;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, AccessFailedCountChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                user.AccessFailedCount = ev.AccessFailedCount;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginAdded ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_usersById.TryGetValue(userId, out var user))
            {
                // Create a composite key for the external login
                string loginKey = GetExternalLoginKey(ev.Provider.Name, ev.ProviderKey.Value);

                // Add to lookup - same reference to the user
                _usersByExternalLogin.TryAdd(loginKey, user);
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginRemoved ev)
        {
            // Create a composite key for the external login
            string loginKey = GetExternalLoginKey(ev.Provider.Name, ev.ProviderKey.Value);

            // Remove from lookup
            _usersByExternalLogin.TryRemove(loginKey, out _);

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);

            // Remove from primary collection
            if (_usersById.TryRemove(userId, out var user))
            {
                // Remove from lookups
                if (!string.IsNullOrEmpty(user.NormalizedUserName))
                {
                    _usersByNormalizedName.TryRemove(user.NormalizedUserName, out _);
                }

                if (!string.IsNullOrEmpty(user.NormalizedEmail))
                {
                    _usersByNormalizedEmail.TryRemove(user.NormalizedEmail, out _);
                }

                // Remove all external login entries - need to scan
                foreach (var loginKey in _usersByExternalLogin.Where(kvp => ReferenceEquals(kvp.Value, user))
                    .Select(kvp => kvp.Key).ToList())
                {
                    _usersByExternalLogin.TryRemove(loginKey, out _);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, IdentityUserDeleted ev)
        {
            // User is already handled in UserProfileDeleted
            await Task.CompletedTask;
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Gets a user by ID
        /// </summary>
        public User GetById(UserIdentifier id)
        {
            _usersById.TryGetValue(id, out var user);
            return user;
        }

        /// <summary>
        /// Gets a user by normalized username
        /// </summary>
        public User GetByNormalizedUserName(string normalizedUserName)
        {
            if (string.IsNullOrEmpty(normalizedUserName))
                return null;

            _usersByNormalizedName.TryGetValue(normalizedUserName, out var user);
            return user;
        }

        /// <summary>
        /// Gets a user by normalized email
        /// </summary>
        public User GetByNormalizedEmail(string normalizedEmail)
        {
            if (string.IsNullOrEmpty(normalizedEmail))
                return null;

            _usersByNormalizedEmail.TryGetValue(normalizedEmail, out var user);
            return user;
        }

        /// <summary>
        /// Gets a user by external login provider and key
        /// </summary>
        public User GetByExternalLogin(string loginProvider, string providerKey)
        {
            if (string.IsNullOrEmpty(loginProvider) || string.IsNullOrEmpty(providerKey))
                return null;

            string loginKey = GetExternalLoginKey(loginProvider, providerKey);

            _usersByExternalLogin.TryGetValue(loginKey, out var user);
            return user;
        }

        /// <summary>
        /// Gets a user ID by normalized username
        /// </summary>
        public UserIdentifier GetIdByNormalizedUserName(string normalizedUserName)
        {
            var user = GetByNormalizedUserName(normalizedUserName);
            return user != null ? GetUserIdentifier(user.Id) : default;
        }

        /// <summary>
        /// Gets a user ID by normalized email
        /// </summary>
        public UserIdentifier GetIdByNormalizedEmail(string normalizedEmail)
        {
            var user = GetByNormalizedEmail(normalizedEmail);
            return user != null ? GetUserIdentifier(user.Id) : default;
        }

        /// <summary>
        /// Gets a user ID by external login provider and key
        /// </summary>
        public UserIdentifier GetIdByExternalLogin(string loginProvider, string providerKey)
        {
            var user = GetByExternalLogin(loginProvider, providerKey);
            return user != null ? GetUserIdentifier(user.Id) : default;
        }

        #endregion

        #region Helper Methods

        private string GetExternalLoginKey(string providerName, string providerKey)
        {
            return $"{providerName}|{providerKey}";
        }

        private UserIdentifier GetUserIdentifier(string userId)
        {
            return UserIdentifier.Parse(userId, null);
        }

        #endregion
    }
}