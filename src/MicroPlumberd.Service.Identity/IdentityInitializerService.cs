using MicroPlumberd.Services.Identity.ReadModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Background service that initializes the identity system on application startup.
/// Creates an admin user and role if no users exist in the system.
/// </summary>
public class IdentityInitializerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly UsersModel _usersModel;
    private readonly RolesModel _rolesModel;
    private readonly IdentityInitializerOptions _options;
    private readonly ILogger<IdentityInitializerService> _logger;

    public IdentityInitializerService(
        IServiceProvider serviceProvider,
        UsersModel usersModel,
        RolesModel rolesModel,
        IOptions<IdentityInitializerOptions> options,
        ILogger<IdentityInitializerService> logger)
    {
        _serviceProvider = serviceProvider;
        _usersModel = usersModel;
        _rolesModel = rolesModel;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SeedAdminUser)
        {
            _logger.LogInformation("Admin user seeding is disabled");
            return;
        }

        _logger.LogInformation("Waiting {WaitTime} for projections to catch up before checking for users",
            _options.ProjectionWaitTime);

        try
        {
            await Task.Delay(_options.ProjectionWaitTime, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Identity initialization cancelled during wait period");
            return;
        }

        var users = _usersModel.GetAllUsers();
        if (users.Count > 0)
        {
            _logger.LogInformation("Found {UserCount} existing users, skipping admin seeding", users.Count);
            return;
        }

        _logger.LogInformation("No users found, creating initial admin user");

        // Use a scope for UserManager and RoleManager (they are scoped services)
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        await CreateAdminRoleAsync(roleManager, stoppingToken);
        await CreateAdminUserAsync(userManager, stoppingToken);
    }

    private async Task CreateAdminRoleAsync(RoleManager<Role> roleManager, CancellationToken stoppingToken)
    {
        var normalizedRoleName = _options.AdminRoleName.ToUpperInvariant();
        var existingRole = _rolesModel.GetByNormalizedName(normalizedRoleName);

        if (existingRole != null)
        {
            _logger.LogDebug("Admin role '{RoleName}' already exists", _options.AdminRoleName);
            return;
        }

        var role = new Role { Name = _options.AdminRoleName };
        var result = await roleManager.CreateAsync(role);

        if (result.Succeeded)
        {
            _logger.LogInformation("Created admin role '{RoleName}'", _options.AdminRoleName);
        }
        else
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create admin role '{RoleName}': {Errors}",
                _options.AdminRoleName, errors);
        }
    }

    private async Task CreateAdminUserAsync(UserManager<User> userManager, CancellationToken stoppingToken)
    {
        var adminUser = new User
        {
            UserName = _options.AdminUserName,
            Email = _options.AdminEmail,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(adminUser, _options.AdminPassword);

        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create admin user '{UserName}': {Errors}",
                _options.AdminUserName, errors);
            return;
        }

        _logger.LogInformation("Created admin user '{UserName}' with email '{Email}'",
            _options.AdminUserName, _options.AdminEmail);

        // Assign admin role
        var roleResult = await userManager.AddToRoleAsync(adminUser, _options.AdminRoleName);

        if (roleResult.Succeeded)
        {
            _logger.LogInformation("Assigned admin user to role '{RoleName}'", _options.AdminRoleName);
        }
        else
        {
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            _logger.LogError("Failed to assign admin user to role '{RoleName}': {Errors}",
                _options.AdminRoleName, errors);
        }
    }
}
