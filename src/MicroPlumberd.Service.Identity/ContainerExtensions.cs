﻿using System;
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
            

            // Register stores
            return container;

        }
    }
}
