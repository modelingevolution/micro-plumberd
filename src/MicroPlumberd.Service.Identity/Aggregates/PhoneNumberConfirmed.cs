﻿namespace MicroPlumberd.Service.Identity.Aggregates;

public record PhoneNumberConfirmed
{
    public Guid Id { get; init; }
    public string ConcurrencyStamp { get; init; }
}