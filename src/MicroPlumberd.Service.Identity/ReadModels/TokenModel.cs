using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;


namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("TokenModel_v1")]
    public partial class TokenModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ConcurrentDictionary<string, string>> _tokensByUserId = new();

        private async Task Given(Metadata m, TokenAggregateCreated ev)
        {
            _tokensByUserId[ev.UserId] = new ConcurrentDictionary<string, string>();
            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenSet ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_tokensByUserId.TryGetValue(userId, out var userTokens))
            {
                var lookupKey = GetLookupKey(ev.Name.Value, ev.LoginProvider);
                userTokens[lookupKey] = ev.Value.Value;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenRemoved ev)
        {
            var userId = new UserIdentifier(m.Id);

            if (_tokensByUserId.TryGetValue(userId, out var userTokens))
            {
                var lookupKey = GetLookupKey(ev.Name.Value, ev.LoginProvider);
                userTokens.TryRemove(lookupKey, out _);
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, TokenAggregateDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);
            _tokensByUserId.TryRemove(userId, out _);

            await Task.CompletedTask;
        }

        // Query methods
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