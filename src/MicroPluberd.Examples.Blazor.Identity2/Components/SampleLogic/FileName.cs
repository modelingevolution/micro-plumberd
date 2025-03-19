using MicroPlumberd.Services;
using MicroPlumberd;
using System.Diagnostics;

namespace MicroPluberd.Examples.Blazor.Identity2.Components.SampleLogic
{
    
    [OutputStream("Workflow")]
    public class WorkflowCompleted 
    {
        
        public string? Name { get; set; }
        
        public Guid Id { get; set; } = Guid.NewGuid();
    }
    [OutputStream("Workflow")]
    public class CompleteWorkflow 
    {
       
        public string? Name { get; set; }
        
        public Guid Id { get; set; } = Guid.NewGuid();
    }
    [OutputStream("Workflow")]
    public class StartWorkflow 
    {
        public string? Name { get; set; }
        
        public Guid Id { get; set; } = Guid.NewGuid();
    }
    [CommandHandler]
    public partial class StartWorkflowHandler(ICommandBus bus)
    {
        public async Task Handle(Guid id, StartWorkflow cmd)
        {
            Debug.WriteLine("===> Start workflow handler begin invocation...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await bus.SendAsync(id, new CompleteWorkflow { Name = cmd.Name });
            Debug.WriteLine("===> Start workflow handler Invoked");
        }
    }

    [CommandHandler]
    public partial class CompleteWorkflowHandler(IPlumberInstance pl)
    {
        
        public async Task Handle(Guid id, CompleteWorkflow cmd)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            Debug.WriteLine("===> Complete workflow returned.");
            await pl.AppendEvent(new WorkflowCompleted(), id);
        }
    }
}
