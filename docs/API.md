# Dashboard API

Canonical reference for HTTP routes and wire formats. Behavior behind each route is defined in the [product specification](product-specification.md). Security rules applied to every route are in [`SECURITY.md`](SECURITY.md).

## Conventions

All routes are under the dashboard origin `http://127.0.0.1:<port>` (default port `47321`). Model names travel in JSON bodies, because tags contain colons.

The host exposes generic module operations only. Tool-specific routes are registered by the plugin through `IToolModule.ContributeApi` and mounted under `/api/modules/{id}/`. Adding Ollama's model routes does not add a branch inside the host.

| Rule | Detail |
| --- | --- |
| Session | Every `/api/*` request needs the `penelope_session` cookie, or it gets `401`. |
| Origin | `POST`, `PUT`, `PATCH`, `DELETE` under `/api/*` also need `Origin` equal to the dashboard origin, or they get `403`. |
| Host header | Requests whose `Host` is not `127.0.0.1` get `400`. |
| CORS | No `Access-Control-Allow-Origin` is ever sent. |
| JSON | camelCase property names. Enums are lowercase strings (`"pending"`, `"ready"`). |
| Failures | An action that cannot run returns `409` with `{ "failure": FailureCard }`. |
| Unknown module | `404` |
| SSE | `text/event-stream`, one `event:` name per stream, JSON in `data:` |

## Shell and session

| Method | Path | Role |
| --- | --- | --- |
| GET | `/session?token=...` | One-time link from the tray/console. Sets the session cookie and redirects to `/`. |
| GET | `/` and other non-API paths | The React shell. Without a session, a locked page that says to open Penelope from its shortcut. |
| GET | `/plugins/{id}/ui/{file}` | A plugin's UI bundle file, served as `text/javascript` for `.js`. Path must stay inside the plugin's `ui` folder. |

## Host routes

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/health` | Host is up. `{ "ok": true }` |
| GET | `/api/system` | One sample of CPU, RAM, GPUs, models disk |
| GET | `/api/system/stream` | Server-sent samples |
| GET | `/api/settings` | `theme`, `setupComplete`, `openBrowserOnLaunch`, `startOnSignIn`, `exposeOllamaOnLan`, `ollamaPort` |
| PUT | `/api/settings` | Body `{ "theme"?, "setupComplete"?, "startOnSignIn"? }` |
| POST | `/api/host/quit` | Stop the host. External tools keep running. |
| POST | `/api/host/open-firewall` | Open Windows Firewall settings |
| POST | `/api/host/open-url` | Body `{ "url" }`. Only the known GPU-vendor driver pages are allowed. |
| POST | `/api/host/open-disk` | Open the models folder in Explorer |
| GET | `/api/diagnostics/logs?limit=200` | Recent diagnostic log entries ([`LOGGING.md`](LOGGING.md)) |
| GET | `/api/diagnostics/events?limit=100` | Recent event bus events ([`EVENT_BUS.md`](EVENT_BUS.md)) |

## Modules

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/modules` | Discovered plugins (list of module descriptions, without `plan`) |
| GET | `/api/modules/{id}` | One module description including `plan` |
| POST | `/api/modules/{id}/install` | Begin install. `202 { "jobId" }`, or `200 { "jobId" }` if one is already active. |
| POST | `/api/modules/{id}/upgrade` | Begin update. Same responses as install. |
| POST | `/api/modules/{id}/check` | Refresh the latest-version lookup. `{ "release" }` |
| POST | `/api/modules/{id}/start` | Start the tool. `{ "status", "release" }` or `409 { "failure" }` |
| POST | `/api/modules/{id}/stop` | Stop the tool. Same responses as start. |
| POST | `/api/modules/{id}/fixes` | Body `{ "jobId", "fixId" }`. Runs a fix from that job's failure card. |

A plugin that loads but is not ready returns `"availability": "planned"` and the host rejects install, update, start, and stop with a failure card. A plugin that failed to load (bad manifest, incompatible `hostApi`, missing assembly) is listed with `"availability": "error"` and `loadError` set.

