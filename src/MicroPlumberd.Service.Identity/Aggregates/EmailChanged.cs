﻿namespace MicroPlumberd.Services.Identity.Aggregates;

public record EmailChanged
{
    public Guid Id { get; init; }
    public string Email { get; init; }
    public string NormalizedEmail { get; init; }
    
}