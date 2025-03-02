using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserByNameModel_v1")]
    public partial class UserByNameModel
    {
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedUsername = new();

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            if (!string.IsNullOrEmpty(ev.NormalizedUserName))
            {
                _userIdsByNormalizedUsername[ev.NormalizedUserName] = ev.UserId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserNameChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            // Find and remove old username mapping if it exists
            foreach (var entry in _userIdsByNormalizedUsername)
            {
                if (entry.Value.Id == userId.Id)
                {
                    _userIdsByNormalizedUsername.TryRemove(entry.Key, out _);
                    break;
                }
            }

            // Add new mapping
            if (!string.IsNullOrEmpty(ev.NormalizedUserName))
            {
                _userIdsByNormalizedUsername[ev.NormalizedUserName] = userId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);

            // Remove username mapping for this user
            foreach (var entry in _userIdsByNormalizedUsername)
            {
                if (entry.Value.Id == userId.Id)
                {
                    _userIdsByNormalizedUsername.TryRemove(entry.Key, out _);
                    break;
                }
            }

            await Task.CompletedTask;
        }

        // Query methods
        public UserIdentifier GetIdByNormalizedUsername(string normalizedUsername)
        {
            if (!string.IsNullOrEmpty(normalizedUsername) &&
                _userIdsByNormalizedUsername.TryGetValue(normalizedUsername, out var userId))
            {
                return userId;
            }

            return default;
        }
    }
}