using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Cron
{
    public enum JobServiceRunningMode
    {
        Client = 1,
        Server = 2,
        Both = 3
    }
    public static class ContainerExtensions
    {
        public static IServiceCollection AddCron(this IServiceCollection services, 
            JobServiceRunningMode mode = JobServiceRunningMode.Both)
        {
            if (mode.HasFlag(JobServiceRunningMode.Client))
            {
                services.AddScoped<IJobService, JobService>();
            }

            if (mode.HasFlag(JobServiceRunningMode.Server))
            {
                services.AddScopedCommandHandler<JobExecutorCommandHandler>();
                services.AddHostedService<JobExecutionBackgroundService>();
                services.AddSingletonEventHandler<JobExecutionProcessor>(start: FromRelativeStreamPosition.End);
                services.AddSingletonEventHandler<JobDefinitionModel>();
                
                services.AddSingleton<IJobsScheduler>(sp => sp.GetRequiredService<JobExecutionProcessor>());
                services.AddSingleton<IJobsMonitor, JobsMonitor>();
            }

            return services;
        }
    }
}
