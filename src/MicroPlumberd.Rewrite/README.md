# `mp-rewrite`

Rewrites the history of a **KurrentDB running in docker** into a **new store**, then puts that store in place
of the old one. It never edits the old store: the previous data directory is renamed to `data.bak.<timestamp>`
and is never deleted by the tool.

You give it the id or name of the container, and optionally a JavaScript rule script.

```
mp-rewrite <container> [--script <file.js>] [--eval "<js>"] [--dry-run]
                       [--no-projection-copy] [--yes]
mp-rewrite <container> --rollback [<backup-dir>]
mp-rewrite <container> --status
```

| Option | Meaning |
|---|---|
| `--script <file.js>` | Rule script to run every event through. |
| `--user` / `--password` | Credentials for the store being rewritten (or `MP_REWRITE_USER` / `MP_REWRITE_PASSWORD`). Default `admin`/`changeit`. |
| `--eval "<js>"` | The same, inline. Mutually exclusive with `--script`. |
| `--dry-run` | Copy nothing. Print the per-rule counts and the names of every affected stream. |
| `--no-projection-copy` | Do not pre-create the app's user projections on the new store (default: copy them). |
| `--yes` | Skip the confirmation prompt. |
| `--rollback [dir]` | Swap the most recent backup (or the given one) back and restart the container. |
| `--status` | Print the data location, the backups present, and whether a rewrite is safe right now. |

**With no script the tool performs a pure copy — and that alone repairs a store.** System streams, `$et-*`
streams and `$>` link events are never copied, so a store whose command-router projection is Faulted on a
dangling link comes back with the links rebuilt from the events that actually exist. That is the case this
tool was written for.

## Installing

```bash
dotnet tool install -g MicroPlumberd.Rewrite      # needs the .NET SDK
```

Devices without an SDK (neurons) use the single-file build attached to each release — one file, nothing to
install:

```bash
curl -L -o mp-rewrite <release-asset-url>/mp-rewrite-<version>-linux-arm64
chmod +x mp-rewrite && ./mp-rewrite <container> --status
```

## What a run does

1. **Parses the script** — before anything else, so a typo costs nothing.
2. **Inspects the container**: image, environment, compose label, and which mount holds the store. The data
   directory is the mount whose destination matches `KURRENTDB_DB` / `EVENTSTORE_DB` when either is set,
   otherwise `/var/lib/kurrentdb` or `/var/lib/eventstore`.
3. **Guards** (below). These run before the tool opens its own connection to the store.
4. **Starts a scratch store** from the same image on an empty `data.new.<ts>` beside the current one.
5. **Copies every event** in `$all` commit order through the rules, and pre-creates the app's user projections
   so the merge streams are rebuilt in commit order.
6. **Verifies** the destination against what the copy believes it wrote — per-stream counts and a write
   checksum. A mismatch aborts *before* the swap.
7. **Swaps**: stop both containers, `data → data.bak.<ts>`, `data.new.<ts> → data`, start the original
   container, wait for `/health/live`, check that no projection is Faulted.

A real run appends a `MigrationApplied` record to the new store's `mp-migrations` stream — the script's
sha256, the rules it compiled to, and the counts — so the history of rewrites travels with the data.

## Refusals

The tool **refuses rather than warns**, and these two checks have no `--force`: if either is true, a writer
could lose data.

- **A sibling container of the same compose project is running.** Every container carrying the target's
  `com.docker.compose.project` label must be stopped first. The message names them.
- **A client has work in flight against the store.**

### The connected-client check, and exactly what it reads

`/stats` **cannot** answer this on KurrentDB 26.1. Its only connection counter is `proc.tcp.connections`,
which counts the **legacy TCP client protocol** — measured at 0 with a live gRPC `$all` subscription open.

The guard therefore reads the Prometheus endpoint `/metrics`:

| Metric | Used | Measured: no client / open `$all` subscription / client exited |
|---|---|---|
| `kurrentdb_current_incoming_grpc_calls` | **the guard** | 0 / **2** / 0 |
| `kurrentdb_kestrel_connections` | reported only | 1 / 2 / 1 |

`kurrentdb_current_incoming_grpc_calls` excludes the tool automatically: the guard's own request is plain
HTTP, not a gRPC call. `kurrentdb_kestrel_connections` counts that scrape — and would count a Prometheus
scraper too — so it is printed but never gated on, because a false refusal here has no override.

If the metric is **absent**, the tool refuses. A renamed counter must fail loudly, not silently switch the
guard off.

**Known limit:** it counts *open calls*, not *connected clients*. A client sitting idle with nothing in flight
reads as zero. In practice the real case is caught — an app holds a persistent `$all` subscription, and the
sibling-container guard covers the app being up at all.

## The script

