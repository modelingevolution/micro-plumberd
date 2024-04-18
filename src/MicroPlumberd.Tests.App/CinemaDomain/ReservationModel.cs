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
        this.Reservations = db.Reservations();


    }

    public ILiteCollection<Reservation> Reservations { get; set; }


    private async Task Given(Metadata m, TicketReserved ev)
    {
        Reservations.Insert(new Reservation() { RoomName = ev.RoomName, MovieName = ev.MovieName });
        
    }
}

public static class DbExtensions
{
    public static ILiteCollection<Reservation> Reservations(this LiteDatabase db) => db.GetCollection<Reservation>("reservations");
}
public record Reservation
{
    public ObjectId ReservationId { get; set; }
    public string RoomName { get; set; }
    public string MovieName { get; set; }

}