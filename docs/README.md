# Penelope documentation

Start here. This page tells a new contributor (human or AI agent) what to read, which document owns which topic, and which rules must not be broken.

## Reading order for a new session

1. [`product-specification.md`](product-specification.md) — what Penelope does and for whom (Decisions table first).
2. [`ARCHITECTURE.md`](ARCHITECTURE.md) — layers, boundaries, source tree, ownership.
3. [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) — what is built, what is open, known deviations from the spec.
4. The subsystem document for the area you are changing (table below).
5. [`ADR/`](ADR/README.md) — why the big decisions were made. Read before proposing to reverse one.

## Document ownership

Each topic has exactly one canonical document. Other documents link to it instead of restating it. When two documents disagree, the canonical one wins and the other is a bug.

| Topic | Canonical document | Canonical code |
| --- | --- | --- |
| Product behavior, requirements, acceptance, vocabulary | [`product-specification.md`](product-specification.md) | — |
| Layers, dependency rules, source tree, ownership | [`ARCHITECTURE.md`](ARCHITECTURE.md) | `*.csproj` references |
| HTTP routes and wire formats | [`API.md`](API.md) | `src/Penelope.Host/Api/`, `OllamaApi.cs` |
| Plugin manifest, `hostApi` versioning, module contract, plugin UI | [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md) | `IToolModule.cs`, `HostApi.cs`, `PluginCatalog.cs` |
| Provider kinds, host services for plugins | [`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md) | `IToolProvider.cs`, `IHostInfrastructure.cs` |
| Job states, kinds, lifecycle | [`JOB_QUEUE.md`](JOB_QUEUE.md) | `JobState.cs`, `JobRunner.cs` |
| Event names and naming standard | [`EVENT_BUS.md`](EVENT_BUS.md) | `PenelopeEventNames` in `IEventBus.cs` |
| Downloads, retries, checksums | [`DOWNLOAD_MANAGER.md`](DOWNLOAD_MANAGER.md) | `IDownloadManager.cs`, `DownloadManager.cs` |
| Diagnostic log and log levels | [`LOGGING.md`](LOGGING.md) | `IPenelopeDiagnosticLog.cs` |
| Session, loopback, LAN exposure, plugin trust | [`SECURITY.md`](SECURITY.md) | `Security/`, `LanFirewall.cs` |
| Screens, copy, design tokens, brand mark | [`UI_SPEC.md`](UI_SPEC.md) | `web/src/design/` |
| Release direction | [`ROADMAP.md`](ROADMAP.md) | — |
| Task-level progress | [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) | — |
| Rationale for major decisions | [`ADR/`](ADR/README.md) | — |

Names that appear on the wire (job states, event names, fix kinds, provider kinds, `hostApi`) are defined in code first. Documentation must quote the code constant, never invent a synonym.

## Non-negotiable rules

Breaking one of these is a design change and needs a new ADR, not a code review comment.

1. The dashboard binds `127.0.0.1` only. See [ADR-002](ADR/ADR-002-Loopback-Only-Dashboard.md).
2. No screen, error, or fix asks the person to open a terminal or type a command.
3. `Penelope.Host` never references a `Penelope.Modules.*` or `Penelope.Plugins.*` project. A test enforces this. See [ADR-001](ADR/ADR-001-Plugin-System.md).
4. A module never references another module. Cross-tool calls go over HTTP.
5. Chat is a model check. The full chat product is Odysseus. See [ADR-005](ADR/ADR-005-Local-AI-Control-Center.md).
6. Long work runs as a job with progress and a live log. Failures end in a failure card whose fixes are buttons.
7. Penelope does not install GPU drivers and does not update external tools without the person pressing Update.

## Glossary of state words

Penelope has three separate state vocabularies. They are not interchangeable.

| Vocabulary | Values | Describes | Defined in |
| --- | --- | --- | --- |
| Job state | `pending`, `running`, `completed`, `failed`, `cancelled` | One unit of long work (install, upgrade, pull, restart) | `JobState` / [`JOB_QUEUE.md`](JOB_QUEUE.md) |
| Tool run state | `not-installed`, `installing`, `stopped`, `starting`, `running`, `stopping` | Whether an external tool's server is up | `ToolRunState` / [product spec Vocabulary](product-specification.md#vocabulary) |
| Tool availability | `ready`, `planned`, `missing`, `error` | Whether the plugin itself loaded and can act | `ToolAvailability` / [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md) |

"Running" appears in both job state and tool run state. In prose, say "the job is running" or "Ollama is running", never bare "running".

Other terms:

| Term | Meaning |
| --- | --- |
| Host | `Penelope.Host.exe`: Kestrel, session, jobs, readings, tray, plugin loader |
| Shell | The React app in `web/src/` excluding plugin bundles |
| Plugin | A folder under `plugins/<id>/` with `manifest.json` and an assembly |
| Module | The `IToolModule` implementation inside a plugin assembly. One per plugin. |
| Provider | A module that also implements `IToolProvider` and declares a `ProviderKind` |
| Model provider | A module that implements `IModelProvider` (answers chat). Unrelated to `IToolProvider`. |
| Terminal job state | `completed`, `failed`, or `cancelled`. No further transitions. |
| Active job state | `pending` or `running` |

## Keeping docs honest

- When you change a contract, update its canonical document in the same change.
- Record task progress only in [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md). Record direction only in [`ROADMAP.md`](ROADMAP.md).
- When the code deliberately departs from the product spec, add a row to "Deviations from the product specification" in [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md).
