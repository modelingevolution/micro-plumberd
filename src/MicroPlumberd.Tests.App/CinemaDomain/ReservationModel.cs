using LiteDB;
using MicroPlumberd.Tests.App.Infrastructure;

namespace MicroPlumberd.Tests.App.CinemaDomain;

[OutputStream("ReservationModel_v1")]
[EventHandler]
public partial class ReservationModel(InMemoryAssertionDb assertionModelStore)
{
    public InMemoryAssertionDb ModelStore => assertionModelStore;
    public bool EventHandled{ get; set; } = false;
    private async Task Given(Metadata m, TicketReserved ev)
    {
        EventHandled = true;
        assertionModelStore.Add(m, ev);
        await Task.Delay(0);
    }
   
}



[OutputStream("ReservationModel_v2")]
[EventHandler]
public partial class DbReservationModel
{
    private readonly LiteDatabase _db;

    public DbReservationModel(LiteDatabase db)
    {
        _db = db;
        this.Reservations = db.GetCollection<Reservation>("reservations");
        
    }

    public ILiteCollection<Reservation> Reservations { get; set; }


    private async Task Given(Metadata m, TicketReserved ev)
    {
        Reservations.Insert(new Reservation() { RoomName = ev.RoomName, MovieName = ev.MovieName });
        
    }

}
public record Reservation
{
    public ObjectId ReservationId { get; set; }
    public string RoomName { get; set; }
    public string MovieName { get; set; }

}