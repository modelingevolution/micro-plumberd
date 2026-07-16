namespace MicroPlumberd.Services.Uniqueness;

/// <summary>A row in a category's reservation table. One row per reserved name (unique index on Name).</summary>
class UniqueNameReservation
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid SourceId { get; set; }

    /// <summary>UTC. Only meaningful while <see cref="IsConfirmed"/> is false — an unconfirmed
    /// reservation past this instant is dead and may be taken over by anyone.</summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>Confirmed reservations never expire; they are released explicitly.</summary>
    public bool IsConfirmed { get; set; }
}
