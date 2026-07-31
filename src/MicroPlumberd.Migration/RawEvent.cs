using System.Text.Json.Nodes;

namespace MicroPlumberd.Migration;

/// <summary>
/// A raw, non-typed view of a single stored event as seen by migration rules.
/// <para>
/// Migrations NEVER deserialize into domain event classes — they operate purely on the event-type
/// string plus the JSON payload and metadata. This is what lets a migration rewrite events whose CLR
/// type has since been deleted or renamed: the library has zero reference to any app's event assemblies.
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Data"/> is <c>null</c> when the stored payload is not <c>application/json</c> (e.g. a
/// binary / protobuf event) — such payloads are copied verbatim and cannot be transformed.
/// <see cref="Metadata"/> is a <see cref="JsonObject"/> for MicroPlumberd-written events (it always
/// stores JSON metadata); it is <c>null</c> only when the source metadata is absent or non-JSON.
/// </remarks>
public sealed record RawEvent
{
    /// <summary>The source stream the event was read from (e.g. <c>Offer-of-…</c>).</summary>
    public required string StreamId { get; init; }

    /// <summary>The per-stream event number (revision) at the source, 0-based.</summary>
    public required ulong EventNumber { get; init; }

    /// <summary>The stored event-type string (e.g. <c>OfferCreated</c>).</summary>
    public required string Type { get; init; }

    /// <summary>The JSON payload, or <c>null</c> for non-JSON payloads.</summary>
    public required JsonNode? Data { get; init; }

    /// <summary>The JSON metadata, or <c>null</c> when the source has none.</summary>
    public required JsonNode? Metadata { get; init; }
}
