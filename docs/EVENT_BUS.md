# Event bus

Canonical document for event names, the naming standard, payloads, and who publishes what. The code source of truth is `PenelopeEventNames` in `Penelope.Abstractions/IEventBus.cs`.

## Implementation

`Penelope.Infrastructure.Events.EventBus` — in-process, synchronous dispatch to subscribers, ring buffer of recent events (500). Nothing is persisted across host restarts.

## Rationale

- Decouple plugins from host UI refresh logic
- Allow future subscribers (telemetry plugins, tray animations) without changing callers
- Complement job SSE (events are lightweight signals; jobs carry full logs)

Events are notifications, not commands. A subscriber must never be required for correctness; the job record and module status remain the source of truth.

## Naming standard

Format: **`{category}.{verb}`**

| Rule | Example | Not |
| --- | --- | --- |
| Lowercase ASCII, one dot | `model.added` | `Model.Added`, `model_added` |
| Category is a singular noun naming the subject | `download.failed` | `downloads.failed` |
| Verb is past tense for something that happened | `tool.started` | `tool.start`, `tool.starting` |
| `progress` and `error` are the only non-past-tense verbs | `download.progress`, `tool.error` | |
| Multi-word verbs use a hyphen | `job.state-changed` | `job.stateChanged` |
| The verb set mirrors the subject's lifecycle | `job.completed` ⇔ job state `completed` | `job.succeeded` |
| Plugin-specific events are prefixed with the plugin id | `ollama.listen-changed` | `listen.changed` |

Job event verbs match the canonical job states in [`JOB_QUEUE.md`](JOB_QUEUE.md). `install.completed` is named `completed` for the same reason.

### Payload conventions

`PenelopeEvent` fields: `Name`, `Time`, optional `ToolId`, `JobId`, `Completed`, `Total`, `Message`.

- `ToolId` is the plugin id (`ollama`), set on every event that concerns a tool.
- `JobId` is set whenever the event comes from a job.
- `Completed` / `Total` are bytes (or units) for progress events only.
- `Message` is a short human-readable line, or a model tag for `model.*`, or the API job state for `job.state-changed`. Never secrets, tokens, or credential-bearing URLs.

## Canonical event catalog

**Published** means a publisher exists today. **Reserved** means the name is defined in `PenelopeEventNames` and is the only acceptable name for that signal, but nothing publishes it yet.

### Tool events

| Name | When | Publisher | Status |
| --- | --- | --- | --- |
| `tool.started` | Tool process/API became available | Ollama module | Published |
| `tool.stopped` | Tool stopped | Ollama module | Published |
| `tool.error` | Tool failed to start, crashed, or became unreachable unexpectedly | Tool modules | Reserved |

### Job events

| Name | When | Publisher | Status |
| --- | --- | --- | --- |
| `job.created` | Job record created in state `pending` | `JobRunner` | Reserved |
| `job.started` | Job moved to `running` | `JobRunner` | Reserved |
| `job.completed` | Job ended in `completed` | `JobRunner` | Reserved |
| `job.failed` | Job ended in `failed` | `JobRunner` | Reserved |
| `job.cancelled` | Job ended in `cancelled` | `JobRunner` | Reserved |
| `job.state-changed` | Any job transition; `Message` = API state value | `JobRunner` | Published (transitional) |

`job.state-changed` predates this standard. When `JobRunner` starts publishing the five per-state events, keep publishing `job.state-changed` for one minor `hostApi` version, then remove it.

### Install events

Published for jobs of kind `install` and `upgrade`, in addition to job events.

| Name | When | Publisher | Status |
| --- | --- | --- | --- |
| `install.started` | Install/upgrade job created | `JobRunner` | Published |
| `install.completed` | Install/upgrade job ended in `completed` | `JobRunner` | Published |
| `install.failed` | Install/upgrade job ended in `failed` | `JobRunner` | Published |

### Download events

| Name | When | Publisher | Status |
| --- | --- | --- | --- |
| `download.started` | A file download began | `DownloadManager` | Reserved |
| `download.progress` | Job progress update (bytes) | `JobRecord` (every `IJobLog.Progress` call, including Ollama model pulls) | Published |
| `download.completed` | A file download finished and passed size/checksum checks | `DownloadManager` | Reserved |
| `download.failed` | A file download gave up after retries or failed verification | `DownloadManager` | Reserved |

`download.progress` is currently raised for any job progress, not only `IDownloadManager` transfers. Treat it as "job byte progress" until download start/finish events exist.

### Model events

| Name | When | Publisher | Status |
| --- | --- | --- | --- |
| `model.added` | Model pull finished; `Message` = tag | Ollama module | Published |
| `model.removed` | Model weights deleted from disk | Model-managing modules | Reserved |
| `model.loaded` | Model loaded into memory | Model-managing modules | Reserved |
| `model.unloaded` | Model unloaded from memory | Model-managing modules | Reserved |

## Guidelines for future modules

1. Use an existing `PenelopeEventNames` constant when one fits. Do not publish string literals.
2. Reuse the shared categories (`tool`, `job`, `install`, `download`, `model`) for shared concepts so subscribers work across tools. A Docker image pull is `download.*`, a Docker container start is `tool.started` only if it is the tool itself.
3. For a concept only your plugin has, prefix with your plugin id: `docker.image-pruned`. Keep those names as constants in your plugin, documented in your plugin's README.
4. A new shared category or verb is a contract change: add the constant to `PenelopeEventNames`, add a row here, and bump `hostApi` minor ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#what-bumps-which-number)).
5. Do not publish from inside a lock; dispatch is synchronous.
6. Plugins publish via `HostInfrastructure.Events`, not by referencing `EventBus` directly.

## API

- `GET /api/diagnostics/events?limit=100` — recent events for debugging. See [`API.md`](API.md).

## Subscribing in code

```csharp
using var sub = eventBus.Subscribe(PenelopeEventNames.ToolStarted, evt => { ... });
```
