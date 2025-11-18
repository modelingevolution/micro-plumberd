using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    /// <summary>
    /// Read model maintaining external login provider associations for users.
    /// </summary>
    [EventHandler]
    [OutputStream("ExternalLoginModel_v1")]
    public partial class ExternalLoginModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ImmutableList<ExternalLoginInfo>> _loginsByUserId = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByLogin = new();

        /// <summary>
        /// Represents external login information for a user.
        /// </summary>
        public record ExternalLoginInfo
        {
            /// <summary>
            /// Gets the name of the external login provider.
            /// </summary>
            public string ProviderName { get; init; }

            /// <summary>
            /// Gets the provider-specific key for the user.
            /// </summary>
            public string ProviderKey { get; init; }

            /// <summary>
            /// Gets the display name for the external login.
            /// </summary>
            public string DisplayName { get; init; }
        }

        private async Task Given(Metadata m, ExternalLoginAggregateCreated ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _loginsByUserId[userId] = ImmutableList<ExternalLoginInfo>.Empty;
            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginAdded ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            // Add to user's logins
            if (_loginsByUserId.TryGetValue(userId, out var logins))
            {
                var loginInfo = new ExternalLoginInfo
                {
                    ProviderName = ev.Provider.Name,
                    ProviderKey = ev.ProviderKey.Value,
                    DisplayName = ev.DisplayName ?? string.Empty
                };

                _loginsByUserId[userId] = logins.Add(loginInfo);
            }

            // Add to login lookup
            var lookupKey = GetLookupKey(ev.Provider.Name, ev.ProviderKey.Value);
            _userIdsByLogin[lookupKey] = userId;

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginRemoved ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            // Remove from user's logins
            if (_loginsByUserId.TryGetValue(userId, out var logins))
            {
                var loginToRemove = logins.FirstOrDefault(l =>
                    l.ProviderName == ev.Provider.Name &&
                    l.ProviderKey == ev.ProviderKey.Value);

                if (loginToRemove != null)
                {
                    _loginsByUserId[userId] = logins.Remove(loginToRemove);
                }
            }

            // Remove from login lookup
            var lookupKey = GetLookupKey(ev.Provider.Name, ev.ProviderKey.Value);
            _userIdsByLogin.TryRemove(lookupKey, out _);

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginAggregateDeleted ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_loginsByUserId.TryRemove(userId, out var logins))
            {
                // Remove all lookups for this user
                foreach (var login in logins)
                {
                    var lookupKey = GetLookupKey(login.ProviderName, login.ProviderKey);
                    _userIdsByLogin.TryRemove(lookupKey, out _);
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets all external logins configured for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An immutable list of external login information.</returns>
        public ImmutableList<ExternalLoginInfo> GetLoginsForUser(UserIdentifier userId)
        {
            if (_loginsByUserId.TryGetValue(userId, out var logins))
            {
                return logins;
            }

            return ImmutableList<ExternalLoginInfo>.Empty;
        }

        /// <summary>
        /// Finds a user ID by external login provider and key.
        /// </summary>
        /// <param name="providerName">The name of the external login provider.</param>
        /// <param name="providerKey">The provider-specific key.</param>
        /// <returns>The user identifier, or default if not found.</returns>
        public UserIdentifier FindUserIdByLogin(string providerName, string providerKey)
        {
            var lookupKey = GetLookupKey(providerName, providerKey);

            _userIdsByLogin.TryGetValue(lookupKey, out var userId);
            return userId;
        }

        // Helper method for lookup key
        private string GetLookupKey(string providerName, string providerKey)
        {
            return $"{providerName}|{providerKey}";
        }
    }
}