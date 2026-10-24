# Logging

Canonical document for log destinations, levels, and categories. Plugins receive the diagnostic log as `HostInfrastructure.Diagnostics` ([`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md#host-services-for-plugins)).

## Three kinds of record

| Record | Audience | Where it shows | Interface |
| --- | --- | --- | --- |
| Job log | The person using Penelope | On the page, under the job's progress bar | `IJobLog` |
| Diagnostic log | Troubleshooting (person or developer) | Diagnostics page and `penelope.log` | `IPenelopeDiagnosticLog` |
| Host internals | Developers | Console / `ILogger` providers | `ILogger<T>` |

Events on the event bus are signals, not logs. See [`EVENT_BUS.md`](EVENT_BUS.md).

## Diagnostic log (Penelope)

Implementation: `Penelope.Infrastructure.Logging.PenelopeDiagnosticLog`

### Levels

`PenelopeLogLevel`: `Debug`, `Info`, `Warning`, `Error`

Job log lines use their own lowercase levels, `info`, `warning`, `error` (no debug), because they are shown to the person.

### Destinations

| Destination | Path / API |
| --- | --- |
| File | `%LOCALAPPDATA%\Penelope\logs\penelope.log` (append) |
| UI | Dashboard **Diagnostics** page → `GET /api/diagnostics/logs` |

### Categories

Lowercase. A colon separates a category from its subject.

| Category | Written by |
| --- | --- |
| `jobs` | `JobRunner` (job start, unexpected failure) |
| `job:{moduleId}` | Every job log line, mirrored from `IJobLog` |
| `{pluginId}` (e.g. `ollama`) | The plugin itself (Ollama logs tool events here) |
| `download` | Reserved for the download manager. It currently reports only through the calling job's `IJobLog`, which is mirrored as `job:{moduleId}`. |
| `ui:{pluginId}` | Reserved for plugin UI crash reports ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-safety)) |

### Microsoft.Extensions.Logging

ASP.NET Core and `ILogger<T>` remain for host internals (plugin load errors, unhandled job exceptions).

## Standards

- User-facing install/download steps go to `IJobLog` (shown on the page).
- Operational detail and cross-cutting errors go to `IPenelopeDiagnosticLog`.
- Do not log secrets, session tokens, or full URLs with credentials.

## Troubleshooting

1. Open **Diagnostics** in the sidebar.
2. Inspect `penelope.log` on disk for the full session.
3. Correlate with job id from the failing card and `/api/jobs/{id}`.
