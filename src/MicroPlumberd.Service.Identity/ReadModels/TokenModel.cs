using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    /// <summary>
    /// Read model maintaining authentication tokens for users.
    /// </summary>
    [EventHandler]
    [OutputStream("TokenModel_v1")]
    public partial class TokenModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ConcurrentDictionary<string, string>> _tokensByUserId = new();

        private async Task Given(Metadata m, TokenAggregateCreated ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _tokensByUserId[userId] = new ConcurrentDictionary<string, string>();
            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenSet ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_tokensByUserId.TryGetValue(userId, out var userTokens))
            {
                var lookupKey = GetLookupKey(ev.Name.Value, ev.LoginProvider);
                userTokens[lookupKey] = ev.Value.Value;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenRemoved ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_tokensByUserId.TryGetValue(userId, out var userTokens))
            {
                var lookupKey = GetLookupKey(ev.Name.Value, ev.LoginProvider);
                userTokens.TryRemove(lookupKey, out _);
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenAggregateDeleted ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _tokensByUserId.TryRemove(userId, out _);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets a token for a user by name and login provider.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="name">The name of the token.</param>
        /// <param name="loginProvider">The login provider associated with the token.</param>
        /// <returns>The token value, or null if not found.</returns>
        public string GetToken(UserIdentifier userId, string name, string loginProvider)
        {
            if (_tokensByUserId.TryGetValue(userId, out var userTokens))
            {
                var lookupKey = GetLookupKey(name, loginProvider);
                userTokens.TryGetValue(lookupKey, out var token);
                return token;
            }

            return null;
        }

        // Helper method for lookup key
        private string GetLookupKey(string name, string loginProvider)
        {
            return $"{loginProvider ?? string.Empty}|{name}";
        }
    }
}