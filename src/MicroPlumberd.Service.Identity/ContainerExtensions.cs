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
        /// Adds the identity initializer service that seeds an admin user on first startup.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configure">Optional action to configure the identity initializer options.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddIdentityInitializer(
            this IServiceCollection services,
            Action<IdentityInitializerOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }
            else
            {
                services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new IdentityInitializerOptions()));
            }

            services.AddHostedService<IdentityInitializerService>();
            return services;
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
