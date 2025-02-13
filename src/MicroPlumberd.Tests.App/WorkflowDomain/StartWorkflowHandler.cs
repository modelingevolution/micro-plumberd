using System.Diagnostics;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;

namespace MicroPlumberd.Tests.App.WorkflowDomain;

[CommandHandler]
public partial class StartWorkflowHandler(ICommandBus bus)
{
    public async Task Handle(Guid id, StartWorkflow cmd)
    {
        Debug.WriteLine("===> Start workflow handler begin invocation...");
        await bus.SendAsync(id, new CompleteWorkflow { Name = cmd.Name });
        Debug.WriteLine("===> Start workflow handler Invoked");
    }
}