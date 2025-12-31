using FluentAssertions;
using MicroPlumberd.Services;
using MicroPlumberd.Services.Identity;
using MicroPlumberd.Services.Identity.ReadModels;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Tests.Integration.Identity;

[TestCategory("Integration")]
public class AdminSeedingTests : IClassFixture<EventStoreServer>, IAsyncLifetime
{
    private readonly EventStoreServer _eventStore;

    public AdminSeedingTests(EventStoreServer eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task InitializeAsync()
    {
        await _eventStore.StartInDocker(inMemory: true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IHost CreateHostWithIdentity(Action<IdentityInitializerOptions>? configureInitializer = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddPlumberd(_eventStore.GetEventStoreSettings());
                services.AddPlumberdIdentity();

                if (configureInitializer != null)
                {
                    services.AddIdentityInitializer(configureInitializer);
                }
                else
                {
                    services.AddIdentityInitializer();
                }
            });

        return builder.Build();
    }

    [Fact]
    public async Task FirstStartup_CreatesAdminUser()
    {
        // Arrange
        using var host = CreateHostWithIdentity(opts =>
        {
            opts.AdminEmail = "admin@test.com";
            opts.AdminUserName = "testadmin";
            opts.AdminPassword = "Test123!@#";
            opts.AdminRoleName = "Admin";
            opts.ProjectionWaitTime = TimeSpan.FromSeconds(2);
        });

        // Act
        await host.StartAsync();

        // Wait for the initializer to complete (2 seconds wait + some buffer)
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert
        using var scope = host.Services.CreateScope();
        var usersModel = scope.ServiceProvider.GetRequiredService<UsersModel>();
        var rolesModel = scope.ServiceProvider.GetRequiredService<RolesModel>();

        var users = usersModel.GetAllUsers();
        users.Should().HaveCount(1);
        users[0].Email.Should().Be("admin@test.com");
        users[0].UserName.Should().Be("testadmin");

        var roles = rolesModel.GetAllRoles();
        roles.Should().Contain(r => r.Name == "Admin");

        await host.StopAsync();
    }

    [Fact]
    public async Task DisabledSeeding_DoesNotCreateAdmin()
    {
        // Arrange
        using var host = CreateHostWithIdentity(opts =>
        {
            opts.SeedAdminUser = false;
            opts.ProjectionWaitTime = TimeSpan.FromSeconds(1);
        });

        // Act
        await host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert
        using var scope = host.Services.CreateScope();
        var usersModel = scope.ServiceProvider.GetRequiredService<UsersModel>();

        var users = usersModel.GetAllUsers();
        users.Should().BeEmpty();

        await host.StopAsync();
    }
}

[TestCategory("Unit")]
public class RegistrationPolicyTests
{
    [Fact]
    public void AllowAnonymousRegistration_True_AllowsAnonymous()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions
        {
            AllowAnonymousRegistration = true
        });
        var service = new RegistrationPolicyService(options);

        // Act
        var result = service.CanRegister(null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AllowAnonymousRegistration_False_DeniesAnonymous()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions
        {
            AllowAnonymousRegistration = false
        });
        var service = new RegistrationPolicyService(options);

        // Act
        var result = service.CanRegister(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RequiredRole_UserHasRole_Allows()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions
        {
            AllowAnonymousRegistration = false,
            RequiredRoleForRegistration = "Admin"
        });
        var service = new RegistrationPolicyService(options);

        var claims = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
        }, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(claims);

        // Act
        var result = service.CanRegister(principal);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void RequiredRole_UserMissingRole_Denies()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions
        {
            AllowAnonymousRegistration = false,
            RequiredRoleForRegistration = "Admin"
        });
        var service = new RegistrationPolicyService(options);

        var claims = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User")
        }, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(claims);

        // Act
        var result = service.CanRegister(principal);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void NoRequiredRole_AuthenticatedUser_Allows()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions
        {
            AllowAnonymousRegistration = false,
            RequiredRoleForRegistration = null
        });
        var service = new RegistrationPolicyService(options);

        var claims = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser")
        }, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(claims);

        // Act
        var result = service.CanRegister(principal);

        // Assert
        result.Should().BeTrue();
    }
}
