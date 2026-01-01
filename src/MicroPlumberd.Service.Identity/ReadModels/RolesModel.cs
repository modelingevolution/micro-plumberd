using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;

namespace MicroPlumberd.Services.Identity.ReadModels
{
    /// <summary>
    /// Consolidated read model for roles with direct references in lookups.
    /// Exposes Items as ObservableCollection for UI binding.
    /// </summary>
    [EventHandler]
    [OutputStream("RolesModel_v1")]
    public partial class RolesModel
    {
        // Primary collection - clustered index
        private readonly ConcurrentDictionary<RoleIdentifier, Role> _rolesById = new();

        // Observable collection for UI binding
        private readonly ObservableCollection<Role> _items = new();

        // Lookup dictionary - direct references to the same Role objects
        private readonly ConcurrentDictionary<string, Role> _rolesByNormalizedName = new();

        /// <summary>
        /// Gets the observable collection of roles for UI binding.
        /// The underlying collection implements INotifyCollectionChanged.
        /// </summary>
        public IReadOnlyList<Role> Items => _items;

        /// <summary>
        /// Gets all roles in the read model.
        /// </summary>
        /// <returns>An immutable list of all roles.</returns>
        public ImmutableList<Role> GetAllRoles() => _rolesById.Values.ToImmutableList();

        #region Event Handlers

        private async Task Given(Metadata m, RoleCreated ev)
        {
            // Create a new role object
            var roleId = m.StreamId<RoleIdentifier>();
            
            var role = new Role
            {
                Id = roleId.ToString(),
                Name = ev.Name,
                NormalizedName = ev.NormalizedName
            };

            // Add to primary collection
            if (_rolesById.TryAdd(roleId, role))
            {
                // Add to observable collection for UI
                _items.Add(role);

                // Add to lookup - same reference
                if (!string.IsNullOrEmpty(ev.NormalizedName))
                {
                    _rolesByNormalizedName.TryAdd(ev.NormalizedName, role);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleNameChanged ev)
        {
            var roleId = m.StreamId<RoleIdentifier>();

            if (_rolesById.TryGetValue(roleId, out var role))
            {
                // Remove from old lookup
                if (!string.IsNullOrEmpty(role.NormalizedName))
                {
                    _rolesByNormalizedName.TryRemove(role.NormalizedName, out _);
                }

                // Update the role
                role.Name = ev.Name;
                role.NormalizedName = ev.NormalizedName;

                // Add to lookup with updated normalized name - same reference
                if (!string.IsNullOrEmpty(ev.NormalizedName))
                {
                    _rolesByNormalizedName.TryAdd(ev.NormalizedName, role);
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, RoleDeleted ev)
        {
            var roleId = m.StreamId<RoleIdentifier>();

            // Remove from primary collection
            if (_rolesById.TryRemove(roleId, out var role))
            {
                // Remove from observable collection for UI
                _items.Remove(role);

                // Remove from lookup
                if (!string.IsNullOrEmpty(role.NormalizedName))
                {
                    _rolesByNormalizedName.TryRemove(role.NormalizedName, out _);
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// Gets a role by ID
        /// </summary>
        public Role GetById(RoleIdentifier id)
        {
            _rolesById.TryGetValue(id, out var role);
            return role;
        }

        /// <summary>
        /// Gets a role by normalized name
        /// </summary>
        public Role GetByNormalizedName(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName))
                return null;

            _rolesByNormalizedName.TryGetValue(normalizedName, out var role);
            return role;
        }

        /// <summary>
        /// Gets a role ID by normalized name
        /// </summary>
        public RoleIdentifier GetIdByNormalizedName(string normalizedName)
        {
            var role = GetByNormalizedName(normalizedName);
            return role != null ? GetRoleIdentifier(role.Id) : default;
        }

        #endregion

        #region Helper Methods

        private RoleIdentifier GetRoleIdentifier(string roleId)
        {
            return RoleIdentifier.Parse(roleId, null);
        }

        #endregion
    }
}