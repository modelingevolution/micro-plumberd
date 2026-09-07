# Dev-log: Index-backed merge streams (v1, shape-A) — implementation

Branch: `feature/mp-index-backed-merge`. Implements v1 (slices S1–S4) of
`docs/design-index-backed-merge-streams.md` exactly. Shape-B (S5) is out of scope (v2). No git actions performed
by the engineer — reviewed branch only.

Build with `dotnet.exe`. Every slice ends with a green integration test against a REAL KurrentDB 26.1
(`MicroPlumberd.Testing` `EventStoreServer.StartInDocker`, NEVER Testcontainers, no `ClearAllPools`).

## What shipped, by slice

### S1 — index-backed subscription primitive + shared subscription seam (core)
- **Relocated the index primitives into core** as `MicroPlumberd.UserDefinedIndex` and made `KurrentHttpEndpoint`
  a public core utility (added `FromSettings(KurrentDBClientSettings)` alongside the existing
  `Parse(connectionString)`). `MicroPlumberd.Migration` now has a `ProjectReference` → core and its
  `UserDefinedIndexSource` DELEGATES create/name/filter/stream to the core type — ONE implementation
  (CLAUDE.md "NEVER DUPLICATE"), dependency arrow Migration → core (design "Assembly layering", team-lead
  APPROVED). `UserDefinedIndexSource` keeps its offline-only members (`CountMatchingAsync`,
  `WaitUntilReadyAsync` count-gate, `ReadAsync`, `CreateWaitReadAsync`) unchanged.
- **`ISubscriptionState` seam** driving ONE `SubscriptionRunner` loop (no fork): `SubscriptionRunnerState`
  (existing stream path via `SubscribeToStream`, resume `FromStream.After(rev)`) and the new
  `IndexSubscriptionState` (filtered `SubscribeToAll(FromAll, resolveLinkTos:true,
  StreamFilter.Prefix($idx-user-<name>))`, resume `FromAll.After(pos)`). `SubscriptionRunner.WithHandler`
  changed only its two source-specific lines (`Subscribe()` / `Advance(e)`); dispatch, `ICaughtUpHandler`,
  5s-resubscribe-backoff, `FailFastException` are literally reused.
- **Loud guard** (SPIKE-2a/SPIKE-9 footgun): `SubscriptionRunnerState.Subscribe()` throws
  `InvalidOperationException` if the stream name starts with `$idx-user-` (a direct `SubscribeToStream` there
  silently delivers zero). Index streams are read ONLY via filtered `$all`.

### S2 — opt-in wiring + PROJECTION-vs-INDEX PARITY (the acceptance bar)
- **`MergeSource` enum** (`MicroPlumberd.Services`, default `Projection`). `AddEventHandler<T>` /
  `AddSingletonEventHandler<T>` / `AddScopedEventHandler<T>` gained `MergeSource mergeSource = Projection`;
  `EventHandlerStarter<T>.Configure` carries it and `Start()` routes to `SubscribeEventHandlerViaIndex<T>`.
- **`PlumberEngine.SubscribeEventHandlerViaIndex<T>`** (public): convention output stream + `GetEventNamesFor<T>`
  → reconcile (ensure index + return the name) → subscribe via `IndexSubscriptionState` in a `SubscriptionRunner`,
  same dispatch/converter as the projection path. Catch-up only. Logs handler → index → stream → types + start.
- **Registration guards (fail fast):** `UserDefinedIndex + persistently` → throw (SPIKE-7, permanent);
  `UserDefinedIndex + specific-revision start` → throw (only Start/End meaningful on filtered `$all`).

### S3 — create-new-and-swap reconciler (`UserDefinedIndexReconciler`)
- Filter-hashed name `mpidx-<normalized-output>-<hash8-of-filter>` (`UserDefinedIndex.IndexNameFor`,
  `hash8` = first 8 lower-hex of `SHA-256(BuildFilter(types))`, ordinal-sorted ⇒ stable per set). Changed
  event-type set ⇒ new name ⇒ read model rebuilds from `FromAll.Start`; unchanged ⇒ same name ⇒ 409 reuse ⇒
  no rebuild. Best-effort DELETE orphan cleanup of superseded `mpidx-<output>-*` via `ListNamesAsync` +
  `DeleteAsync` (correctness never depends on cleanup running).