The contract is the one [Kurrent Replicator](https://docs.kurrent.io/) documents, so a script written for
either tool runs on both.

- The file may define `function transform(original)`.
- `original` has `Stream`, `EventType`, `Data` (the payload as an object), `Metadata` (an object or
  `undefined`), plus read-only `EventId`, `EventNumber` and `Created` (an ISO-8601 string, so
  `e.Created < "2026-08-25"` orders correctly).
- It returns an object of the same shape. Returning `undefined`, or an object with an empty `Stream` or
  `EventType`, **drops** the event.
- `log.debug/info/warn/error(template, ...values)` writes to the tool's log.

On top of the plain function, helpers keep the common repairs to one line each. Helpers and `transform` may be
combined: helpers run first, and `transform` sees the survivors.

```js
dropStream("StartPipelineCommand-325888b9-621a-4f36-b091-40b8432799b2");
dropStream(/^Offer-of-/);
dropStream(s => s.startsWith("Recording-") && s.endsWith("-test"));
dropEvent(e => e.EventId === "8d8066f9-bfbb-423e-a990-3fa5f7c6d4d6");
dropEvent(e => e.EventType === "StopPipelineCommand" && e.Created < "2026-08-25");
update("SetPipelineProperties", e => { e.Data.Properties["pyl1.hdr-profile"] = 0; return e; });
updateById("c1ceefa2-363b-44ce-93a8-c54a0517550f", e => { e.Metadata.CorrelationId = e.Metadata.CausationId; return e; });
renameType("PipelineStarted", "PipelineStartedV2");
renameStream("Pipeline-old-id", "Pipeline-new-id");
```

Order within one event: `dropEvent` predicates → `update` / `updateById` handlers → `transform`.
`dropStream`, `renameStream` and `renameType` are applied before any of that, so a dropped stream never
reaches a handler at all.

**Sandbox.** The script runs in Jint with **no CLR access** — it cannot reach a .NET type, the filesystem or
the network. Recursion is capped at 64 frames, and all script code run for one event shares a single
**2-second** budget (not 2 seconds per call).

**Only JSON payloads are transformable.** A payload that is not JSON — or that claims to be and is not —
bypasses the script entirely, is copied byte-for-byte, and is counted in the report. It is never dropped.

**Numbers beyond 2^53.** JavaScript has one number type. A payload **no rule touched** is copied byte-for-byte
and is safe. The moment any rule modifies an event, its whole payload is re-rendered from JavaScript doubles,
and an integer larger than 2^53 (`9007199254740993` → `9007199254740992`) or a non-canonical literal (`1.0` →
`1`) changes. This is inherent to the script contract, not to this implementation. If your events carry int64
identifiers, restrict your rules to the streams that need them.

## Exit codes

| Code | Meaning | State of your store |
|---|---|---|
| `0` | The rewrite completed and was verified. | The container runs on the new store; the old one is in `data.bak.<ts>`. |
| `1` | A guard refused, or you declined at the prompt. | Untouched. Nothing was created or started. |
| `2` | The script does not parse (the message carries line and column). | Untouched. No container was started. |
| `3` | Docker unreachable, no such container, or the image could not be pulled. | Untouched. |
| `4` | The copy engine or the verification failed. | **Untouched.** The scratch store is removed. |
| `5` | The filesystem swap, or something after it, failed. | **Restored.** The original directory is back and the container is running on it. |

## Rollback and status

```bash
mp-rewrite my-eventstore --status      # where the data is, which backups exist, whether it is safe now
mp-rewrite my-eventstore --rollback    # put the most recent backup back
mp-rewrite my-eventstore --rollback /var/docker/data/data.bak.20260907T151200
```

A rollback is itself reversible: the rewritten store is moved to `data.rolledback.<ts>`, not deleted.

## Permissions, and why a helper container appears

The KurrentDB image runs as **uid 1001**. The operator running this tool usually does not.

- **The swap works anyway.** Renaming `data` needs write permission on its *parent* directory, not on `data`
  itself, so directories full of files owned by another uid swap fine.
- **The new directory is made group/other writable** before the scratch store is pointed at it, and keeps
  those permissions after the swap — that is what lets the original container go on writing.
- **Deleting `data.new.<ts>` needs root.** The store contains `index/stream-existence/` created with the
  container's own umask and owner, which the tool's user cannot remove. So on a dry run, and on any failure
  before the swap, the directory is removed by `rm -rf` inside a short-lived container from the same image,
  running as root, with the parent bind-mounted. **The tool never needs root on the host.** That helper is
  aimed only at a `.new.` directory this run created.

## Limits

- **Named volumes are refused.** The tool swaps stores by renaming directories, which needs a bind mount.
  `--force-volume-copy` is **reserved** for a future version that implements the named-volume path; passing it
  to this one exits `1` and says so, rather than being silently ignored.
- **Integers beyond 2^53** change in any payload a rule touches (see *The script* above).
- **The connected-client guard counts open gRPC calls**, not connected clients (see *Refusals* above).

## Not in scope

Live replication while the application runs (other containers are stopped by contract), editing binary or
protobuf payloads, and clusters — one node in one container.
