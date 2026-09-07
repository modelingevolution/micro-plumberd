# Dev-log — User-Defined-Index merge read-path (feature/mp-userdefined-index)

Audience: the reviewer/tester (task B2). This is the CLEAN alternative to the paced `ProjectionCopier`,
using KurrentDB 26.1 **user-defined indexes**. `ProjectionCopier` is kept intact — both strategies ship.

## What problem this solves

An app-style `fromStreams(['$et-A','$et-B']).linkTo(X)` join projection **type-clusters** on catch-up: it
drains `$et-A` then `$et-B`, so a merged read comes out `0,2,4,1,3` instead of commit order `0,1,2,3,4`
(proven in `ProjectionBehaviorSpikes`). `ProjectionCopier` works around it by PACING the copy event-by-event
so a backlog never forms. A **user-defined index** is read through the filtered-`$all` API, which yields
events in native commit order with **no pacing and no backlog** — exactly what a migration merge needs.

## Files

Added (`src/MicroPlumberd.Migration/`):
- `UserDefinedIndexSource.cs` — the reader (public). create → poll-until-ready → stream `RawEvent`s in commit order.
- `UserDefinedIndexCopyContext.cs` — runner-facing context + `CreatedIndex` record + `UserDefinedIndexMergeBuilder`
  (discovers the app's join projections on the source, creates one index per merge on the dest).
- `KurrentHttpEndpoint.cs` — shared HTTP-endpoint/creds parser extracted from `ProjectionCopier` (DRY;
  used by both the projection copier and the index source).

Changed:
- `ProjectionCopier.cs` — now uses `KurrentHttpEndpoint` (its private `ParseHttpEndpoint`/`CreateHttpClient` removed). Behaviour unchanged.
- `MigrationRunner.cs` — `RunAsync` gained an `UserDefinedIndexCopyContext? indexCopy = null` parameter
  (mutually exclusive with `projectionCopy` — supplying both throws); `MigrationRunResult.CreatedIndexes` added.

Tests (`src/MicroPlumberd.Migration.Tests/`):
- `UserDefinedIndexIntegrationTests.cs` — 4 integration tests against real KurrentDB 26.1 (same
  `EventStoreServer` Docker fixture as the rest; NEVER Testcontainers; no `ClearAllPools`).

## Read-path design (verified empirically against KurrentDB 26.1.0.3443)

1. **Create** — `POST /v2/indexes/{name}` with body `{"filter":"rec => rec.schema.name == \"A\" || rec.schema.name == \"B\"","start":true}`.
   - The filter MUST be a single-arg JS arrow function (`rec => …`) or KurrentDB returns HTTP 400.
   - Index names: **lower-case alphanumerics, `_`, `-` only** (else HTTP 400). `NormalizeName` lower-cases +
     replaces every other char with `-`; the runner appends an 8-char SHA-256 suffix of the original stream
     name for collision-safety.
   - Idempotent: an identical re-POST is HTTP 200; an existing index is HTTP 409 `INDEX_ALREADY_EXISTS` →
     treated as success (a differing filter is logged as drift, existing kept — KurrentDB won't redefine in place).
2. **Poll until ready (AUTHORITATIVE — expected-count, not a heuristic)** — `GET /v2/indexes/{name}` reports
   `INDEX_STATE_STARTED` **immediately**, BEFORE the backfill completes, and exposes **no checkpoint/progress**
   (I probed `GET`, `?stats`, `?details`, `/stats`, `/checkpoint` on 26.1.0.3443 — only name/filter/fields/state).
   The gotcha: the filtered `$all` read of `$idx-user-{name}` **throws `RpcException NotFound` while the index is
   still building** (NOT empty). So readiness is measured against the **known total**:
   `WaitUntilReadyAsync(name, expectedCount, timeout)` — (phase 1) waits for the resource to be STARTED (404 GET
   = still building); (phase 2) polls the filtered read, treating `NotFound`/short counts as "still building",
   and returns only when the indexed count **equals `expectedCount` exactly** (exceeding it throws — the filter
   matched more than the store holds). `expectedCount` comes from `CountMatchingAsync(eventTypes)`, an exact
   count of matching events in the indexed client's `$all` (immediately consistent after the copy, so it is a
   KNOWN total, not an estimate). Bounded by a timeout → `TimeoutException` reporting how far the backfill got,
   so a stalled/short build FAILS LOUD instead of a truncated merge. This replaced the earlier stability
   heuristic (B2 MUST-FIX #1).
   - The gate count (`TryCountAsync`) uses the SAME read shape as `ReadAsync` — `resolveLinkTos:true` and skips a
     null-resolving (dead-link) event — so the count the gate waits on can NEVER diverge from what the reader
     yields (a `resolveLinkTos:false` count could hit the total while `ReadAsync` silently drops a dead link).
   - Event-type names are JS-escaped in the filter (`BuildFilter`/`EscapeJsString`) so a name with a quote,
     backslash or control char can't break out of the `rec.schema.name == "…"` literal.
3. **Read in commit order** — `ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix("$idx-user-{name}"), resolveLinkTos: true)`
   → yields the RESOLVED original events as `RawEvent`s (StreamId/EventNumber = the source event's stream +
   revision; Data/Metadata = raw JSON, Data null for non-JSON). This is true `$all` commit order — NOT type-clustered.

`ILogger` at every seam (index name + the failing URI on error); defaults to `NullLogger` (WASM-safe).

## Runner wiring

`MigrationRunner.RunAsync(src, dst, migrations, dryRun, projectionCopy: null, indexCopy: ctx)`:
after the plain copy has written every aggregate stream to the fresh dest, `UserDefinedIndexMergeBuilder`
discovers the app's join projections on the source (reusing `ProjectionCopier.DiscoverAsync` → OutputStream +
`$et-` link types), and for each creates + waits-ready one user-defined index on the DEST filtering those
types. Result: `MigrationRunResult.CreatedIndexes` — each entry names the read stream (`$idx-user-…`) a
consumer subscribes to for that merge, in commit order.

Boundary (honest) — **CONSUMER REWIRING REQUIRED** (B2 MUST-FIX #2, now surfaced loudly): the index path does
NOT physically rebuild the `[OutputStream]` merge streams (no `$>` links written, no `mp_query_hash`) — that is
`ProjectionCopier`'s job and it is unchanged. The index path's contract is "consumers read `$idx-user-{name}`
in commit order." A consumer left pointed at the physical merge stream will see it EMPTY. This is now called
out in three places: the `UserDefinedIndexCopyContext` XML doc (⚠ block), the `CreatedIndex` /
`MigrationRunResult.CreatedIndexes` docs, and a runtime `LogWarning` emitted by `UserDefinedIndexMergeBuilder`
listing each merge stream and its replacement `$idx-user-…` read stream.

## Andon / environment notes for the tester

- Test image `docker.kurrent.io/kurrent-latest/kurrentdb:latest` == **26.1.0.3443** — supports `/v2/indexes`. If a
  future image is <26.1 and lacks `/v2/indexes`, that is an Andon: STOP (the create returns 404/unsupported).
- The suite spins one KurrentDB container per test (unique GUID names). Test (d) starts 3 (source + 2 dests).
- The NuGet feed (`nuget.modelingevolution.com`) was flaky; if `NU1301`, restore with
  `-p:RestoreSources="https://api.nuget.org/v3/index.json;https://nuget.modelingevolution.com/v3/index.json"`.

## Test coverage (what each asserts)

- `Index_reads_interleaved_multitype_merge_in_commit_order_not_type_clustered` — (a)+(b) CORE: A0,B1,A2,B3,A4
  reads back `0,1,2,3,4`, explicitly NOT the type-clustered `0,2,4,1,3`.
- `Create_then_immediately_wait_ready_yields_complete_backfilled_read` — (a) ready-poll waits for the full
  backfill (50 events) even called immediately after create (NotFound-while-building handled).
- `Ensure_is_idempotent_on_recreate` — (c) second identical `EnsureAsync` does not throw; read unchanged.
- `WaitUntilReady_is_authoritative_waits_for_exact_count_and_times_out_if_short` — B2 MUST-FIX #1 proof:
  `CountMatchingAsync` returns the exact total; waiting for that total succeeds and reads all N in order;
  waiting for N+1 (unreachable) `TimeoutException`s instead of falsely settling — proves the readiness is
  authoritative, not a stability heuristic.
- `Large_merge_reads_back_full_count_in_commit_order_no_truncation` — B2 MUST-FIX #1 large-backfill proof:
  20,000 events interleaved across two types (backfill spans many poll intervals, ~30s), read back FULL count
  in commit order `0..19999` — proves no truncated tail regardless of how the backfill paces.
- `Index_copy_matches_projection_copy_merged_order_and_faithful_aggregate_copy` — (d) a full migration with
  `UserDefinedIndexCopyContext` verifies OK (faithful aggregate copy) and its dest index reproduces the SAME
  merged commit order (`a-created,a-refined,b-created,b-refined`) that the `ProjectionCopyContext` path builds
  into the physical `FooModel_v1` — the two strategies are cross-checked against one source.

## Results

Clean full run of `MicroPlumberd.Migration.Tests` against real KurrentDB 26.1.0.3443 (Docker), after the
environment restart, feed fallback applied:

```
Total tests: 43   Passed: 43   Failed: 0   Skipped: 0   (5.6 min)
```

- All 4 new `UserDefinedIndexIntegrationTests` PASS (commit-order 16s, ready-poll 15s, idempotent 15s,
  index-vs-projection-copy 56s).
- The existing `IncidentReplayIntegrationTests` (which exercise the refactored `ProjectionCopier`) PASS,
  including the 15/15 paced-copy determinism gate — `ProjectionCopier` is unregressed by the
  `KurrentHttpEndpoint` extraction.
- Build: 0 errors (test project + all its ProjectReferences incl. `MicroPlumberd.Migration.Runner`, the one
  consumer of the changed `RunAsync` signature — the new `indexCopy` param is optional and inserted before the
  optional `ct`; no caller passed `ct` positionally).

One bug was found and fixed DURING testing: the filtered `$all` read throws `RpcException NotFound` while the
index backfills (not empty) — readiness now handles it (see phase-2 above). It surfaced only under the full
parallel suite (slower backfill), not in isolation — a genuine heisenbug the single-test run hid.
