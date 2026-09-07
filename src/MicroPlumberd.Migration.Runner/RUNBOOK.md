# mp-migrate — offline event-store rewrite runbook

`mp-migrate` rewrites a SOURCE KurrentDB store into a fresh DEST store, applying raw (non-typed)
migration rules. It is offline: point it at two already-running stores.

```
mp-migrate --source <source-conn> --dest <dest-conn> [--dry-run]
```

- `--source` — SOURCE connection string (read-only).
- `--dest`   — DEST connection string. Must be a **fresh, empty** store.
- `--dry-run` — report per-migration Kept/Dropped/Renamed/Transformed counts; write nothing.

## Topology

Provide the topology externally (e.g. docker): source ES on the existing volume mounted read-only,
dest ES on a fresh volume, both up. The tool only needs the two connection strings.

## OPERATIONAL REQUIREMENT — standard projections MUST be enabled on DEST

The DEST EventStore **must run with standard projections enabled and running** — specifically
`$by_event_type` (and `$by_category`). For a KurrentDB container:

```
KURRENTDB_RUN_PROJECTIONS=All
KURRENTDB_START_STANDARD_PROJECTIONS=true
```

Why: `[OutputStream]` merge/read-model streams are **not copied**. They are built by continuous
`linkTo` JOIN projections that the application re-registers on boot
(`SubscribeEventHandler(ensureOutputStreamProjection=true)`), reading from `$et-{EventType}`. The
migration tool deliberately **SKIPS** every `$>` link event (and `$ce`/`$et`/system streams) — a
count is reported as `Merge/link events skipped`. On the dest, the app's projection regenerates each
output stream from `$et`, in order, with correct dest links and no dead links (the dropped/tombstoned
aggregate stream is gone). If standard projections are OFF, `$et` never repopulates, so the
regenerated join/output streams stay **empty** and read models project nothing.

Do **not** try to rebuild the merge streams during migration: the app's re-registered projection would
emit the same links a second time = duplicate links = corruption. Skip-and-regenerate is the design.

## What the tool does

- Reads SOURCE `$all` in commit order (`resolveLinkTos:false`, so tombstone/deleted-stream artifacts
  are never dereferenced — no NRE).
- Three stream classes: `$`-system streams SKIPPED; `[OutputStream]` merge/link (`$>`) streams
  SKIPPED + counted; aggregate/domain streams copied raw with per-stream expected version (gapless),
  rules applied (Drop / Rename type / Transform JSON / Rename stream). Appends are BATCHED and capped by
  both event COUNT and cumulative BYTES so no single append exceeds KurrentDB's limits; a stream larger than
  one batch is flushed across several appends (gapless version preserved).
- Copies each event's metadata verbatim (correlation/causation/Created preserved) and stamps
  `MigratedAt` (the run's UTC time). The **original event id is preserved** (each event is written once to a
  fresh dest, so keeping the id holds causation/correlation-by-id refs + the idempotency key; `linkTo` uses
  stream+position, not id, so read-model rebuild is unaffected).
- An event whose payload is **declared JSON but unparseable is NEVER dropped** — it is copied VERBATIM
  (byte-for-byte) and reported as `UnparseableVerbatim` (a warning, not data loss). Only an explicit rule
  (DropStream/DropStreams/DropEvent) drops an event.
- History travels in the `mp-migrations` stream (one `MigrationApplied` record per applied migration).
  On each run it carries existing history forward, computes PENDING = defined-but-not-applied, and
  refuses to run if an already-applied migration's checksum changed.
- Prints a verification report: per destination stream, expected vs actual counts, final versions, AND a
  **write-fidelity checksum** — a per-stream SHA-256 over each written event's `(EventType || 0x00 || Data)`
  recomputed by re-reading the dest, which catches dest-side reorder/truncation/corruption a count check
  cannot. It verifies the copy INTENT reached the dest faithfully; it does NOT re-derive from the source or
  re-check transform semantics (tests cover those). Any mismatch not explained by an intended drop is flagged.

## Migration authoring notes

- **Prefer literal arguments** (`DropStream("X")`, `DropStreams(...)`, `RenameType`, `RenameStream`) for
  anything whose applied-history checksum must stay stable. Their checksum is over the literal text and is
  build-independent. `_0001` is literal-only.
- **Delegate-based ops** (`DropStream(predicate)`, `DropEvent`, `TransformJson`) are checksummed over the
  delegate's IL body. Two caveats: (a) IL can differ across build configuration / compiler / TFM, so an
  already-applied delegate migration can trip the checksum guard on a later run under a different build —
  build/run delegate-based migrations with a **pinned configuration**; (b) **the checksum does NOT see values
  captured by the lambda** (e.g. a captured constant/list) — it hashes the delegate's IL body only, so two
  migrations differing ONLY in a captured value hash the SAME and the guard will NOT catch such an edit (m7).
  Keep delegate migrations self-contained (inline literals, no captured state) and avoid editing them after
  they are applied.
