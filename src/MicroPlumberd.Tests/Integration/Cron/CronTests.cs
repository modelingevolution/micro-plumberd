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
    public class CronTests
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
            
            await Task.Delay(20000);

            await CompleteWorkflowHandler.Mock.Received().Execute(Arg.Any<Guid>(), Arg.Any<CompleteWorkflow>());
            monitor.ScheduledTotal.Should().BeGreaterThanOrEqualTo(1ul);
            monitor.Executed.Should().BeGreaterThanOrEqualTo(1ul);
            
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

