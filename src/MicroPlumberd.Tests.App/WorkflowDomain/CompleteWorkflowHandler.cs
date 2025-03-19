using System.Diagnostics;
using MicroPlumberd.DirectConnect;
using MicroPlumberd.Services;
using NSubstitute;

namespace MicroPlumberd.Tests.App.WorkflowDomain;

[CommandHandler]
public partial class CompleteWorkflowHandler(IPlumberInstance pl)
{
    public static ICommandHandler<Guid,CompleteWorkflow> Mock = Substitute.For<ICommandHandler<Guid,CompleteWorkflow>>();
    public async Task Handle(Guid id, CompleteWorkflow cmd)
    {
        Debug.WriteLine("===> Complete workflow returned.");
        await pl.AppendEvent(new WorkflowCompleted(), id);
        await Mock.Execute(id, cmd);
    }
}