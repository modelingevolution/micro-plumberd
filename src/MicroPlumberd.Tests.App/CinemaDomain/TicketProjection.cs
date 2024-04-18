namespace MicroPlumberd.Tests.App.CinemaDomain;

[EventHandler]
public partial class TicketProjection(IPlumber plumber)
{
    private async Task Given(Metadata m, TicketReserved ev)
    {
        await plumber.AppendLink($"RoomOccupancy-{ev.RoomName}", m);
    }
}

