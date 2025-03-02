using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroPlumberd.Service.Identity.ReadModels;
using MicroPlumberd.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static MicroPlumberd.Service.Identity.Aggregates.RoleAggregate;

namespace MicroPlumberd.Service.Identity
{
    public static class ContainerExtensions
    {
        public static IServiceCollection AddPlumberdIdentity(this IServiceCollection container)
        {
            container.AddSingleton<UserByIdModel>();
            container.AddSingleton<UserByNameModel>();
            container.AddSingleton<UserByEmailModel>();
            container.AddSingleton<AuthenticationModel>();
            container.AddSingleton<UserProfileModel>();
            container.AddSingleton<UserAuthorizationModel>();
            container.AddSingleton<ExternalLoginModel>();
            container.AddSingleton<TokenModel>();
            container.AddSingleton<RoleByIdModel>();
            container.AddSingleton<RoleByNameModel>();

            // Register event handlers for read models
            container.AddEventHandler<UserByIdModel>();
            container.AddEventHandler<UserByNameModel>();
            container.AddEventHandler<UserByEmailModel>();
            container.AddEventHandler<AuthenticationModel>();
            container.AddEventHandler<UserProfileModel>();
            container.AddEventHandler<UserAuthorizationModel>();
            container.AddEventHandler<ExternalLoginModel>();
            container.AddEventHandler<TokenModel>();
            container.AddEventHandler<RoleByIdModel>();
            container.AddEventHandler<RoleByNameModel>();

            // Register stores
            return container;

        }
    }
}
