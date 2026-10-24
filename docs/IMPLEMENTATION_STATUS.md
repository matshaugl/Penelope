# Implementation status

The only place that tracks task-level progress. Direction is in [`ROADMAP.md`](ROADMAP.md); requirements and "done when" criteria are in the [product specification](product-specification.md#5-implementation-roadmap).

Last updated: 13 October 2026 — documentation and consistency pass (canonical job states, `hostApi` "1.0", event naming standard, ADRs, doc split).

## Phase status

Phases 0–4 plus the model check make up [release 0.1](ROADMAP.md#release-01--ollama-control-center). Phases 5 and 6 are releases 0.3 and 0.4; release 0.2 (Docker) was added to the roadmap after the phases were numbered.

**Status on 12 October 2026**, from the source rather than from the phase list alone:

| Phase | State |
| --- | --- |
| 0 Shell and plugin host | Done. Loopback session, plugin scan, generic tool page, jobs, and the sample plugin. |
| 1 See and control Ollama | Done. `plugins/ollama` detects, starts, and stops the real Windows app. The host project does not reference it. Spike notes are in `OllamaProcess`. |
| 2 Models | Done. List, catalog, pull, load, unload, and remove. `qwen2.5:0.5b` was downloaded, loaded, and unloaded on this PC. Catalog cards warn when installed memory or video memory is below the model's request. |
| 3 This computer and the network switch | Partial. CPU, memory, graphics, and the models drive are live. NVIDIA utilization comes from `nvidia-smi`. AMD and Intel utilization comes from the GPU Engine counter, with a memory-fill fallback labeled "about". The Network page can restart Ollama onto `0.0.0.0` or back to `127.0.0.1` and asks Windows for one private firewall rule. This PC was left on loopback, so the live firewall round trip is still open. |
| 4 Install, update, and first visit | Partial. The page can download the official installer, check its published checksum, and run the per-user silent install. The first visit is Welcome, then Install or "already installed", then an optional starter model. "Start Penelope when I sign in" writes the current-user Run key and is off until the person turns it on. `scripts\publish.ps1` builds `artifacts\PenelopeSetup.exe`. A forced Ollama install failure and a watched update on a clean account are still open. |
| Model check | Done. Chat streams to the running model. Agent mode can call web search only. Tools use `IAgentToolSource`. The conversation stays in the browser tab. |
| Brand mark | Done. `branding/penelope.svg` is the clean P tile. The program, installer, shortcuts, favicon, sidebar, welcome, loading, empty states, and locked page use it. The status dot stays on the header, tool cards, tool pages, and the tray. |
| Infrastructure refactor | Done. See below. |
| 5 Odysseus (release 0.3) | Not started. |
| 6 FlashML FreeToken (release 0.4) | Not started. |

### Phase notes

**Phase 1.** Verified against Ollama 0.35.1 on this PC, including the already-running case.

**Phase 2.** Done for the model actions. The small starter downloaded with a job log, then Start and Stop changed its in-memory flag. A refresh keeps that job on the page while Penelope is still running. Catalog warnings use the live memory and video-memory readings.

**Phase 3.** CPU, memory, graphics cards, and free space on the models drive are live. NVIDIA utilization comes from `nvidia-smi` on this PC. AMD and Intel utilization is the busier of the summed 3D and Compute GPU Engine counters for that card. Tests cover adding several processes, preferring Compute, ignoring Copy, treating a quiet card as zero, and ignoring an unknown adapter. If the engine counter is missing, the meter uses dedicated-memory fill and says "about". The Network page, the serve restart, and the private firewall rule are implemented. Tests cover turning the switch on, turning it off when the administrator prompt is refused, and the dashboard source staying on `127.0.0.1`. This PC was left on loopback, so that firewall round trip has not been watched live.

**Phase 4.** The Ollama download, checksum, silent install, failure cards, version check, and Update are implemented. The first visit shows three steps and then sets `setupComplete`. Sign-in startup is a checkbox. It writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Penelope` to the quoted `Penelope.Host.exe` and removes that value when turned off. Tests keep the switch in memory so they do not change the signed-in user's registry. Penelope's own installer compiles to `artifacts\PenelopeSetup.exe`. A silent install into a temporary folder opened the dashboard, loaded the Ollama plugin, and did not load the sample plugin. Uninstall removed the program and both shortcuts. A clean Windows account, a forced Ollama install failure, and a watched Ollama update are still open.

**Model check.** The shell page posts to `/api/chat/stream` or `/api/agent/stream`. Ollama implements `IModelProvider`. Web search is an `IAgentToolSource` in the host. A plugin can add tools by implementing that same interface. Tests cover a successful tool round, a refused unknown tool, the three-call stop, streamed chat lines, a model that cannot use tools, search HTML, and a redirect that leaves DuckDuckGo.

## Infrastructure refactor (completed)

- `Penelope.Infrastructure` shared library
- `IToolProvider` / `ToolProviderKinds` and `ToolProviderRegistry`
- `IDownloadManager` with redirect, retry, checksum, progress
- Ollama installer migrated to download manager
- `IInfrastructureModule` + `HostInfrastructure` wiring for plugins
- Canonical job states `pending`, `running`, `completed`, `failed`, `cancelled` (API + UI)
- `IEventBus` with `PenelopeEventNames`; host and Ollama publishers
- `IPenelopeDiagnosticLog` (file + `/api/diagnostics/logs`)
- Diagnostics page in the React shell

## Contract changes in the documentation pass

- `hostApi` is now `"MAJOR.MINOR"` (`"1.0"`). The host still reads the legacy integer `1` as `1.0`. Manifests in `src/` and `plugins/` were migrated. Rules: [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#hostapi-versioning).
- `PenelopeEventNames` defines the full event catalog. New names are reserved (no publisher yet). Standard: [`EVENT_BUS.md`](EVENT_BUS.md).
- Tests: 91 passing (adds six `hostApi` compatibility cases).

## Current state

- **Ollama** — full provider (`IToolProvider`, `IModelProvider`), events on start/stop/model pull
- **Docker / Odysseus / FlashML** — provider-kind constants and docs only; no modules yet
- **Job runner** — single in-memory `JobRunner` in the host (no separate queue process, no persistence)
- **Event bus** — in-memory only (500-event ring buffer). `job.state-changed` is the only job event published; per-state `job.*` names are reserved.
- **Plugin UI** — failed import is caught; errors after mount are not yet contained

## Deviations from the product specification

Deliberate or accepted differences between the spec and the code. Fix the spec or the code before removing a row.

| Spec says | Code does | Status |
| --- | --- | --- |
| Separate `Penelope.Elevation.exe` with `requireAdministrator`, accepting only `firewall-add` / `firewall-remove` | Ollama plugin's `LanFirewall` runs one elevated `cmd.exe /c netsh ...` via `runas` with fixed arguments | Accepted for 0.1. Revisit before code signing / wide distribution ([`SECURITY.md`](SECURITY.md#elevation)). |
| Frontend checks with Vitest for presentational helpers | No Vitest in `web/package.json`; `tsc --noEmit` runs in the build | Open |
| §3 file tree (planned files) | Actual tree differs; documented in [`ARCHITECTURE.md`](ARCHITECTURE.md#source-tree) | Resolved by moving the tree to ARCHITECTURE.md |
| Jobs table listed `queued` / `succeeded` | `pending` / `completed` everywhere | Resolved; spec corrected ([`JOB_QUEUE.md`](JOB_QUEUE.md#why-these-names)) |

## Outstanding tasks

- [ ] Clean-account acceptance: forced Ollama install failure, watched Ollama update (phase 4)
- [ ] Live firewall round trip with the LAN switch (phase 3)
- [ ] Publish per-state `job.*` events from `JobRunner`, then retire `job.state-changed` after one minor `hostApi` version
- [ ] Publish `download.started` / `download.completed` / `download.failed` from `DownloadManager`; publish `model.removed` / `model.loaded` / `model.unloaded` and `tool.error` from the Ollama module
- [ ] Plugin UI error containment and fallback to the generic page ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-safety))
- [ ] Add Vitest or remove it from the spec
- [ ] Stub or scaffold provider modules for Docker, Odysseus, FlashML (optional sample manifests)
- [ ] Persist event bus or job history across host restarts (if product requires)
- [ ] Wire diagnostic log level filter in UI
- [ ] Copy `Penelope.Infrastructure.dll` beside plugins in publish if ever loaded without host ALC sharing (today: shared from host)

## Recommended next milestones

1. **Close release 0.1** — the two clean-account checks and the live firewall round trip.
2. **Event catalog completion** — publish the reserved names; small, unblocks tray and UI subscribers.
3. **Plugin UI safety** — error boundary + generic-page fallback before a second plugin ships a UI bundle.
4. **Docker provider design (release 0.2)** — install detection and service control without CLI in UI.
5. **Job history** — list recent jobs on Diagnostics for beginners troubleshooting installs.
6. **Penelope self-update** — reuse download manager + job queue (still deferred in product spec).