### S4 — coexistence + permanent persistent guard + proliferation
- Projection-backed and index-backed handlers coexist in one engine; the projection default creates NO index;
  the persistent+index registration guard is permanent; N index-backed merges all build (SPIKE-6 headroom).

## Key decisions / deviations
- **Relocation over copy** (design-mandated): required adding `Migration → MicroPlumberd` `ProjectReference`.
  This surfaced a namespace collision — `MicroPlumberd.StreamMetadata` began shadowing
  `KurrentDB.Client.StreamMetadata` at an unqualified use site in `ProjectionCopier.cs`; fixed by fully
  qualifying that one `new KurrentDB.Client.StreamMetadata(...)`. No behavior change (the 6 Migration
  integration tests remain green = the S1-T5 relocation-parity guard).
- **CaughtUp semantics on the index path (empirical nuance, not a defect — DOCUMENTED at the opt-in surface):**
  an index-backed subscription's history→live boundary tracks the `$all` position, so while the index is still
  backfilling, `CaughtUp` can fire BEFORE the backfilled links arrive as "live". The guaranteed properties are
  CaughtUp-fires + no-loss + commit-order — NOT the projection output-stream path's strict "all history
  processed before CaughtUp" timeline. S2-T3 asserts the guaranteed properties.
  **Consumer guidance (surfaced at the decision point):** because this is a per-handler opt-in, the trade-off is
  documented in XML docs on `MergeSource.UserDefinedIndex`, on the `mergeSource:` parameter of
  `AddEventHandler`/`AddSingletonEventHandler`/`AddScopedEventHandler`, and on
  `PlumberEngine.SubscribeEventHandlerViaIndex<T>` — a read model that uses `ICaughtUpHandler.CaughtUp()` as a
  "fully caught up / now authoritative" readiness signal should NOT opt into index-backing (or must tolerate the
  weaker guarantee); eventual delivery of all history + commit ordering are still guaranteed.
- **`HttpEndpoint` from settings:** core derives the management base + basic-auth from
  `KurrentDBClientSettings.ConnectivitySettings.Address` (or first gossip seed) + `DefaultCredentials` — the
  same node the gRPC client talks to; mirrors the existing `WaitUntilReady` pattern.
- Removed the dead `SubscriptionRunnerState.Handler` property (only ever assigned, never read).

## Test results (all against real KurrentDB 26.1, image with `/v2/indexes`)
- **S1:** 4/4 — catch-up→live in commit order; resume from recorded `$all` position; footgun guard throws;
  stream-backed path no-regression. Plus **6/6** pre-existing `UserDefinedIndexIntegrationTests` (Migration
  relocation is byte-identical — S1-T5).
- **S2:** 4/4 — **PARITY (S2-T1, the acceptance bar): the same read model fed the same interleaved events via
  the projection path and the index path reached IDENTICAL final state in the IDENTICAL delivery order**;
  live-after-boot; ICaughtUpHandler fires; registration guards reject persistent + revision-start.
- **S3:** 3/3 — definition change → new filter-hashed index with widened content; no-op when unchanged;
  superseded index DELETEd (orphan cleanup verified end-to-end, incl. `ListNamesAsync` parse + DELETE).
- **S4:** 4/4 — projection + index coexist; default creates no index; persistent+index rejected (projection
  persistent untouched); 6 concurrent index-backed merges all build.
- **Regression:** whole solution builds 0 errors; existing subscription/read-model suites pass. One
  timing-sensitive from-End test (`ReadModelTests.SubscribeModelFromEnd`) flaked once under parallel Docker
  load but passes 3/3 in isolation — pre-existing flakiness, the refactored path is behavior-identical.

