using MicroPlumberd.Tests.App.Srv;
using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.App.Domain;

[ProcessManager]
public partial class XooProcessManager 
{
    public async Task<ICommandRequest<CreateBoo>> StartWhen(Metadata m, FooCreated ev)
    {
        return CommandRequest.Create("Hello".ToGuid(), new CreateBoo());
    }
    private async Task<ICommandRequest<CreateLoo>> When(Metadata m, BooUpdated ev)
    {
        return CommandRequest.Create(Guid.NewGuid(), new CreateLoo());
    }

    private async Task Given(Metadata m, FooCreated ev)
    {
        // This method is used to rebuild the state;
        // In this example, It is called only When method "When(.., BooUpdated)" is executed.
        // Because the state of the FooProcessManager needs to be rebuilt.
        Console.WriteLine("Given-FooCreated");
    }

    private async Task Given(Metadata m, CommandEnqueued<CreateBoo> ev)
    {
        // This method is optional; It is used to capture the fact, that command was sent to the queue.
        Console.WriteLine("Given-CommandEnqueued<CreateLoo>");
    }


}