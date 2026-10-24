# Penelope architecture

Canonical document for layers, dependency rules, the source tree, and ownership. Product behavior is in the [product specification](product-specification.md). Rationale for the major choices is in [`ADR/`](ADR/README.md).

Penelope is a loopback-only Windows host that loads tool plugins from `plugins/`, serves a React dashboard, and runs long work on a shared job runner.

## Layers

| Layer | Project | Role |
| --- | --- | --- |
| Contracts | `Penelope.Abstractions` | Plugin API, jobs, downloads, events, logging interfaces, `HostApi` version |
| Infrastructure | `Penelope.Infrastructure` | Download manager, event bus, diagnostic log (shared by host and plugins) |
| Host | `Penelope.Host` | HTTP API, session, tray, plugin load, job runner, system readings, chat/agent |
| Tools | `Penelope.Modules.*` | Optional plugins (Ollama today) |
| UI | `web/` | React shell + per-plugin Vite bundles |

## Dependency rules

```text
Penelope.Abstractions        (references nothing in the solution; no Windows calls)
      ▲          ▲
      │          │
Penelope.Infrastructure      (references Abstractions only)
      ▲          ▲
      │          └───────────── Penelope.Modules.* / Penelope.Plugins.*
      │                         (reference Abstractions, optionally Infrastructure; never Host; never another module)
Penelope.Host                (references Abstractions + Infrastructure; never a module — enforced by a test)
```

| Rule | Enforced by |
| --- | --- |
| Host does not reference any `Penelope.Modules.*` / `Penelope.Plugins.*` project | `DashboardTests.HostProjectDoesNotReferencePlugins` |
| A module does not reference another module | Review. Cross-tool calls go over HTTP. |
| Plugins share exactly one copy of `Penelope.Abstractions` and `Penelope.Infrastructure` with the host | `PluginLoadContext` resolves those two names from the host's context |
| Plugins use host services only through `HostInfrastructure` interfaces | Review |
| The React shell does not import plugin UI bundles | Separate Vite entry (`vite.ollama.config.ts`); loaded at runtime via `loadModuleUi` |
| Plugin pages talk to the host only over `/api/...` | Review ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#expectations-for-plugin-authors)) |

Tools ship as folders under `plugins/<id>/`.

## Core components

