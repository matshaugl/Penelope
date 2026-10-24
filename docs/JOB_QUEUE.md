# Job queue

Canonical document for job states, job kinds, and the job lifecycle. The code source of truth is `JobState` / `JobStateNames` in `Penelope.Abstractions/JobState.cs`. Product behavior of jobs (what the person sees) is in the [product specification](product-specification.md#jobs). Wire format is in [`API.md`](API.md#jobs).

## Overview

Long-running work runs on `JobRunner` in the host. Plugins enqueue work through `IJobQueue` (`HostJobQueue` adapter). Jobs live in host memory only; closing Penelope ends them and forgets them.

## Canonical job states

| State | API value | Meaning | Active / terminal |
| --- | --- | --- | --- |
| Pending | `pending` | Created, work not started yet | Active |
| Running | `running` | Work in progress | Active |
| Completed | `completed` | Finished successfully | Terminal |
| Failed | `failed` | Finished unsuccessfully. A failure card is present. | Terminal |
| Cancelled | `cancelled` | Stopped by the person or the system before finishing | Terminal |

```text
pending ──► running ──► completed
   │           ├──────► failed
   │           └──────► cancelled
   └──────────────────► cancelled / failed   (reserved for pre-start cancellation or failure)
```

Rules:

- A terminal state never changes again (`JobRecord.Complete` ignores later calls).
- `JobStateNames.IsActive` is true for `pending` and `running`. Only active jobs can be cancelled, and only while the module allows it (`cancelAllowed`).
- **Completed means success.** Never use "completed" to mean "finished in any way"; say "terminal" or "ended" for that.
- Every surface (C# enum, JSON, TypeScript `Job.state`, events, docs, UI copy) uses exactly these five names.

### Why these names

An earlier draft of the product specification used `queued` / `succeeded`. The implementation, API, and UI shipped with `pending` / `completed`. We kept `pending` / `completed` because:

1. **It matches what exists.** The C# enum, JSON API, TypeScript types, tests, and the transitional `job.state-changed` event payload already use these values. Renaming would change the wire format for no user benefit.
2. **`pending` is accurate for a host with no queue.** `JobRunner` starts work immediately on the thread pool; there is no FIFO wait. `queued` would promise ordering semantics that do not exist.
3. **It lines up with the event standard.** Events are named `{category}.{past-tense-verb}`; `job.completed` and `install.completed` read naturally and match the state one-to-one (see [`EVENT_BUS.md`](EVENT_BUS.md)).
4. **The UI copy is separate.** The person never sees the raw state; they see "Finished.", the failure card, or "Cancelled.".

Do not reintroduce `queued`, `succeeded`, `done`, `error`, or `canceled` (single *l*) anywhere.

## Lifecycle

1. `JobRunner.Start(moduleId, kind, work)` returns the existing active job for that module and kind if there is one; otherwise it creates a record in `pending` and runs `work` on a thread-pool thread.
2. The record moves to `running`. `IJobLog` on the record receives info/warning/error lines and progress.
3. The work returns `ModuleActionResult`. Success → `completed`; failure → `failed` with its failure card; cancellation or `OperationCanceledException` → `cancelled`; an unexpected exception → `failed` with a generic "Something went wrong." card and a Try again fix.
4. UI reads `GET /api/jobs/{id}` or subscribes to `GET /api/jobs/{id}/events` (SSE).
5. `POST /api/jobs/{id}/cancel` requests cancellation when allowed.
6. A Try again fix (`POST /api/modules/{id}/fixes`) re-runs the same work as a new job.

Job log lines are mirrored into the diagnostic log under category `job:{moduleId}` ([`LOGGING.md`](LOGGING.md)).

## Kinds in use

| Kind | Module | Description |
| --- | --- | --- |
| `install` | Any tool | First install, from `IToolModule.InstallAsync` |
| `upgrade` | Any tool | Update, from `IToolModule.UpgradeAsync` |
| `pull` | Ollama | Model download |
| `restart` | Ollama | LAN listen change |

Kinds are lowercase single words chosen by the module. `install` and `upgrade` are reserved for the host's generic routes; a plugin uses its own kinds for anything else.

## Events

`JobRunner` publishes `job.state-changed` on every transition, and `install.started`, `install.completed`, `install.failed` for `install` / `upgrade` jobs. The per-state `job.created`, `job.started`, `job.completed`, `job.failed`, `job.cancelled` names are reserved. See [`EVENT_BUS.md`](EVENT_BUS.md#job-events).

## Future examples

- Docker image pull: `Start("docker", "pull", ...)`
- Odysseus setup: `Start("odysseus", "install", ...)`
- FlashML token install: `Start("flashml-freetoken", "install", ...)`

Same queue and UI patterns (`JobPanel`, failure cards, retry fixes).
