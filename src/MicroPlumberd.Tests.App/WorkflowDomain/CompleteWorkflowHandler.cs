using System.Diagnostics;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;

namespace MicroPlumberd.Tests.App.WorkflowDomain;

[CommandHandler]
public partial class CompleteWorkflowHandler(IPlumberInstance pl)
{
    public async Task Handle(Guid id, CompleteWorkflow cmd)
    {
        Debug.WriteLine("===> Complete workflow returned.");
        await pl.AppendEvent(new WorkflowCompleted(), id);
    }
}