- **Id ordering.** Migrations are applied in ORDINAL id order. Mixed-width numeric prefixes silently misorder
  (`"10"` sorts before `"2"`); the runner WARNS on inconsistent widths — zero-pad ids to equal width (`0001`,
  `0002`, …).
- **Stream/type targets are guarded.** A `RenameStream` target in `$`-space or the reserved `mp-migrations`
  stream, or a `RenameType` target in `$`-space, is REJECTED up front (such events would be skipped as system
  and vanish). Likewise a `DropStream`/`DropStreams` that names a `$`-stream is now HARD-REJECTED — previously
  it was a silent no-op (the copy never touches `$`-streams anyway), which could mask a typo. Also note
  `RenameType` on a type feeding an `[OutputStream]` projection breaks its linking (below).
- **Schema evolution of history (m10).** `MigrationApplied` is the persisted history record. Adding a
  `required` field to it will BREAK deserialization of history written by an older tool version (old records
  lack the field). Add new history fields as OPTIONAL (nullable / defaulted), never `required`.

## `[OutputStream]` ordering — the paced PROJECTION COPY (opt-in)

The app's `[OutputStream]` merge stream X is fed by a JOIN projection
`fromStreams(['$et-A','$et-B']).linkTo('X')`. Catching that up over a **backlog** that spans multiple `$et`
streams **TYPE-CLUSTERS** the output (drains all `$et-A`, then all `$et-B`): `[A0,B1,A2,B3,A4] → 0,2,4,1,3`
(proven by `Spike1_FromStreams_join_ordering_interleaved_vs_type_clustered`). For an order-SENSITIVE read
model (e.g. the ERP `OffersReadModel` state machine) that corrupts the final state, so the merge stream must
be rebuilt in **commit order**.

**The fix lives entirely in this tool** (no framework change): the optional `ProjectionCopyContext` turns on
the **paced projection copy** —

1. Pre-create the app's join projections on the EMPTY dest (discover the source's user projections, read each
   query VERBATIM over HTTP `GET /projection/{name}/query`, `CreateContinuous→Disable→Update(emit:true)→Enable`,
   then stamp `mp_query_hash = SHA256(query)` on the projection's stream so the app **NO-OPs on boot**).
2. Copy events in `$all` order but **PACED**: after each kept event, wait until the join projection(s) have
   emitted its link (the merge stream's link count advances by 1) BEFORE writing the next event. The projection
   therefore processes events **one at a time, in commit order, and never accumulates a backlog** — so the
   type-clustering (a backlog-catch-up property) can never happen. Final drain to head.

The pacing keys on the **actual link emission**, not a timer, and is **bounded**: if a projection doesn't emit
a link within the per-event timeout it FAILS LOUD (aborts the run, naming the stalled projection/stream/event)
rather than hanging.

Determinism is the bar: `Paced_projection_copy_preserves_interleaved_order_deterministically_15x` runs the
interleaved recovery **15×** and requires **15/15** in arrival order; the end-to-end
`Paced_projection_copy_recovers_tombstoned_store_in_commit_order_without_NRE` proves the full `:5081` recovery
(drop the tombstoned stream, rebuild the merge stream in commit order, read model replays with no NRE, app
boots NO-OP).

> **PERFORMANCE — read before pointing this at a huge store.** Strict per-event pacing is **O(n) SEQUENTIAL**:
> one link-emission round-trip per linkable event. For the ERP (~1070 events) that is seconds-to-minutes —
> fine (correctness over speed). It is **NOT** suitable for very large stores (e.g. millions of events) without
> **batched pacing** (pace per small batch with a per-batch drain — trades a tiny clustering risk for speed).
> That batched mode is a future optimization and is deliberately NOT implemented; strict per-event pacing is
> what gives the determinism guarantee above.

Without a `ProjectionCopyContext` the tool runs the **simple copy**: it SKIPS the `$>` merge/link streams and
lets the app regenerate them on boot — correct ONLY if the app's projection is itself commit-ordered, which
`fromStreams` is not for a multi-`$et` merge. Use the projection copy whenever merge-stream order matters.

### Library limitation — `RenameType` vs `[OutputStream]` selectors

A migration that **renames an event type** feeding an `[OutputStream]` projection breaks that projection's
linking: the projection selects the OLD `$et-{oldType}`, but the migrated events now carry the new type and
land in `$et-{newType}`, so they are never linked into the output stream. Not an issue for the `:5081`
tombstone fix (`_0001` is `DropStreams`-only), but a real limitation to weigh before using `RenameType` on
a type that feeds a merge/read-model stream.

## Exit codes

`0` ok · `1` verification mismatch (count OR write-fidelity checksum) · `2` bad args · `3` checksum guard
tripped. (Unparseable-JSON payloads are copied verbatim and do NOT fail the run.)
