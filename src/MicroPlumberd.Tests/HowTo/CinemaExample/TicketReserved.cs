using MicroPlumberd.Services.Uniqueness;

namespace MicroPlumberd.Tests.HowTo.CinemaExample;

[OutputStream("ReservationStream")]
public record TicketReserved { 
    public string? MovieName { get; set; } 
    public string? RoomName { get; set; }
}
