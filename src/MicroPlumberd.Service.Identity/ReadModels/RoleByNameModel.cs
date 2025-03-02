using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("RoleByNameModel_v1")]
    public partial class RoleByNameModel
    {
        private readonly ConcurrentDictionary<string, RoleIdentifier> _roleIdsByNormalizedName = new();

        private async Task Given(Metadata m, RoleCreated ev)
        {
            if (!string.IsNullOrEmpty(ev.NormalizedName))
            {
                _roleIdsByNormalizedName[ev.NormalizedName] = ev.RoleId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleNameChanged ev)
        {
            var roleId = new RoleIdentifier(m.Id);

            // Find and remove old role name mapping if it exists
            foreach (var entry in _roleIdsByNormalizedName)
            {
                if (entry.Value.Id == roleId.Id)
                {
                    _roleIdsByNormalizedName.TryRemove(entry.Key, out _);
                    break;
                }
            }

            // Add new mapping
            if (!string.IsNullOrEmpty(ev.NormalizedName))
            {
                _roleIdsByNormalizedName[ev.NormalizedName] = roleId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleDeleted ev)
        {
            var roleId = new RoleIdentifier(m.Id);

            // Remove role name mapping for this role
            foreach (var entry in _roleIdsByNormalizedName)
            {
                if (entry.Value.Id == roleId.Id)
                {
                    _roleIdsByNormalizedName.TryRemove(entry.Key, out _);
                    break;
                }
            }

            await Task.CompletedTask;
        }

        // Query methods
        public RoleIdentifier GetIdByNormalizedName(string normalizedName)
        {
            if (!string.IsNullOrEmpty(normalizedName) &&
                _roleIdsByNormalizedName.TryGetValue(normalizedName, out var roleId))
            {
                return roleId;
            }

            return default;
        }
    }
}