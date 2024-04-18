namespace MicroPlumberd.Tests.App.CinemaDomain;

[OutputStream("ReservationStream")]
public record TicketReserved { 
    public string? MovieName { get; init; } 
    public string? RoomName { get; init; }
}
