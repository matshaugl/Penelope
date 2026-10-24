# Tool provider system

Canonical document for provider kinds and the host services handed to plugins. Plugin folder layout, manifest, `hostApi` versioning, and the full `IToolModule` contract are in [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md).

## Purpose

Formalize how Penelope manages external tools (Ollama today; Docker, Odysseus, FlashML FreeToken later) without editing the host for each integration.

"Provider" here means a **tool provider** (`IToolProvider`): a plugin that manages an external program. It is unrelated to `IModelProvider`, which answers chat completions.

## `IToolProvider`

Defined in `Penelope.Abstractions`. Extends `IToolModule` with:

```csharp
string ProviderKind { get; }
```

Use constants from `ToolProviderKinds`:

| Kind | Constant | Status |
| --- | --- | --- |
| Ollama | `ollama` | Implemented (`OllamaModule`) |
| Docker | `docker` | Reserved. Target release 0.2 ([roadmap](ROADMAP.md)). |
| Odysseus | `odysseus` | Reserved. Target release 0.3. |
| FlashML FreeToken | `flashml-freetoken` | Reserved. Target release 0.4. |

A module that does not implement `IToolProvider` is still a valid plugin; its provider kind is reported as its `Id`. The sample plugin is such a module.

Add a constant to `ToolProviderKinds` when a new provider is designed. Adding a constant is a minor contract change ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#what-bumps-which-number)) but does not require plugins to change.

## Registration

1. Create a plugin folder as described in [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-folder).
2. Implement a public class implementing `IToolProvider` (and optionally `IModelProvider`, `IJobAwareModule`, `IInfrastructureModule`, `IAgentToolSource`).
3. Restart Penelope. `PluginCatalog` discovers the folder; `ToolProviderRegistry` lists providers for the host.

No host project reference is required.

## Host services for plugins

Implement `IInfrastructureModule`:

```csharp
void UseInfrastructure(HostInfrastructure infrastructure);
```

`HostInfrastructure` supplies:

| Member | Use for | Document |
| --- | --- | --- |
| `IDownloadManager Downloads` | Installer and payload downloads with retry and checksum | [`DOWNLOAD_MANAGER.md`](DOWNLOAD_MANAGER.md) |
| `IEventBus Events` | Publishing tool and model events | [`EVENT_BUS.md`](EVENT_BUS.md) |
| `IPenelopeDiagnosticLog Diagnostics` | Operational log lines | [`LOGGING.md`](LOGGING.md) |
| `IJobQueue Jobs` | Starting jobs owned by the plugin | [`JOB_QUEUE.md`](JOB_QUEUE.md) |

Called from `ModuleEndpoints` after load, before API routes are mapped. Plugins use these interfaces; they never reference `Penelope.Host` or construct `Penelope.Infrastructure` types themselves.

## Example: future Docker provider

1. New project `Penelope.Modules.Docker` referencing Abstractions + Infrastructure.
2. `DockerModule : IToolProvider` with `ProviderKind => ToolProviderKinds.Docker`.
3. Use `infrastructure.Downloads` for any installer payload.
4. Publish `tool.started` / `tool.stopped` / `tool.error` via `infrastructure.Events` ([event standard](EVENT_BUS.md)).
5. Ship `plugins/docker/` with a manifest declaring `"hostApi": "1.0"` and optional `ui/index.js`.

## API surface

`GET /api/modules` includes `providerKind` for each tool. See [`API.md`](API.md).
