using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MicroPlumberd.Services.Identity.Aggregates;
using MicroPlumberd.Services.Identity.ReadModels;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Implementation of ASP.NET Core Identity store for roles using event sourcing with MicroPlumberd
/// </summary>
public class RoleStore :
    IRoleStore<Role>,
    IQueryableRoleStore<Role>
{
    private readonly IPlumber _plumber;
    private readonly RolesModel _rolesModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoleStore"/> class.
    /// </summary>
    /// <param name="plumber">The plumber instance for event sourcing operations.</param>
    /// <param name="rolesModel">The read model for role queries.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plumber"/> or <paramref name="rolesModel"/> is null.</exception>
    public RoleStore(
        IPlumber plumber,
        RolesModel rolesModel)
    {
        _plumber = plumber ?? throw new ArgumentNullException(nameof(plumber));
        _rolesModel = rolesModel ?? throw new ArgumentNullException(nameof(rolesModel));
    }

    /// <summary>
    /// Gets a queryable collection of all roles in the store.
    /// </summary>
    public IQueryable<Role> Roles => _rolesModel.GetAllRoles().AsQueryable();

    // Helper method to convert string ID to RoleIdentifier
    private RoleIdentifier GetRoleIdentifier(string roleId)
    {
        if (string.IsNullOrEmpty(roleId))
            throw new ArgumentException("Role ID cannot be null or empty", nameof(roleId));

        if (!RoleIdentifier.TryParse(roleId, null, out var roleIdentifier))
            throw new ArgumentException("Invalid role ID format", nameof(roleId));

        return roleIdentifier;
    }

    // Helper method to extract expected version from concurrency stamp
    private CompositeStreamVersion GetExpectedVersion(string concurrencyStamp)
    {
        if (string.IsNullOrEmpty(concurrencyStamp))
            return CompositeStreamVersion.Empty;

        if (!CompositeStreamVersion.TryParse(concurrencyStamp, null, out var version))
            return CompositeStreamVersion.Empty;

        return version;
    }

    /// <summary>
    /// Creates a new role in the store.
    /// </summary>
    /// <param name="role">The role to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    public async Task<IdentityResult> CreateAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (role == null)
                throw new ArgumentNullException(nameof(role));

            // Check if a role with this normalized name already exists
            if (!string.IsNullOrEmpty(role.NormalizedName))
            {
                var existingRoleId = _rolesModel.GetIdByNormalizedName(role.NormalizedName);
                if (!existingRoleId.Equals(default(RoleIdentifier)))
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Description = $"Role with name '{role.Name}' already exists"
                    });
                }
            }

            // If the role doesn't have an ID, generate one
            if (string.IsNullOrEmpty(role.Id))
            {
                role.Id = RoleIdentifier.New().ToString();
            }

            var roleId = GetRoleIdentifier(role.Id);
            var normalizedName = role.NormalizedName ?? role.Name?.ToUpperInvariant();

            var roleAggregate = RoleAggregate.Create(roleId, role.Name, normalizedName);
            await _plumber.SaveNew(roleAggregate);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a role from the store.
    /// </summary>
    /// <param name="role">The role to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    public async Task<IdentityResult> DeleteAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (role == null)
                throw new ArgumentNullException(nameof(role));

            var roleId = GetRoleIdentifier(role.Id);
            var expectedVersion = GetExpectedVersion(role.ConcurrencyStamp);

            var roleAggregate = await _plumber.Get<RoleAggregate>(roleId);
            roleAggregate.Delete(); // Not using concurrency stamp
            await _plumber.SaveChanges(roleAggregate);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    /// <summary>
    /// Finds a role by its identifier.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role if found; otherwise, null.</returns>
    public async Task<Role> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = GetRoleIdentifier(roleId);
        return _rolesModel.GetById(id);
    }

    /// <summary>
    /// Finds a role by its normalized name.
    /// </summary>
    /// <param name="normalizedRoleName">The normalized role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role if found; otherwise, null.</returns>
    public async Task<Role> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _rolesModel.GetByNormalizedName(normalizedRoleName);
    }

    /// <summary>
    /// Gets the normalized name of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The normalized role name.</returns>
    public Task<string> GetNormalizedRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.NormalizedName);
    }

    /// <summary>
    /// Gets the identifier of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role identifier.</returns>
    public Task<string> GetRoleIdAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.Id);
    }

    /// <summary>
    /// Gets the name of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role name.</returns>
    public Task<string> GetRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.Name);
    }

    /// <summary>
    /// Sets the normalized name of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="normalizedName">The normalized name to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetNormalizedRoleNameAsync(Role role, string normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the name of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="roleName">The role name to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetRoleNameAsync(Role role, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        role.Name = roleName;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates a role in the store.
    /// </summary>
    /// <param name="role">The role to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IdentityResult"/> indicating the result of the operation.</returns>
    public async Task<IdentityResult> UpdateAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (role == null)
                throw new ArgumentNullException(nameof(role));

            var roleId = GetRoleIdentifier(role.Id);

            // Check if another role with this normalized name already exists
            if (!string.IsNullOrEmpty(role.NormalizedName))
            {
                var existingRole = _rolesModel.GetByNormalizedName(role.NormalizedName);
                if (existingRole != null && existingRole.Id != role.Id)
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Description = $"Role with name '{role.Name}' already exists"
                    });
                }
            }

            var roleAggregate = await _plumber.Get<RoleAggregate>(roleId);

            // Let the aggregate decide if a change is needed
            roleAggregate.ChangeName(role.Name, role.NormalizedName);

            // Only save if there are pending changes
            if (roleAggregate.HasPendingChanges)
            {
                await _plumber.SaveChanges(roleAggregate);
            }

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    /// <summary>
    /// Disposes of resources used by the role store.
    /// </summary>
    public void Dispose()
    {
        // Nothing to dispose
    }
}