using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("UserByEmailModel_v1")]
    public partial class UserByEmailModel
    {
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedEmail = new();

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            if (!string.IsNullOrEmpty(ev.NormalizedEmail))
            {
                _userIdsByNormalizedEmail[ev.NormalizedEmail] = ev.UserId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailChanged ev)
        {
            var userId = new UserIdentifier(m.Id);

            // Find and remove old email mapping if it exists
            foreach (var entry in _userIdsByNormalizedEmail)
            {
                if (entry.Value.Id == userId.Id)
                {
                    _userIdsByNormalizedEmail.TryRemove(entry.Key, out _);
                    break;
                }
            }

            // Add new mapping
            if (!string.IsNullOrEmpty(ev.NormalizedEmail))
            {
                _userIdsByNormalizedEmail[ev.NormalizedEmail] = userId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = new UserIdentifier(m.Id);

            // Remove email mapping for this user
            foreach (var entry in _userIdsByNormalizedEmail)
            {
                if (entry.Value.Id == userId.Id)
                {
                    _userIdsByNormalizedEmail.TryRemove(entry.Key, out _);
                    break;
                }
            }

            await Task.CompletedTask;
        }

        // Query methods
        public UserIdentifier GetIdByNormalizedEmail(string normalizedEmail)
        {
            if (!string.IsNullOrEmpty(normalizedEmail) &&
                _userIdsByNormalizedEmail.TryGetValue(normalizedEmail, out var userId))
            {
                return userId;
            }

            return default;
        }
    }
}