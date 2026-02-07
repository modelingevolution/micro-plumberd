using System.Dynamic;
using System.Text.Json;
using MicroPlumberd.Utils;

namespace MicroPlumberd;

/// <summary>
/// Factory for creating <see cref="Metadata"/> instances with proper JSON schema.
/// Centralizes metadata construction that was previously scattered across PlumberEngine, EventAggregator, and tests.
/// </summary>
public class MetadataFactory
{
    private readonly IReadOnlyConventions _conventions;

    /// <summary>
    /// Creates a factory with default conventions (Created timestamp, InvocationContext enrichers).
    /// </summary>
    public MetadataFactory() : this(new Conventions()) { }

    /// <summary>
    /// Creates a factory with custom conventions.
    /// </summary>
    /// <param name="conventions">The conventions to use for metadata enrichment and stream naming.</param>
    public MetadataFactory(IReadOnlyConventions conventions) => _conventions = conventions;

    /// <summary>
    /// Creates metadata from raw EventStore fields.
    /// Used by PlumberEngine when reading events from EventStore.
    /// </summary>
    public Metadata Create(
        Guid id,
        string sourceStreamId,
        Guid eventId,
        long sourceStreamPosition,
        long? linkStreamPosition,
        JsonElement data)
    {
        return new Metadata(id, eventId, sourceStreamPosition, linkStreamPosition, sourceStreamId, data);
    }

    /// <summary>
    /// Creates metadata from an event object. Conventions compute the sourceStreamId and enrich metadata.
    /// The <see cref="Metadata.Id"/> is extracted from the event's Id property via duck typing if present.
    /// Used by EventAggregator and other in-process event sources.
    /// </summary>
    public Metadata Create(
        OperationContext context,
        object evt,
        object? id = null,
        object? customMetadata = null,
        IAggregate? aggregate = null)
    {
        var sourceStreamId = _conventions.StreamNameFromEventConvention(context, evt.GetType(), id);
        var enriched = _conventions.GetMetadata(context, aggregate, evt, customMetadata);
        var data = JsonSerializer.SerializeToElement(enriched);
        var eventId = _conventions.GetEventIdConvention(context, aggregate, evt).ToGuid();
        IdDuckTyping.Instance.TryGetGuidId(evt, out var metadataId);
        return new Metadata(metadataId, eventId, 0, null, sourceStreamId, data);
    }

    /// <summary>
    /// Creates metadata with explicit field values.
    /// Useful for tests and simple scenarios where you know the sourceStreamId.
    /// </summary>
    public Metadata Create(
        string sourceStreamId,
        DateTimeOffset? created = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        string? userId = null)
    {
        var data = BuildData(created, correlationId, causationId, userId);
        return new Metadata(Guid.Empty, Guid.NewGuid(), 0, null, sourceStreamId, data);
    }

    /// <summary>
    /// Creates metadata for a specific event type and id. Conventions compute the sourceStreamId.
    /// The <see cref="Metadata.Id"/> and <see cref="Metadata.EventId"/> are extracted from the event's Id property via duck typing if present.
    /// </summary>
    public Metadata Create<TEvent, TId>(
        TId id,
        TEvent evt,
        DateTimeOffset? created = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        string? userId = null)
    {
        var context = OperationContext.Create(Flow.Component);
        var sourceStreamId = _conventions.StreamNameFromEventConvention(context, typeof(TEvent), id);
        var data = BuildData(created, correlationId, causationId, userId);
        var eventId = _conventions.GetEventIdConvention(context, null, evt!).ToGuid();
        IdDuckTyping.Instance.TryGetGuidId(evt!, out var metadataId);
        return new Metadata(metadataId, eventId, 0, null, sourceStreamId, data);
    }

    private static JsonElement BuildData(
        DateTimeOffset? created,
        Guid? correlationId,
        Guid? causationId,
        string? userId)
    {
        IDictionary<string, object> obj = new ExpandoObject();
        if (created.HasValue) obj["Created"] = created.Value;
        if (correlationId.HasValue) obj["$correlationId"] = correlationId.Value;
        if (causationId.HasValue) obj["$causationId"] = causationId.Value;
        if (userId != null) obj["UserId"] = userId;
        return JsonSerializer.SerializeToElement(obj);
    }
}
