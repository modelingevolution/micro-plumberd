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
            
            container.AddIdentity<User, Role>()
                .AddDefaultTokenProviders()
                .AddSignInManager();
            container.AddScoped<IUserStore<User>, UserStore>();
            container.AddScoped<IRoleStore<Role>, RoleStore>();

            if (GetCurrentUser == null) return container;

            GetFlow ??= (sp) => new ValueTask<Flow>(Flow.Request);
            container.AddScoped<IUserAuthContext>(sp => new UserAuthContextFunc(GetCurrentUser,GetFlow,sp));
            container.Decorate<ICommandBus, CommandBusIdentityDecorator>();
            // Register stores
            return container;

        }
    }
}
