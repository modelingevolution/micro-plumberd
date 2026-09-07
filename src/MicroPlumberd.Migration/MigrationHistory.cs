using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroPlumberd.Migration;

/// <summary>
/// Reads and writes the applied-migration history that travels inside the store.
/// </summary>
/// <remarks>
/// The history lives in a dedicated stream (<see cref="StreamName"/>). The copy engine treats it as a
/// reserved stream and skips it, so history is never copied twice: this class owns reading it from the
/// source and re-writing it (existing + newly-applied records) to the destination. A non-<c>$</c> name is
/// used deliberately so writing history needs no <c>$admins</c> privilege — any authenticated connection
/// can author it. (The owner's brief suggested <c>$mp-migrations</c> or a clearly-named non-<c>$ce</c>
/// stream; this is the latter.)
/// </remarks>
internal sealed class MigrationHistory(ILogger? logger = null)
{
    /// <summary>The dedicated history stream. Skipped by the copy engine and reserved from user rules.</summary>
    public const string StreamName = "mp-migrations";

    /// <summary>Event-type string for a <see cref="MigrationApplied"/> record.</summary>
    public const string EventType = nameof(MigrationApplied);

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    /// <summary>Reads all applied-migration records from <paramref name="client"/>'s history stream, in order.</summary>
    public async Task<IReadOnlyList<MigrationApplied>> ReadAsync(KurrentDBClient client, CancellationToken ct = default)
    {
        var result = client.ReadStreamAsync(Direction.Forwards, StreamName, StreamPosition.Start,
            resolveLinkTos: false, cancellationToken: ct);

        if (await result.ReadState.ConfigureAwait(false) == ReadState.StreamNotFound)
        {
            _logger.LogInformation("No migration history stream ({Stream}) — treating as empty.", StreamName);
            return [];
        }

        var applied = new List<MigrationApplied>();
        await foreach (var re in result.ConfigureAwait(false))
        {
            var er = re.Event;
            if (er is null || er.EventType != EventType) continue;
            var record = JsonSerializer.Deserialize<MigrationApplied>(er.Data.Span, JsonOptions);
            if (record is not null) applied.Add(record);
        }
        _logger.LogInformation("Read {Count} applied migration(s) from history.", applied.Count);
        return applied;
    }

    /// <summary>
    /// Rewrites the full history to <paramref name="client"/>'s destination stream: the pre-existing records
    /// (verbatim) followed by the newly-applied records. Idempotent per run — call once after a successful copy.
    /// </summary>
    public async Task WriteAsync(KurrentDBClient client, IReadOnlyList<MigrationApplied> existing,
        IReadOnlyList<MigrationApplied> newlyApplied, DateTime runTimeUtc, CancellationToken ct = default)
    {
        var all = existing.Concat(newlyApplied).ToArray();
        if (all.Length == 0) return;

        var events = all.Select(r => ToEventData(r, runTimeUtc)).ToArray();
        // Fresh destination history: expect NoStream. History is authored only by this tool.
        await client.AppendToStreamAsync(StreamName, StreamState.NoStream, events, cancellationToken: ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Wrote {Total} migration history record(s) to {Stream} ({New} newly applied).",
            all.Length, StreamName, newlyApplied.Count);
    }

    private static EventData ToEventData(MigrationApplied record, DateTime runTimeUtc)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        var meta = new JsonObject
        {
            ["Created"] = record.AppliedAtUtc,
            ["MigratedAt"] = runTimeUtc
        };
        var metaBytes = Encoding.UTF8.GetBytes(meta.ToJsonString());
        return new EventData(Uuid.NewUuid(), EventType, data, metaBytes);
    }
}