- **Plugin catalog** (`PluginCatalog`) — scans `manifest.json`, checks `hostApi` compatibility, loads assemblies in an isolated `AssemblyLoadContext`. See [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md).
- **Tool provider registry** (`ToolProviderRegistry`) — exposes loaded modules with their `ProviderKind`. See [`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md).
- **Job runner** (`JobRunner`) — background work with canonical states `pending`, `running`, `completed`, `failed`, `cancelled`; streams snapshots to `/api/jobs/{id}/events`. See [`JOB_QUEUE.md`](JOB_QUEUE.md).
- **Download manager** — HTTPS downloads with redirects, retries, checksum verification, progress via `IJobLog`. See [`DOWNLOAD_MANAGER.md`](DOWNLOAD_MANAGER.md).
- **Event bus** — in-process pub/sub using the `{category}.{verb}` naming standard. See [`EVENT_BUS.md`](EVENT_BUS.md).
- **Diagnostic log** — structured log to `%LOCALAPPDATA%\Penelope\logs\penelope.log` and `/api/diagnostics/logs`. See [`LOGGING.md`](LOGGING.md).
- **Session gate** (`SessionGate`) — cookie, origin, and Host checks on every request. See [`SECURITY.md`](SECURITY.md).
- **Chat and agent** (`Agent/`) — Chat streams from the first `IModelProvider`; Agent runs `AgentRunner` with tools from `IAgentToolSource`s.
- **System readings** (`SystemInfo/`) — CPU, memory, GPUs, models disk, shared by every tool.
- **Tray** (`Tray/`) — reopens the dashboard and quits; colored status dot.

## Data flow (install example)

1. UI `POST /api/modules/ollama/install` → `ModuleEndpoints.Begin` → `JobRunner.Start` (job `pending`, `install.started`).
2. Job moves to `running` and calls `OllamaModule.InstallAsync` → `GitHubOllamaSetup` → `IDownloadManager`.
3. Progress lines update the job snapshot; each `IJobLog.Progress` raises `download.progress`.
4. The job ends `completed` (raises `install.completed`) or `failed` with a failure card (raises `install.failed`). `job.state-changed` accompanies every transition.
5. UI follows the SSE job stream, or polls `GET /api/jobs/{id}`.

## Event flow

Plugins receive `HostInfrastructure` via `IInfrastructureModule.UseInfrastructure` (downloads, events, diagnostics, jobs). The host publishes job and install events from `JobRunner`; Ollama publishes `tool.started`, `tool.stopped`, and `model.added`. The full catalog, including reserved names, is in [`EVENT_BUS.md`](EVENT_BUS.md#canonical-event-catalog).

## Source tree

Actual layout. Build output (`bin/`, `obj/`, `wwwroot/`, plugin DLLs and `ui/` bundles, `artifacts/`) is generated and not committed.

```text
Penelope/
  Demo/                              Reference site. Not compiled, not shipped, not committed (absent from a fresh clone).
  branding/penelope.svg              The only logo source
  docs/                              This documentation (start at docs/README.md)
    ADR/                             Architecture decision records
  installer/penelope.iss             Per-user Inno Setup script
  scripts/
    publish.ps1                      Web build, self-contained host, installer
    BrandExport/                     Renders penelope.svg into .ico files at build time
  src/
    Penelope.Abstractions/           Contracts shared with plugins
      IToolModule.cs, IToolProvider.cs, IModelProvider.cs, IAgentTool.cs
      IJobLog.cs, IJobQueue.cs, JobState.cs
      IDownloadManager.cs, IEventBus.cs, IPenelopeDiagnosticLog.cs, IHostInfrastructure.cs
      HostApi.cs                     Host API version + compatibility rules
      FailureCard.cs, InstallPlan.cs, ToolRelease.cs, ToolStatus.cs, IModuleEndpointBuilder.cs
    Penelope.Infrastructure/
      Downloads/DownloadManager.cs
      Events/EventBus.cs
      Logging/PenelopeDiagnosticLog.cs
    Penelope.Host/
      Program.cs                     Kestrel, static files, plugin scan, route mapping
      Agent/                         AgentRunner, ChatStreamer, AgentToolRegistry, WebSearch (DuckDuckGo)
      Api/                           Health, System, Jobs, Modules, Chat, Host, Diagnostics endpoints
      Jobs/                          JobRunner (+ JobRecord, JobSnapshot), HostJobQueue
      Plugins/                       PluginCatalog, PluginLoadContext, PluginRoots, ToolProviderRegistry, ModuleEndpointBuilder
      Security/                      SessionGate, SessionToken
      Settings/                      AppConfigStore, SignInStartup
      SystemInfo/                    MachineReadings, GraphicsCards, SystemSnapshot
      Tray/                          TrayIcon, LogoIcon, LogoTone, BrandMark, Browser
      Assets/penelope.ico            Generated by BrandExport, committed
      wwwroot/                       Vite build output. Generated.
    Penelope.Modules.Ollama/
      OllamaModule.cs                IToolProvider, IModelProvider, IInfrastructureModule, IJobAwareModule
      OllamaApi.cs                   Plugin routes under /api/modules/ollama
      OllamaClient.cs                HTTP only. No UI.
      OllamaProcess.cs               Start, stop, process strategy notes
      OllamaListen.cs                OLLAMA_HOST and serve restart
      LanFirewall.cs                 Elevated netsh for the LAN rule
      OllamaSetup.cs                 GitHub release, checksum, silent install, winget fallback
      OllamaCatalog.cs, catalog.json Curated models and fit
      OllamaChat.cs                  Chat completions for IModelProvider
      manifest.json
    Penelope.Plugins.Sample/         Throwaway plugin used by tests. Not shipped.
    Penelope.Host.Tests/             xUnit tests, including a fake Ollama server
  plugins/                           Runtime layout the host scans. Not a project reference.
    ollama/                          Copied from the Ollama module build plus its UI bundle
    sample/                          Copied from the sample plugin build
  web/
    vite.config.ts                   Shell build → src/Penelope.Host/wwwroot
    vite.ollama.config.ts            Ollama UI build → plugins/ollama/ui
    src/
      main.tsx
      app/                           App, routes, api client, module context, SSE helper
      design/                        tokens.css, global.css, fonts/
      components/                    Sidebar, StatusHeader, Button, Meter, ProgressBar, JobPanel, FailureCard, EmptyState, Logo
      pages/                         Welcome, Home, Chat, System, Tools, Diagnostics, GenericModulePage, PluginPage
      modules/
        types.ts                     Wire types shared by shell and plugin bundles
        loadModuleUi.ts              Dynamic import of a plugin's ui/index.js
        ollama/main.tsx              Separate bundle: Models, Add a model, Network
```

The original plan in the product specification listed some files that were folded into others or not built: `JobModels.cs` and `JobLog.cs` (now inside `JobState.cs` / `JobRunner.cs`), `LoopbackOnly.cs` (inside `SessionGate`), `CpuRamProbe.cs` / `GpuProbe.cs` (now `MachineReadings` / `GraphicsCards`), `OllamaInstaller.cs` / `OllamaRelease.cs` (inside `OllamaSetup.cs`), separate Ollama page components (one `main.tsx`), `AppShell`, `Toggle`, `Disclosure`, `SetupPage` (now `WelcomePage`), and the `Penelope.Elevation` project (replaced by `LanFirewall`; see [`SECURITY.md`](SECURITY.md#elevation)).

## Ownership

| Path | Owns |
| --- | --- |
| `Penelope.Abstractions` | Types every module and the host share. No Windows calls. Changes here follow [`hostApi` rules](PLUGIN_SYSTEM.md#what-bumps-which-number). |
| `Penelope.Infrastructure` | Implementations of shared services. No host or module knowledge. |
| `Penelope.Host` | Browser session, tray, jobs, system readings, static files, plugin scan, chat/agent routing. |
| `Penelope.Modules.Ollama` | Installer, process, releases, Ollama HTTP, LAN listen and firewall rule, catalog fit. Output is copied into `plugins/ollama/`. |
| `web/src/design` | Tokens and chrome. Plugin pages consume these. They do not restyle the shell. |
| `web/src/modules/ollama` | Ollama's custom pages, emitted as a plugin UI bundle. |
| `web/src/pages/GenericModulePage.tsx` | The page every plugin gets even with no custom UI. |
| `plugins/` | What the host loads. Adding a folder here does not change the host. |
| `installer` | The thing a person double-clicks. It ships the host and the plugins that are ready. |

The host project does not reference a module project. A module does not reference another module. Odysseus, when it exists, calls Ollama through Ollama's HTTP port or through the host's module status API. Its source can live in its own folder and its build output is another directory under `plugins/`.

## Build outputs

- `web/npm run build` → `src/Penelope.Host/wwwroot/` and `plugins/ollama/ui/`.
- `dotnet build` → copies module DLLs and manifests into `plugins/<id>/`, runs `BrandExport`.
- `scripts/publish.ps1` → `artifacts/PenelopeSetup.exe`.

Version control rules (what to commit and what is generated) are in the [product specification](product-specification.md#version-control).
