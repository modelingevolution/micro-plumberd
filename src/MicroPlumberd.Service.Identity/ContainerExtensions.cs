using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroPlumberd.Services.Identity.ReadModels;
using MicroPlumberd.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using static MicroPlumberd.Services.Identity.Aggregates.RoleAggregate;

namespace MicroPlumberd.Services.Identity
{
    /// <summary>
    /// Extension methods for configuring MicroPlumberd Identity services in a dependency injection container.
    /// </summary>
    public static class ContainerExtensions
    {
        /// <summary>
        /// Adds MicroPlumberd Identity services to the service collection, including ASP.NET Core Identity integration and event-sourced read models.
        /// Uses AddIdentityCore (not AddIdentity) to avoid registering authentication schemes - the consuming app
        /// should configure authentication separately via AddAuthentication().AddIdentityCookies() or similar.
        /// </summary>
        /// <param name="container">The service collection to add services to.</param>
        /// <param name="GetCurrentUser">Optional function to retrieve the current user ID from the operation context.</param>
        /// <param name="GetFlow">Optional function to retrieve the current flow context.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddPlumberdIdentity(this IServiceCollection container,
            Func<IServiceProvider, Flow, Task<string>>? GetCurrentUser = null,
            Func<IServiceProvider, ValueTask<Flow>>? GetFlow = null)
        {
            container.AddSingleton<UsersModel>();
            container.AddSingleton<RolesModel>();

            container.AddSingleton<AuthenticationModel>();
            container.AddSingleton<UserProfileModel>();
            container.AddSingleton<UserAuthorizationModel>();
            container.AddSingleton<ExternalLoginModel>();
            container.AddSingleton<TokenModel>();


            // Register event handlers for read models
            container.AddEventHandler<UsersModel>();
            container.AddEventHandler<RolesModel>();
            container.AddEventHandler<AuthenticationModel>();
            container.AddEventHandler<UserProfileModel>();
            container.AddEventHandler<UserAuthorizationModel>();
            container.AddEventHandler<ExternalLoginModel>();
            container.AddEventHandler<TokenModel>();

            // Use AddIdentityCore instead of AddIdentity to avoid registering auth schemes.
            // The consuming app should configure authentication (e.g., AddAuthentication().AddIdentityCookies()).
            container.AddIdentityCore<User>()
                .AddRoles<Role>()
                .AddDefaultTokenProviders()
                .AddSignInManager<PlumberdSignInManager>();
            container.AddScoped<IUserStore<User>, UserStore>();
            container.AddScoped<IRoleStore<Role>, RoleStore>();

            if (GetCurrentUser == null) return container;

            GetFlow ??= (sp) => new ValueTask<Flow>(Flow.Request);
            container.AddScoped<IUserAuthContext>(sp => new UserAuthContextFunc(GetCurrentUser,GetFlow,sp));
            container.Decorate<ICommandBus, CommandBusIdentityDecorator>();
            // Register stores
            return container;

        }

        /// <summary>
        /// Declares, fluently, the identity state that must exist after start-up: roles, users and optional
        /// consumer steps. The seed runs on readiness (the identity read models report
        /// <see cref="ICaughtUpHandler.CaughtUp"/>), converges the store to the declaration, and never stops the
        /// host — see <see cref="IdentityInitializerService"/>.
        /// </summary>
        /// <remarks>
        /// May be called more than once: declarations accumulate in registration order and the hosted runner is
        /// registered exactly once. The last <see cref="IdentitySeedBuilder.WaitUpTo"/> wins.
        /// </remarks>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configure">The fluent declaration.</param>
        /// <returns>The service collection for method chaining.</returns>
        /// <example>
        /// <code>
        /// services.AddIdentitySeed(seed => seed
        ///     .Role("Administrator")
        ///     .User("admin@localhost", u => u.WithUserName("admin").WithPassword(pwd).InRoles("Administrator"))
        ///     .WaitUpTo(TimeSpan.FromSeconds(30)));
        /// </code>
        /// </example>
        public static IServiceCollection AddIdentitySeed(
            this IServiceCollection services,
            Action<IdentitySeedBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddSingleton(new IdentitySeedDeclaration(configure));
            RegisterSeedRunner(services);
            return services;
        }

        /// <summary>
        /// Adds the identity initializer service that seeds an admin user on first startup.
        /// </summary>
        /// <remarks>
        /// Backward-compatible adapter over <see cref="AddIdentitySeed"/> (requirement R7): the options are read
        /// at run time and contribute <c>Role(AdminRoleName)</c>,
        /// <c>User(AdminEmail, WithUserName(AdminUserName), WithPassword(AdminPassword), InRoles(AdminRoleName))</c>
        /// and <c>WaitUpTo(ProjectionWaitTime)</c>. <see cref="IdentityInitializerOptions.SeedAdminUser"/> set to
        /// false contributes nothing, so the seed is ready immediately.
        /// </remarks>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configure">Optional action to configure the identity initializer options.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddIdentityInitializer(
            this IServiceCollection services,
            Action<IdentityInitializerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (configure != null)
            {
                services.Configure(configure);
            }
            else
            {
                services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new IdentityInitializerOptions()));
            }

            services.AddSingleton(IdentitySeedDeclaration.FromOptions());
            RegisterSeedRunner(services);
            return services;
        }

        /// <summary>
        /// Registers the seed plan and the hosted runner exactly once, however many times
        /// <see cref="AddIdentitySeed"/> / <see cref="AddIdentityInitializer"/> /
        /// <c>AddIdentitySeedHealthCheck</c> are called.
        /// </summary>
        /// <remarks>
        /// This mirrors <c>MicroPlumberd.Services.ContainerExtensions.AddBackgroundServiceIfMissing</c>, but
        /// dedupes on an explicit marker: that helper's guard matches on
        /// <c>ServiceDescriptor.ImplementationType</c>, which is null for the factory registration it itself adds,
        /// so it does not actually dedupe.
        /// </remarks>
        internal static void RegisterSeedRunner(IServiceCollection services)
        {
            services.TryAddSingleton<IdentitySeedPlan>();
            services.TryAddSingleton(sp => new IdentityInitializerService(
                sp,
                sp.GetRequiredService<IdentitySeedPlan>(),
                sp.GetRequiredService<ILogger<IdentityInitializerService>>()));

            if (services.Any(d => d.ServiceType == typeof(IdentitySeedHostedMarker))) return;

            services.AddSingleton<IdentitySeedHostedMarker>();
            services.AddHostedService(sp => sp.GetRequiredService<IdentityInitializerService>());
        }

        /// <summary>
        /// Adds the registration policy service that controls who can register new users.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configure">Optional action to configure the registration policy options.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddRegistrationPolicy(
            this IServiceCollection services,
            Action<RegistrationPolicyOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }
            else
            {
                services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new RegistrationPolicyOptions()));
            }

            services.AddSingleton<RegistrationPolicyService>();
            return services;
        }
    }
}
