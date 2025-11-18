using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services.Cron
{
    /// <summary>
    /// Specifies the running mode for the job service.
    /// </summary>
    [Flags]
    public enum JobServiceRunningMode
    {
        /// <summary>
        /// Client mode only - allows creating and managing job definitions.
        /// </summary>
        Client = 1,

        /// <summary>
        /// Server mode only - processes and executes scheduled jobs.
        /// </summary>
        Server = 2,

        /// <summary>
        /// Both client and server mode - full job service functionality.
        /// </summary>
        Both = 3
    }

    /// <summary>
    /// Provides extension methods for configuring job scheduling services.
    /// </summary>
    public static class ContainerExtensions
    {
        /// <summary>
        /// Registers job scheduling services with the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="mode">The running mode for the job service. Default is <see cref="JobServiceRunningMode.Both"/>.</param>
        /// <returns>The service collection for method chaining.</returns>
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
