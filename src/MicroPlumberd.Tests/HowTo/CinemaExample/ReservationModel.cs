using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.HowTo.CinemaExample;

namespace MicroPlumberd.Tests.HowTo;

[OutputStream("ReservationModel_v1")]
[EventHandler]
public partial class ReservationModel(InMemoryModelStore assertionModelStore)
{
    public InMemoryModelStore ModelStore => assertionModelStore;
    public bool EventHandled{ get; set; } = false;
    private async Task Given(Metadata m, TicketReserved ev)
    {
        EventHandled = true;
        assertionModelStore.Given(m, ev);
        await Task.Delay(0);
    }
   

}