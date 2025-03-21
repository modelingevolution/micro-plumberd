﻿namespace MicroPlumberd.Services.Identity.Aggregates;

[OutputStream("Token")]
public record TokenRemoved
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public TokenName Name { get; init; }
    public string LoginProvider { get; init; }
    
}