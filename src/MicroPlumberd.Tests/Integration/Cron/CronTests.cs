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

        /// <summary>How long a job may take to be scheduled and run (it runs every second; the budget absorbs a loaded host).</summary>
        private static readonly TimeSpan JobRunBudget = TimeSpan.FromSeconds(60);

        private readonly Guid _recipientId = Guid.NewGuid();

        /// <summary>
        /// THIS test's evidence: CompleteWorkflowHandler.Mock is STATIC, so "received any call" would be satisfied by any other
        /// caller in the process (none today — measured: 0 calls while WorkflowTests ran); only a call for this test's own
        /// recipient proves this test's job ran.
        /// </summary>
        private ulong RanForMe() => (ulong)CompleteWorkflowHandler.Mock.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ICommandHandler<Guid, CompleteWorkflow>.Execute)
                        && c.GetArguments()[0] is Guid id && id == _recipientId);

        private async Task<(IJobService Jobs, IJobsMonitor Monitor)> StartServer()
        {
            await _eventStore.StartInDocker();
            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(), scopedCommandBus: true)
                .AddCron()
                .AddScopedCommandHandler<StartWorkflowHandler>()
                .AddSingletonCommandHandler<CompleteWorkflowHandler>());
            var srv = await _serverTestApp.StartAsync();
            // No fixed wait before creating the job (rev-reach 116f2d9ad): the job's events are folded whenever the
            // processor subscribes, so a job created before it is ready is scheduled when it is.
            return (srv.CreateScope().ServiceProvider.GetRequiredService<IJobService>(), srv.GetRequiredService<IJobsMonitor>());
        }

        /// <summary>Waits for the CONDITION, bounded — never a fixed sleep (a fixed 20 s failed under load with ScheduledTotal 0).</summary>
        private static async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + JobRunBudget;
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(200);
        }

        /// <summary>
        /// The ENGINE path: an enabled job on a 1 s interval is scheduled and run. (This row was named "ManualTrigger"; it never
        /// triggered manually — rev-reach 116f2d9ad. A job first firing after 20 s is red under the old fixed sleep, green here.)
        /// </summary>
        [Fact]
        public async Task AnEnabledIntervalJob_IsScheduledByTheEngine_AndRuns()
        {
            var (js, monitor) = await StartServer();

            await js.CreateBuilder("test")
                .WithCommand(new StartWorkflow { Name = "Test" }, _recipientId)
                .WithIntervalSchedule(TimeSpan.FromSeconds(1))
                .Enable()
                .Create();
            await Until(() => monitor.ScheduledTotal >= 1 && monitor.Executed >= 1 && RanForMe() >= 1);

            monitor.ScheduledTotal.Should().BeGreaterThanOrEqualTo(1ul, $"the job is scheduled within {JobRunBudget}");
            monitor.Executed.Should().BeGreaterThanOrEqualTo(1ul, "and executed");
            RanForMe().Should().BeGreaterThanOrEqualTo(1ul, "and THIS job's command reached the handler for this test's recipient");
        }

        /// <summary>
        /// The MANUAL path (IJobService.RunOnce → ScheduleTrigger.Manual): a job that is NOT enabled — so the engine never
        /// schedules it — runs once when triggered, and the engine's schedule count stays 0, which proves the run was manual.
        /// </summary>
        [Fact]
        public async Task ADisabledJob_TriggeredManually_RunsOnce_WithoutTheEngineScheduling_It()
        {
            var (js, monitor) = await StartServer();
            var job = await js.CreateBuilder("manual")
                .WithCommand(new StartWorkflow { Name = "Manual" }, _recipientId)
                .WithIntervalSchedule(TimeSpan.FromSeconds(1))
                .Create();

            await js.RunOnce(job.Id);
            await Until(() => monitor.Executed >= 1 && RanForMe() >= 1);

            RanForMe().Should().Be(1ul, "the manual trigger ran this job exactly once");
            monitor.ScheduledTotal.Should().Be(0ul, "a disabled job is never scheduled by the engine: the run was the manual one");
        }

        public void Dispose()
        {
            _clientTestApp?.Dispose();
            _serverTestApp?.Dispose();
            _eventStore?.Dispose();

        }
    }
}

