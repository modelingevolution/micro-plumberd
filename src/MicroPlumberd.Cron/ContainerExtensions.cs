using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Cron
{
    public static class ContainerExtensions
    {
        public static IServiceCollection AddCron(this IServiceCollection services)
        {
            services.AddScoped<IJobService, JobService>();


            return services;
        }
    }
}