## Review follow-ups closed (post-approval, before v1 lands)
- **Acceptance-path integration test (was compile-checked only).** Added a full-DI test
  (`IndexBackedMergeReviewFollowupsTests.DiAcceptancePath`): a consumer registers
  `AddSingletonEventHandler<T>(mergeSource: MergeSource.UserDefinedIndex)`, boots a real `TestAppHost`
  (`Host.StartAsync` → `EventHandlerService` → `EventHandlerStarter.Start` →
  `SubscribeEventHandlerViaIndex<T>(eh: null)`), and the DI-resolved singleton read model folds index-delivered
  events to the expected state + tails a live append. Exercises the `eh == null` DI-resolution branch
  (`SubscriptionRunner.WithHandler<T>(func)`) + starter routing the other tests bypassed by passing an instance.
- **Cross-output orphan-delete collision guard (data-destructive edge).** Two distinct output streams whose names
  NORMALIZE to the same managed base (e.g. `"Foo.1"` and `"Foo-1"` → `"foo-1"`) would share the
  `mpidx-<base>-*` prefix, so reconciling one could DELETE the other's live index. Added
  `UserDefinedIndex.RegisterManagedBase(outputStream)` — a process-wide owner registry keyed on the normalized
  base — called first in `UserDefinedIndexReconciler.ReconcileAsync` (before any DELETE). A collision throws a
  clear `InvalidOperationException` at reconcile; re-registering the SAME output stream is idempotent. Unit test
  `ManagedBase_collision_is_rejected_same_name_is_idempotent` (no server).
- **Deferred (team-lead logged as follow-ups):** a mid-backfill CaughtUp reorder-window integration test; the
  `NormalizeName` doc-charset nit.

## Files
### Added (core `src/MicroPlumberd/`)
- `KurrentHttpEndpoint.cs` — relocated, public, `+FromSettings`.
- `UserDefinedIndex.cs` — relocated primitives + `EnsureAsync`/`DeleteAsync`/`GetFilterAsync`/`ListNamesAsync`/
  `IndexNameFor`/`ManagedNamePrefixFor`.
- `ISubscriptionState.cs` — the seam + `IndexSubscriptionState`.
- `UserDefinedIndexReconciler.cs` — `IIndexDefinitionReconciler` + `UserDefinedIndexReconciler`.
### Added (`src/MicroPlumberd.Services/`)
- `MergeSource.cs`.
### Added (tests `src/MicroPlumberd.Tests/Integration/`)
- `IndexBackedMergeS1Tests.cs`, `IndexBackedMergeS2Tests.cs`, `IndexBackedMergeS3Tests.cs`,
  `IndexBackedMergeS4Tests.cs`, `IndexBackedMergeReviewFollowupsTests.cs` (DI acceptance path + collision guard).
### Changed
- `src/MicroPlumberd/SubscriptionRunner.cs` — `SubscriptionRunnerState : ISubscriptionState` (+guard, +Advance),
  runner ctor `ISubscriptionState`, loop uses `Advance(e)`.
- `src/MicroPlumberd/PlumberEngine.cs` — store settings; lazy `UserDefinedIndex`/reconciler;
  `SubscribeEventHandlerViaIndex<T>` + `IsTailOnlyStart`.
- `src/MicroPlumberd.Services/EventHandlerStarter.cs` — `Configure(..., MergeSource)` + `Start` routing.
- `src/MicroPlumberd.Services/ContainerExtensions.cs` — `mergeSource` params + `ValidateMergeSource` guards.
- `src/MicroPlumberd.Migration/MicroPlumberd.Migration.csproj` — `ProjectReference` → core.
- `src/MicroPlumberd.Migration/UserDefinedIndexSource.cs` — delegates create/name/filter to core.
- `src/MicroPlumberd.Migration/ProjectionCopier.cs` — qualify `KurrentDB.Client.StreamMetadata`.
### Removed
- `src/MicroPlumberd.Migration/KurrentHttpEndpoint.cs` — relocated to core.
