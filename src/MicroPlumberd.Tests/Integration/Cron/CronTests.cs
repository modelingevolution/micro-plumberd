using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.WorkflowDomain;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using MicroPlumberd.Services.Cron;
using NSubstitute;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Cron
{
    public class CronTests : IDisposable
    {
        private readonly EventStoreServer _eventStore;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly TestAppHost _serverTestApp;
        private readonly TestAppHost _clientTestApp;


        public CronTests(ITestOutputHelper testOutputHelper)
        {
            _eventStore = new EventStoreServer();
            _testOutputHelper = testOutputHelper;
            _serverTestApp = new TestAppHost(testOutputHelper);
            _clientTestApp = new TestAppHost(testOutputHelper);
        }

        /// <summary>How long the job may take to be scheduled and run (it runs every second; the budget absorbs a loaded host).</summary>
        private static readonly TimeSpan JobRunBudget = TimeSpan.FromSeconds(60);

        [Fact]
        public async Task ManualTrigger()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), scopedCommandBus: true)
                .AddCron()
                .AddScopedCommandHandler<StartWorkflowHandler>()
                .AddSingletonCommandHandler<CompleteWorkflowHandler>());

            var srv = await _serverTestApp.StartAsync();

            var recipientId = Guid.NewGuid();
            var startWorkflow = new StartWorkflow { Name = "Test" };
            
            var js = srv.CreateScope().ServiceProvider.GetRequiredService<IJobService>();
            var monitor = srv.GetRequiredService<IJobsMonitor>();

            await Task.Delay(1000);

            await js.CreateBuilder("test")
                .WithCommand(startWorkflow, recipientId)
                .WithIntervalSchedule(TimeSpan.FromSeconds(1))
                .Enable()
                .Create();
            
            // Wait for the CONDITION, bounded — not a fixed 20 s sleep, which failed under load with ScheduledTotal 0. And THIS
            // job's evidence: CompleteWorkflowHandler.Mock is STATIC and WorkflowTests drives it too, so "received any call"
            // was satisfied by another test in the suite; only a call for this test's own recipient proves this job ran.
            ulong RanForMe() => (ulong)CompleteWorkflowHandler.Mock.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == nameof(ICommandHandler<Guid, CompleteWorkflow>.Execute)
                            && c.GetArguments()[0] is Guid id && id == recipientId);
            var deadline = DateTime.UtcNow + JobRunBudget;
            while (!(monitor.ScheduledTotal >= 1 && monitor.Executed >= 1 && RanForMe() >= 1) && DateTime.UtcNow < deadline)
                await Task.Delay(200);

            monitor.ScheduledTotal.Should().BeGreaterThanOrEqualTo(1ul, $"the job is scheduled within {JobRunBudget}");
            monitor.Executed.Should().BeGreaterThanOrEqualTo(1ul, "and executed");
            RanForMe().Should().BeGreaterThanOrEqualTo(1ul, "and THIS job's command reached the handler for this test's recipient");
            
            using var serverScope = srv.CreateScope();
            var pl = serverScope.ServiceProvider.GetRequiredService<IPlumber>();
           
        }

        public void Dispose()
        {
            _clientTestApp?.Dispose();
            _serverTestApp?.Dispose();
            _eventStore?.Dispose();

        }
    }
}

