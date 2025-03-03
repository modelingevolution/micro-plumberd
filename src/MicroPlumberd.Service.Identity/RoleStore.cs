using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MicroPlumberd.Service.Identity.Aggregates;
using MicroPlumberd.Service.Identity.ReadModels;

namespace MicroPlumberd.Service.Identity;

/// <summary>
/// Implementation of ASP.NET Core Identity store for roles using event sourcing with MicroPlumberd
/// </summary>
public class RoleStore :
    IRoleStore<Role>,
    IQueryableRoleStore<Role>
{
    private readonly IPlumber _plumber;
    private readonly RolesModel _rolesModel;

    public RoleStore(
        IPlumber plumber,
        RolesModel rolesModel)
    {
        _plumber = plumber ?? throw new ArgumentNullException(nameof(plumber));
        _rolesModel = rolesModel ?? throw new ArgumentNullException(nameof(rolesModel));
    }

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

    public async Task<Role> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = GetRoleIdentifier(roleId);
        return _rolesModel.GetById(id);
    }

    public async Task<Role> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _rolesModel.GetByNormalizedName(normalizedRoleName);
    }

    public Task<string> GetNormalizedRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.NormalizedName);
    }

    public Task<string> GetRoleIdAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.Id);
    }

    public Task<string> GetRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        return Task.FromResult(role.Name);
    }

    public Task SetNormalizedRoleNameAsync(Role role, string normalizedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetRoleNameAsync(Role role, string roleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role == null)
            throw new ArgumentNullException(nameof(role));

        role.Name = roleName;
        return Task.CompletedTask;
    }

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

    public void Dispose()
    {
        // Nothing to dispose
    }
}