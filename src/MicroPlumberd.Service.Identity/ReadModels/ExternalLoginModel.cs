using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("ExternalLoginModel_v1")]
    public partial class ExternalLoginModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ImmutableList<ExternalLoginInfo>> _loginsByUserId = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByLogin = new();

        public record ExternalLoginInfo
        {
            public string ProviderName { get; init; }
            public string ProviderKey { get; init; }
            public string DisplayName { get; init; }
        }

        private async Task Given(Metadata m, ExternalLoginAggregateCreated ev)
        {
            _loginsByUserId[ev.UserId] = ImmutableList<ExternalLoginInfo>.Empty;
            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, ExternalLoginAdded ev)
        {
            var userId = new UserIdentifier(m.Id);

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
            var userId = new UserIdentifier(m.Id);

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
            var userId = new UserIdentifier(m.Id);

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

        // Query methods
        public ImmutableList<ExternalLoginInfo> GetLoginsForUser(UserIdentifier userId)
        {
            if (_loginsByUserId.TryGetValue(userId, out var logins))
            {
                return logins;
            }

            return ImmutableList<ExternalLoginInfo>.Empty;
        }

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