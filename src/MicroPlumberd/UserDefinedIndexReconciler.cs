using Microsoft.Extensions.Logging;

namespace MicroPlumberd;

/// <summary>
/// Idempotently "ensure the current merge index exists and return the index NAME to subscribe to". Kept behind a
/// seam so a future in-place-redefine strategy could drop in IF/when KurrentDB ships index-definition updates —
/// today that is impossible (SPIKE-5: a redefine POST is 409-rejected), so
/// <see cref="UserDefinedIndexReconciler"/> (create-new-and-swap) is the sole implementation.
/// </summary>
interface IIndexDefinitionReconciler
{
    /// <summary>
    /// Ensures the current filter-hashed index for <paramref name="outputStream"/>/<paramref name="eventTypes"/>
    /// exists (creating a new one on an event-type-set change, reusing an unchanged one), deletes superseded
    /// managed indexes for the same output stream, and returns the index NAME the caller should subscribe to.
    /// </summary>
    Task<string> ReconcileAsync(string outputStream, IReadOnlySet<string> eventTypes, CancellationToken ct);
}

/// <summary>
/// THE settled, permanent lifecycle for an index-backed merge (SPIKE-5): a filter-hashed index name
/// (<c>mpidx-&lt;output&gt;-&lt;hash8&gt;</c>) means a changed event-type set deterministically yields a NEW name,
/// so the read model rebuilds from the new merged view (the projection <c>disable→update→enable</c> analog).
/// Unchanged type set ⇒ same name ⇒ 409 reuse ⇒ nothing created/deleted/rebuilt. After ensuring the current
/// index, superseded <c>mpidx-&lt;output&gt;-*</c> indexes are DELETEd (best-effort — correctness never depends on
/// cleanup running; an un-deleted orphan is harmless).
/// </summary>
sealed class UserDefinedIndexReconciler(UserDefinedIndex index, ILogger? logger = null) : IIndexDefinitionReconciler
{
    public async Task<string> ReconcileAsync(string outputStream, IReadOnlySet<string> eventTypes, CancellationToken ct)
    {
        // Fail LOUD on a punctuation collision before doing any (potentially destructive) DELETE cleanup: two
        // distinct output streams that normalize to the same managed base would share the mpidx-<base>-* prefix.
        UserDefinedIndex.RegisterManagedBase(outputStream);

        var filter = UserDefinedIndex.BuildFilter(eventTypes);
        var name = UserDefinedIndex.IndexNameFor(outputStream, filter);

        await index.EnsureAsync(name, eventTypes, ct: ct).ConfigureAwait(false);

        // Best-effort orphan cleanup: DELETE any managed index for THIS output stream whose hash suffix differs
        // from the current one (an old event-type set superseded by this reconcile). Single-instance boot-time
        // cleanup is safe (this app is the only reader and has already cut over to `name`).
        var prefix = UserDefinedIndex.ManagedNamePrefixFor(outputStream);
        foreach (var existing in await index.ListNamesAsync(ct).ConfigureAwait(false))
        {
            if (existing.StartsWith(prefix, StringComparison.Ordinal) &&
                !string.Equals(existing, name, StringComparison.Ordinal))
            {
                logger?.LogInformation("Reconciler deleting superseded index '{Old}' (current is '{Current}').",
                    existing, name);
                await index.DeleteAsync(existing, ct).ConfigureAwait(false);
            }
        }

        return name;
    }
}