### Module description

```json
{
  "id": "ollama",
  "providerKind": "ollama",
  "name": "Ollama",
  "summary": "The local model server. Penelope starts and stops it on this computer.",
  "availability": "ready",
  "loadError": null,
  "hasCustomUi": true,
  "nav": [{ "label": "Models", "path": "/ollama/models" }],
  "status": { "state": "running", "headline": "Ollama is running" },
  "release": { "installedVersion": "0.12.1", "latestVersion": "0.12.3", "checkedAt": "...", "updateAvailable": true, "checkError": null },
  "plan": null,
  "activeJobId": null,
  "latestJobId": "a1b2..."
}
```

| Field | Values |
| --- | --- |
| `availability` | `ready`, `planned`, `missing`, `error` |
| `status.state` | Tool run state: `not-installed`, `installing`, `stopped`, `starting`, `running`, `stopping` |
| `providerKind` | `ToolProviderKinds` value, or the module id when the module is not an `IToolProvider` |

## Jobs

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/jobs/{id}` | Job snapshot, including the log so far |
| GET | `/api/jobs/{id}/events` | SSE, `event: job`, each `data:` a full snapshot |
| POST | `/api/jobs/{id}/cancel` | `202 { "cancelled": true }` or `409 { "cancelled": false }` when not cancellable |

### Job snapshot

```json
{
  "id": "a1b2...",
  "moduleId": "ollama",
  "kind": "pull",
  "state": "running",
  "message": "Downloading",
  "completed": 104857600,
  "total": 397821319,
  "cancelAllowed": true,
  "startedAt": "2026-10-13T18:00:00Z",
  "log": [{ "time": "...", "level": "info", "text": "Pulling manifest" }],
  "failure": null
}
```

| Field | Values |
| --- | --- |
| `state` | `pending`, `running`, `completed`, `failed`, `cancelled` — canonical, see [`JOB_QUEUE.md`](JOB_QUEUE.md) |
| `kind` | `install`, `upgrade`, `pull`, `restart` (modules may add kinds) |
| `log[].level` | `info`, `warning`, `error` |
| `failure` | `FailureCard` when `state` is `failed`, else `null` |

### Failure card

```json
{
  "summary": "Ollama didn't install.",
  "detail": "Windows asked for permission and the request was cancelled.",
  "fixes": [{ "id": "retry", "label": "Try again", "kind": "retry", "url": null }]
}
```

`fixes[].kind` is one of `retry`, `check-again`, `open-url`, `open-firewall`, `open-disk`. `open-url` only opens `https://` addresses. The payload never contains a command line. See the [product specification](product-specification.md#when-an-install-or-update-fails).

## Chat and agent

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/chat/session` | Provider, active model, installed models, and registered agent tool names |
| POST | `/api/chat/stream` | Body `{ "model", "messages" }`. Streams a chat reply. No tools. |
| POST | `/api/agent/stream` | Same body. The agent may call registered tools, then streams the answer. |

## Ollama plugin routes

The Ollama plugin contributes these, and another plugin would contribute its own under `/api/modules/{its-id}/`:

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/modules/ollama/models` | Installed models and which are in memory |
| GET | `/api/modules/ollama/catalog` | Curated entries annotated with fit |
| POST | `/api/modules/ollama/models/pull` | Body `{ "name" }`, returns a job |
| POST | `/api/modules/ollama/models/load` | Body `{ "name" }` |
| POST | `/api/modules/ollama/models/unload` | Body `{ "name" }` |
| POST | `/api/modules/ollama/models/remove` | Body `{ "name" }`, deletes weights |
| GET | `/api/modules/ollama/network` | Current mode |
| PUT | `/api/modules/ollama/network` | Body `{ "exposeOnLan": true }` |

## Versioning

The HTTP API is internal to one Penelope build: the shell and the host ship together, so routes are not versioned. Plugin UI bundles that call host routes rely on the same `hostApi` contract as their assembly ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#hostapi-versioning)); removing or changing a host route that plugin pages use is a major `hostApi` change.
