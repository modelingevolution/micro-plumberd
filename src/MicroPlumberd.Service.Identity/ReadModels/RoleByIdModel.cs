using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Service.Identity.Aggregates;

namespace MicroPlumberd.Service.Identity.ReadModels
{
    [EventHandler]
    [OutputStream("RoleByIdModel_v1")]
    public partial class RoleByIdModel
    {
        private readonly ConcurrentDictionary<RoleIdentifier, Role> _rolesById = new();

        private async Task Given(Metadata m, RoleCreated ev)
        {
            var role = new Role
            {
                Id = ev.RoleId.ToString(),
                Name = ev.Name,
                NormalizedName = ev.NormalizedName,
                ConcurrencyStamp = ev.ConcurrencyStamp
            };

            _rolesById[ev.RoleId] = role;

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleNameChanged ev)
        {
            var roleId = new RoleIdentifier(m.Id);

            if (_rolesById.TryGetValue(roleId, out var role))
            {
                role.Name = ev.Name;
                role.NormalizedName = ev.NormalizedName;
                role.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleConcurrencyStampChanged ev)
        {
            var roleId = new RoleIdentifier(m.Id);

            if (_rolesById.TryGetValue(roleId, out var role))
            {
                role.ConcurrencyStamp = ev.ConcurrencyStamp;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleDeleted ev)
        {
            var roleId = new RoleIdentifier(m.Id);
            _rolesById.TryRemove(roleId, out _);

            await Task.CompletedTask;
        }

        // Query methods
        public Role GetById(RoleIdentifier id)
        {
            if (_rolesById.TryGetValue(id, out var role))
            {
                return role;
            }

            return null;
        }
    }
}