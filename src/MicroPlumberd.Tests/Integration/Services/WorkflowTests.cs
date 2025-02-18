using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.WorkflowDomain;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Services
{
    [TestCategory("Integration")]
    public class WorkflowTests : IClassFixture<EventStoreServer>
    {
        private readonly EventStoreServer _eventStore;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly TestAppHost _serverTestApp;
        private readonly TestAppHost _clientTestApp;


        public WorkflowTests(EventStoreServer eventStore, ITestOutputHelper testOutputHelper)
        {
            _eventStore = eventStore;
            _testOutputHelper = testOutputHelper;
            _serverTestApp = new TestAppHost(testOutputHelper);
            _clientTestApp = new TestAppHost(testOutputHelper);
        }
        
        [Fact]
        public async Task NestedCommandInvocationShouldComplete()
        {
            await _eventStore.StartInDocker();

            _serverTestApp.Configure(x => x
                .AddPlumberd(_eventStore.GetEventStoreSettings(),scopedCommandBus:true)
                .AddCommandHandler<StartWorkflowHandler>()
                .AddCommandHandler<CompleteWorkflowHandler>());

            var srv = await _serverTestApp.StartAsync();

            var client = await _clientTestApp.Configure(x => x
                    .AddPlumberd(_eventStore.GetEventStoreSettings(),
                        (sp, x) => x.ServicesConfig().DefaultTimeout = TimeSpan.FromSeconds(90)))
                .StartAsync();

            var bus = client.GetRequiredService<ICommandBus>();

            var recipientId = Guid.NewGuid();
            var startWorkflow = new StartWorkflow { Name = "Test" };
            var correlationId = startWorkflow.Id;
            
            await bus.SendAsync(recipientId, startWorkflow);
            await Task.Delay(1000);
            var pl = srv.GetRequiredService<IPlumber>();
            var model = await pl.CorrelationModel()
                .WithCommandHandler<StartWorkflowHandler>()
                .WithCommandHandler<CompleteWorkflowHandler>()
                .WithEvent<WorkflowCompleted>()
                .Read(correlationId);
        }
    }
}
