# Penelope

Penelope is a Windows application for people who want local AI models and do not want a command line. A shortcut opens a dashboard in the default browser, and every action is on that page. Ollama ships as a plugin. Odysseus and FlashML FreeToken are further plugins. Adding one does not change the Penelope host. The dashboard itself always stays on this computer.

The visual language comes from the Odysseus Chat page saved in `Demo/`. That folder is a reference. Penelope does not ship its scripts.

This document owns product behavior, requirements, vocabulary, and acceptance. Detailed topics live in their own documents and are summarized here with a link: [API](API.md), [plugin contract](PLUGIN_SYSTEM.md), [security](SECURITY.md), [UI and design](UI_SPEC.md), [architecture and file tree](ARCHITECTURE.md), [progress](IMPLEMENTATION_STATUS.md), [direction](ROADMAP.md), and [decision records](ADR/README.md). The documentation index is [`docs/README.md`](README.md).

## Decisions

| Topic | Decision |
| --- | --- |
| Product name | Penelope |
| Who it is for | Beginners installing and running local models on their own Windows PC |
| How it is used | The browser page is the only control surface. A tray icon reopens that page. |
| Terminal | No screen, error, or fix asks the person to open one |
| Dashboard address | `127.0.0.1` only |
| LAN switch | Changes the Ollama listen address. It does not publish the Penelope dashboard |
| Modules | Discovered from a plugins folder. The host does not reference a tool project. |
| Chat | A short page checks that a local model answers. Odysseus remains the full chat product. |
| Agent | Separate from Chat. Tools register through `IAgentToolSource`. The first tool is web search. |
| First plugin | Ollama, including version display and Update |
| Later plugins | Docker, Odysseus, FlashML FreeToken, in that order ([roadmap](ROADMAP.md)) |
| Host | .NET 10, self-contained |
| Interface | React, TypeScript, Vite, CSS variables taken from the demo |
| Model Start and Stop | Load the model into memory, and unload it |
| GPU drivers | Detected and explained. A button opens the vendor’s page. Penelope does not install drivers. |
| Long jobs | Progress plus a live log, on the page |
| Install failure | A plain-language cause and one or more buttons that recover |
| Release cut | Phases 0–4 plus the model check, released as 0.1 ([roadmap](ROADMAP.md)) |
| Plugin contract version | `"hostApi": "1.0"`, `MAJOR.MINOR` ([plugin system](PLUGIN_SYSTEM.md#hostapi-versioning)) |
| Job states | `pending`, `running`, `completed`, `failed`, `cancelled` ([job queue](JOB_QUEUE.md)) |

---

## 1. Product specification

### Purpose

Penelope installs Ollama when it can, shows whether it is running, starts and stops it, lists models, downloads models, loads and unloads them, shows CPU, RAM, GPU, and VRAM, and can open Ollama’s API to the local network.

A person who never opens a terminal should be able to install Penelope, reach a running Ollama, download one recommended model, load it, watch memory move, turn network access on and off, and update Ollama when a newer release exists.

### People and context

The primary person is at a personal Windows PC and is comfortable with a browser. They will abandon the product if it asks them to edit PATH, set an environment variable, or type a command. They may have a NVIDIA, AMD, or Intel GPU, or no useful GPU at all.

A second person writes a plugin. They add a folder. They do not edit the host, the shell, or the installer.

### v1 requirements

| Requirement | Product behavior |
| --- | --- |
| Install Ollama and dependencies when possible | Home offers one action, Install Ollama. Penelope downloads the official Windows installer and runs it. An existing install is reused. |
| Web dashboard | The host serves a single-page app. Every action in this table is a control on that page. |
| No terminal | Fixes are buttons or links that open a Windows setting or a vendor page. Penelope may run installers itself. The page never shows a command to type. |
| Start and stop Ollama | Buttons on the page. Quitting Penelope leaves Ollama running. Quit Penelope is also a button on the page. |
| Service status | Every page has the same status line: Not installed, Installing, Stopped, Starting, Running, or Stopping. |
| Installed models | Models page lists name, friendly catalog name when we have one, size, and whether it is in memory. |
| Download models | Add a model offers a short curated list, plus a way to type any Ollama tag. |
| Load, unload, start, and stop models | On a model row, Start loads it and keeps it loaded. Stop unloads it and frees memory. |
| System information | Home shows CPU, RAM, and GPU at a glance. This computer shows the same readings with names and totals. |
| Local API, optional `0.0.0.0` | Network page. Off means Ollama listens on `127.0.0.1`. On means `0.0.0.0`, after a plain-language confirmation. |
| Progress and logs | Install, update, download, and restart show a progress bar and a live log on the page. The log remains after success or failure. |
| Actionable install failures | A failed install names what went wrong and offers buttons such as Try again or Open the download page. |
| Versions and updates | Each external tool shows the installed version and the latest version. Update runs from the page, with the same progress and failure treatment as install. |
| New modules | A plugin folder dropped beside Penelope appears in Tools. The host project does not change. |

### Out of scope for v1

Chat, prompts, documents, and agents belong to Odysseus. Penelope does not embed them.

Also deferred: Penelope updating itself, a packaged winget feed, Linux and macOS, Windows on ARM, multi-user servers, accounts, telemetry, and automatic GPU-driver installation. External tools are updated from the page. Penelope does not upgrade them silently in the background.

### Vocabulary

The interface uses the same words everywhere.

| Word | Meaning |
| --- | --- |
| Penelope | This application and its dashboard |
| Ollama | The local model server Penelope manages |
| Running | The Ollama server is accepting API calls |
| Stopped | Ollama is installed and the server is not running |
| Model on disk | The weights are downloaded |
| Model in memory | The weights are loaded and using RAM or VRAM |
| Start, on a model | Load it and keep it loaded |
| Stop, on a model | Unload it |
| Only this computer | Ollama listens on `127.0.0.1` |
| Other devices on my network | Ollama listens on `0.0.0.0` |

Ollama does not run each model as its own Windows service. Start and Stop on a model are memory operations. The server has a separate Start and Stop in the page header.

"Running" and "Stopped" above describe a tool. A job has its own states (`pending`, `running`, `completed`, `failed`, `cancelled`), which the person never sees as words. The [glossary](README.md#glossary-of-state-words) separates the two.

### Ollama behavior Penelope depends on

These are the contracts the Ollama module is written against. They stay inside that module so a future Ollama change does not leak into the shell.

| Need | Mechanism |
| --- | --- |
| Is the server up? | `GET /api/version` on the configured port, default `11434` |
| Which models are installed? | `GET /api/tags` |
| Which models are loaded? | `GET /api/ps` |
| Download | `POST /api/pull` with a streamed progress body |
| Load and keep | `POST /api/generate` with an empty prompt and `keep_alive` of `-1` |
| Unload | The same call with `keep_alive` of `0`. The module may also invoke the Ollama program itself. The person never sees that call. |
| Remove from disk | `POST /api/delete` |
| Listen address | User environment variable `OLLAMA_HOST`, then a restart of the server |
| Where weights live | `OLLAMA_MODELS`, otherwise `%USERPROFILE%\.ollama\models` |

Windows Ollama is a tray application (`ollama app.exe`) that supervises `ollama.exe`, not a Windows service. Phase 1 confirms the current process names against a real installer and records the result in the Ollama process controller. The intended start order is:

1. If `/api/version` already answers, the server is running.
2. Otherwise start the official `ollama app.exe` when that file exists.
3. Wait up to 30 seconds for `/api/version`.
4. When a listen-address change is pending, set the user environment variable, stop the current server, and start it again so the new value is actually used. If the tray app ignores the variable, the module starts `ollama.exe serve` itself with the variable set, after stopping the tray app so two servers cannot bind the same port.

Stop unloads loaded models on a best-effort basis, then closes the Ollama processes Penelope started or that are listening on the Ollama port. It does not terminate unrelated processes.

### Installation

Detection order:

1. `GET /api/version` succeeds, so Ollama is already running.
2. `%LOCALAPPDATA%\Programs\Ollama\ollama.exe`, or another copy found on the user PATH.
3. The uninstall entry in the current user’s registry.

If any of those hit, Penelope offers Start, and does not download a second copy.

If none hit, Install Ollama downloads the official Windows setup from the Ollama GitHub release, checks the published checksum when one is published, and runs the per-user silent install. The checksum comparison accepts the current `sha256sum.txt` layout, where the installer is named `./OllamaSetup.exe`, and also a bare name, a `*` prefix, or a BSD `SHA256 (file) = hash` line. The GitHub asset `digest` for that same file is the other published value. When both exist they have to agree. A download that stops early is retried. A checksum mismatch keeps the file, and the page shows the expected checksum and the calculated checksum. The job log records the download URL, the final redirected URL, both checksums, the file size, and the HTTP status codes. Penelope tells the person, before the installer starts, that Windows may ask for permission. If the current installer requires an administrator and the person declines, the install job fails on the page with Try again and Open the Ollama download page. Look again re-runs detection after they have run that installer themselves.

The Ollama installer is the dependency bundle Penelope relies on. If the direct download fails and winget is present, Penelope may run winget itself and stream that output into the job log. The page does not ask the person to type the winget command.

Penelope also checks for a GPU driver:

- NVIDIA, and `nvidia-smi` works: GPU acceleration is available.
- A GPU is present and no usable driver check succeeds: the dashboard says models will use system memory, and a button opens the vendor’s driver page. Penelope does not install drivers. After the driver is installed, Look again refreshes the reading.
- No discrete GPU: the dashboard says so, and the catalog prefers smaller models.

`winget install Ollama.Ollama` is a fallback only when the direct download fails and winget is present.

The download lives in the Ollama plugin (`OllamaSetup`). The host still has no Ollama-specific branch. A published `sha256sum.txt` has to agree with the GitHub asset digest for `OllamaSetup.exe`. A mismatch keeps the downloaded file and does not run it. The download can be cancelled. Once the installer has started, the job cannot be cancelled. If the download cannot be reached and `winget.exe` is present for this user, Penelope asks Windows to install `Ollama.Ollama` and writes that program's output into the log. The page does not show the command. Install leaves an existing copy alone. Update is the only action that downloads over an existing copy, and only after the page has confirmed a newer release.

Penelope’s own setup is `artifacts\PenelopeSetup.exe`, built by `scripts\publish.ps1`. It is a per-user Inno Setup installer. It does not ask for an administrator. The setup file, the installed program, Add/Remove Programs, and the Start menu and desktop shortcuts use the Penelope mark from `branding\penelope.svg`. It puts a self-contained `Penelope.Host.exe` in `%LOCALAPPDATA%\Programs\Penelope`, with `wwwroot` and `plugins\ollama` beside that exe, and adds a Start menu shortcut and a desktop shortcut. The sample plugin is not included. Uninstall removes the program and the shortcuts. It leaves `%LOCALAPPDATA%\Penelope` (settings and logs) and it leaves Ollama. The installer does not turn on “Start Penelope when I sign in”. A published single-file host unpacks its runtime into a temporary folder, so plugin loading uses the directory of `Penelope.Host.exe` before that unpack folder.

### Network exposure

Default is `OLLAMA_HOST=127.0.0.1:11434`.

Turning on “Other devices on my network”:

1. The page explains that other devices will be able to use the models, that Ollama has no password, and that the switch should go back off afterwards.
2. The person confirms.
3. Penelope sets the user environment variable to `0.0.0.0:11434` and restarts Ollama.
4. One elevated request adds one Windows Firewall inbound rule: TCP 11434, private profile only, scoped to that `ollama.exe`. The rule name is `Penelope Ollama LAN`.
5. The page shows `http://<this-pc>:<port>` and a copy button.

Turning it off deletes that firewall rule, sets `127.0.0.1`, and restarts Ollama. If the person refuses the administrator prompt, Penelope still keeps the listen address on localhost when the switch is off. When the switch is on and the firewall step is refused, the page says other devices may be blocked by Windows Firewall and offers Try again and Open Windows Firewall. Penelope launches that settings window. It does not print a `netsh` command for the person to run.

The Penelope dashboard never binds `0.0.0.0`.

A listen-address change stops the Ollama tray and starts `ollama.exe serve` with `OLLAMA_HOST` set on that process. The tray's own “Expose Ollama to the network” setting can override the user variable, so Penelope does not leave the tray as the parent of that server. Turning the switch off starts `ollama.exe serve` on `127.0.0.1` even if Windows Firewall declines the prompt. Turning it on, when the prompt is declined, leaves Ollama reachable on the network and says other devices may still be blocked. The firewall rule is added or removed by one elevated `netsh` request. The page never shows that command.

### Security of the dashboard

The host listens on `127.0.0.1` only, on port `47321` or one of the next 20. A per-launch session token from the tray link becomes an `HttpOnly`, `SameSite=Strict` cookie, and state-changing requests must also carry the dashboard's own `Origin`, so a page on the public web cannot drive the dashboard. The full model, including the Host-header check, elevation, download integrity, and plugin trust, is in [`SECURITY.md`](SECURITY.md#security-of-the-dashboard).

### System readings

Readings are measured on the host and sent to the page. The page does not invent numbers.

| Reading | Source |
| --- | --- |
| CPU | Windows processor time |
| RAM | `GlobalMemoryStatusEx` — used, total, percent |
| GPU utilization | `nvidia-smi` for NVIDIA. AMD and Intel use the Windows GPU Engine counter: the busier of summed 3D and summed Compute for that card. Copy and video engines are ignored. If the counter is missing, the meter falls back to dedicated-memory use and the line says “about”. |
| NVIDIA VRAM | `nvidia-smi` when it exists: used, total, GPU name, utilization |
| Other VRAM | DXGI dedicated memory, plus the Windows dedicated-usage counter. The memory line says “about” only when engine utilization could not be read. |
| Models drive | Free and total space on the volume that holds the models folder |

Penelope reads graphics cards in the host, in `GraphicsCards`, so every tool shares the same meters. NVIDIA numbers come from `nvidia-smi` when that program runs. Other cards use DXGI for the name and dedicated memory. Their utilization comes from the Windows GPU Engine counter for that adapter’s LUID. Penelope adds every process on the 3D engines, adds every process on the Compute engines, and shows the busier of those two, capped at 100. A local model often sits on Compute, so a 3D-only meter would stay near zero while the card is working. Copy and video-decode engines are left out so a file copy does not look like the model is busy. The first counter sample is discarded; the next sample, about 200 milliseconds later, is the number shown. If that counter is missing, the meter falls back to how full dedicated memory is and the line says “about”. A card Penelope can see but cannot measure stays Unavailable, and the meter can open that vendor’s driver page. Penelope does not install drivers. The status dot on the logo is a separate signal: it describes whether a tool is running, working, or stopped, not how busy the graphics card is.

When a source is missing, the card says “Unavailable” and why. Home and This computer both follow the server-sent stream while the page is open.

### Model catalog

A curated catalog ships with Penelope so the first screen is a handful of named choices with a size and a one-sentence description. Each entry has an Ollama tag, a size, a minimum comfortable RAM, a minimum comfortable VRAM, and a role such as `starter-small` or `starter-standard`.

The host compares the catalog with the live readings:

- Enough VRAM or a large amount of RAM: suggest the standard starter.
- Modest RAM: suggest the small starter.
- Very little RAM: suggest the smallest entry and say the computer may feel slow.

The tag is visible in secondary text. An “Other model” field accepts any `name:tag` and shows size only after Ollama reports it. Penelope checks free space on the models volume before a download and refuses to start one when the file clearly will not fit.

One download at a time. Progress comes from Ollama’s pull stream and survives a browser refresh, because the job lives in the host. Closing Penelope stops the job. The page says that before the download starts.

### Model check

Chat is a shell page for checking that a model answers. It is not Odysseus and it does not grow into a general chat product. The person picks Chat or Agent, sees the active model, and sends a short prompt. Replies stream when the provider streams. The conversation stays in the browser tab (`sessionStorage`). The server does not store it. Refreshing the tab keeps it. Closing the tab clears it.

Chat and Agent are different paths. `POST /api/chat/stream` never sends tools to the model. `POST /api/agent/stream` is the only path that runs `AgentRunner`. The page chooses which path to call. It does not contain tool code.

The host talks to models through `IModelProvider`. The Ollama plugin implements that interface. The host uses the first loaded provider and does not reference the Ollama project. A later provider can implement the same interface.

Agent tools implement `IAgentTool` and are published by `IAgentToolSource`. Web search is the first source, registered inside the host the same way a plugin source is registered. A plugin that also implements `IAgentToolSource` adds its tools at startup without a host change. The first registration of a name wins, so a plugin cannot replace `web_search`. Future tools (files, Ollama management, FreeToken, Odysseus, diagnostics) are additional sources. They are not part of this page.

Web search calls DuckDuckGo’s public HTML endpoint only. Redirects are followed only while the host stays on `duckduckgo.com`. The tool returns titles, links, and short snippets. It does not open those links, read files, or change the computer. The agent may call tools at most three times, then it must answer. A model that cannot use tools gets the sentence “This model can't use Agent mode. Switch to Chat, or try another model.”

### Versions and updates

Every external tool reports three facts: the version installed on this computer, the latest version Penelope could find, and whether an update is available. The Ollama module reads the installed version from `GET /api/version` or the program on disk, and the latest version from the official release. The check runs when the dashboard opens, and no more than once every few hours after that. A button checks again. Offline, the page shows the installed version and Couldn’t check for updates, with Try again.

Update is explicit. Penelope does not download a new Ollama because a check succeeded. The person presses Update, confirms, and watches the same progress and log used for install. The module stops the tool, runs the official installer, then starts it again. Cancel stays available while the installer file is still downloading. Once the installer process has started, the page says cancellation is no longer available. A failed update uses the same failure card as a failed install, and leaves the previously working version in place when the installer supports that.

Odysseus and FlashML implement the same version and update members. A plugin that cannot detect a latest version yet says so, and omits the Update button rather than inventing a number.

### Jobs

Long work (install, update, download, restart after a network change) is a job. The page shows two things the whole time: a progress bar, and a live log.

| Field | Meaning |
| --- | --- |
| `id` | Opaque id |
| `kind` | `install`, `upgrade`, `pull`, `restart` |
| `state` | `pending`, `running`, `completed`, `failed`, `cancelled` (canonical; see [`JOB_QUEUE.md`](JOB_QUEUE.md)) |
| `message` | One line safe to show above the bar |
| `completed`, `total` | Bytes when known |
| `log` | Append-only lines: time, level (`info`, `warning`, `error`), text |

The full wire format, including `moduleId`, `cancelAllowed`, `startedAt`, and `failure`, is in [`API.md`](API.md#jobs).

The page subscribes with server-sent events and appends each log line as it arrives. Refreshing the browser replays the log the host kept for that job. A second pull while one is running returns the existing job. Cancel aborts the host’s HTTP stream to Ollama during a download.

The log is on the page so nobody has to open a log file to see what happened. The summary sentence still comes first. The log is the record underneath it, in a scrolling region, newest line in view.

### When an install or update fails

A failed job ends in a failure card. The card is the only error UI for that job.

| Field | What the page does with it |
| --- | --- |
| `summary` | One sentence, in ordinary language. “Ollama didn’t install.” |
| `detail` | One or two sentences on the likely cause. “Windows asked for permission and the request was cancelled.” |
| `fixes` | Buttons. Each fix is an action the host already understands. |
| `log` | The job log, already on screen, scrolled to the first error line |

A fix is one of: `retry`, `check-again`, `open-url`, `open-firewall`, `open-disk`. `open-url` opens an `https` address the module chose, such as the official Ollama download. The payload never contains a command line. The page never renders a “copy this command” fix.

Examples the Ollama module must be able to return:

| What happened | Detail | Buttons |
| --- | --- | --- |
| Download failed | Penelope couldn’t reach the Ollama release. | Try again |
| Checksum mismatch | The downloaded installer didn’t match the published checksum. The card shows the expected checksum, the calculated checksum, and where the file was left. | Try again |
| Permission cancelled | Windows asked for permission and the request was cancelled. | Try again, Open the Ollama download page |
| Not enough disk | The models disk does not have room for this download. | Open the drive |
| Installer exited with an error | The Ollama installer stopped before it finished. | Try again, Open the Ollama download page |
| Already running and locked | Ollama is open and the installer couldn’t replace it. | Stop Ollama and try again |

### Dashboard API

All routes are under the dashboard origin. Model names travel in JSON bodies, because tags contain colons. The host exposes generic module operations only (status, install, upgrade, check, start, stop, fixes, jobs). Tool-specific routes are registered by the plugin under `/api/modules/{id}/`, so adding Ollama's model routes does not add a branch inside the host. Unknown module ids return 404.

The full route list, request bodies, response shapes, and status codes are in [`API.md`](API.md).

### Plugin contract

The host does not reference Ollama, Odysseus, or FlashML. At startup it scans a plugins directory and loads every valid manifest. Ollama is the first folder in that directory, not a special case in `Program.cs`. A plugin folder holds `manifest.json`, one assembly implementing `IToolModule`, and an optional `ui/index.js`.

`hostApi` in the manifest is the contract version the plugin was built against, written `"MAJOR.MINOR"` (today `"1.0"`). A plugin that asks for a newer host shows on Tools as unavailable, with the sentence "This tool needs a newer Penelope." That is a failure card, not a load error buried in a file.

A module owns its processes, ports, files, installers, and version source. The shell owns the session cookie, system readings, job storage, the generic tool page, the Chat page, and the tray icon that reopens the browser. A plugin without a `ui` entry is still fully usable through the generic page.

The manifest schema, `hostApi` compatibility rules, the `IToolModule` members, optional interfaces (`IModelProvider`, `IAgentToolSource`, and others), the UI bundle contract, and plugin UI safety are in [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md).

### Settings and files

User settings live in `%LOCALAPPDATA%\Penelope\config.json`.

```json
{
  "exposeOllamaOnLan": false,
  "ollamaPort": 11434,
  "setupComplete": false,
  "openBrowserOnLaunch": true,
  "theme": "dark"
}
```

Host diagnostics rotate in `%LOCALAPPDATA%\Penelope\logs\`. Job logs that a person needs are shown on the page. There is no account and no telemetry.

### Tray

One instance, mutex `Local\Penelope.Host`. A second launch opens the browser again.

The icon’s only jobs are to reopen the dashboard and to quit the host. Start, Stop, Install, Update, and every other action live on the page. Quit Penelope is also at the bottom of the sidebar, so the tray menu is unnecessary once the page is open. Quit stops the host and leaves external tools running. The first launch opens the browser. The tooltip repeats a short status line. The tray icon is the only Windows icon that changes color: green when a service is running, yellow while one is working, and red when services are stopped.

“Start Penelope when I sign in” is off until the person turns it on. The switch is on the welcome step and in the sidebar. Penelope writes one value under the current user's Run key, named `Penelope`, whose data is the quoted path of `Penelope.Host.exe`. Turning the switch off deletes that value. A `dotnet` path is never written.

### Windows target

64-bit Windows 10 22H2 and Windows 11. The installed app is a self-contained publish wrapped by `installer\penelope.iss`. Building Penelope requires the .NET 10 SDK, Node, and Inno Setup 6. Running it requires neither.

### Acceptance for the first release

On a clean Windows user account, with a network connection:

1. Installing Penelope and opening it shows the dashboard. Nothing in the flow asks for a terminal.
2. Install Ollama completes, or an existing Ollama is detected and left in place. The install view shows progress and log lines.
3. A forced install failure shows a sentence and at least one button. No fix is a command to type.
4. The page shows the installed Ollama version. When a newer official release exists, Update runs from the page and reports progress.
5. Start and Stop change the status line, and the status survives a browser refresh. Both are buttons on the page.
6. A curated model downloads with a progress bar and a live log.
7. Start on that model puts it on the in-memory list, and Stop removes it.
8. CPU, RAM, and at least one GPU reading show, or an honest Unavailable.
9. The network switch changes the listen address, and the dashboard remains reachable only from this computer.
10. A page on another website cannot perform these actions.
11. A plugin folder the host was not compiled with appears on Tools after restart, and its Install / Start / Stop actions render on the generic page.

---

## 2. Technology stack

.NET 10 is the current long-term release. .NET 8 leaves support in November 2026, so it is the wrong base for a new Windows app.

| Layer | Choice | Why |
| --- | --- | --- |
| Host | ASP.NET Core on .NET 10 | Kestrel serves the dashboard. The same process can start programs, read performance counters, and write a user environment variable. |
| Publish | `win-x64`, self-contained, single-file | The person does not install a runtime. |
| UI | React 19, TypeScript, Vite | Pages split cleanly per module. The production build is static files. |
| Styling | Plain CSS with the demo’s variables | A component library would fight the Odysseus colors, type, and density. |
| Tray | WinForms `NotifyIcon` on an STA thread | Works next to a generic host. There is no main window. |
| Firewall elevation | `Penelope.Elevation.exe` (planned) | `requireAdministrator` manifest. It accepts only `firewall-add` and `firewall-remove`. The main host stays as a normal user. **Not built for 0.1:** the Ollama plugin's `LanFirewall` makes one elevated `netsh` request with fixed arguments instead. See [`SECURITY.md`](SECURITY.md#elevation). |
| Installer | Inno Setup, per user | Startup entry, shortcut, uninstaller. A Store package makes the tray and firewall work harder for no v1 benefit. |
| Plugins | A folder scan, one assembly per manifest | The host does not reference tool projects. A new folder is a new tool. |
| Tests | xUnit, plus a fake Ollama HTTP server | Status parsing, job progress, catalog fit, and the “dashboard stays on loopback” rule can run without a GPU. |
| Frontend checks | TypeScript and Vitest for presentational helpers | Fit decisions stay on the host so the page cannot disagree with it. **Today only `tsc --noEmit` runs;** Vitest is not set up yet ([status](IMPLEMENTATION_STATUS.md#deviations-from-the-product-specification)). |
| Node | Build machine only | Vite produces `wwwroot`. The published host does not run Node. |

Development runs Vite on its own port and proxies `/api` to Kestrel. Production serves `wwwroot` from Kestrel and falls back to `index.html`.

### Version control

The repo root `.gitignore` keeps build output, dependencies, installers, and local settings out of Git. Do not commit `bin/`, `obj/`, `artifacts/`, `web/node_modules/`, `src/Penelope.Host/wwwroot/`, plugin `*.dll` / `*.pdb` / `ui/` bundles, download payloads, logs, or `.env` files. Runtime data (`%LOCALAPPDATA%\Penelope\`, including `downloads\` and `logs\`) never lives in the tree.

After a fresh clone, run `npm ci` in `web/`, then `npm run build` (shell plus Ollama plugin UI). `dotnet build` on the solution runs `BrandExport` and copies the Ollama module into `plugins/ollama/` (DLL and manifests). `scripts/publish.ps1` produces the shipping layout under `artifacts/`. Commit `web/package-lock.json`, source under `src/` and `web/src/`, `plugins/**/manifest.json` (and Ollama `catalog.json`), and generated `src/Penelope.Host/Assets/penelope.ico` plus `web/public/favicon.ico` once `BrandExport` has run so icon tests pass.

If large or generated paths were committed earlier, stop tracking them without deleting local copies, for example: `git rm -r --cached artifacts/ src/Penelope.Host/wwwroot/ plugins/ollama/ui web/node_modules` (add other paths as needed), then commit the `.gitignore` update.

### Options set aside

| Option | Why it waits |
| --- | --- |
| Electron | Ships a second browser. Penelope is meant to be used in the browser the person already has. |
| Tauri | A good webview shell. It adds a Rust application before the Windows service, firewall, and GPU problems are solved. |
| Go | A strong single binary. Firewall rules, the elevation helper, and GPU counters are more natural in .NET on this target. |
| Python | Fast to prototype. A weak double-click install for this audience. |
| Node as the host | The installed app would carry a Node runtime, or a packager that is painful to support. |

### Design tokens and brand mark

The design tokens taken from the demo (dark default, light theme, Fira Code, 14px root) and the brand-mark rules (`branding/penelope.svg` as the only logo source, the status dot only where live state is shown) are in [`UI_SPEC.md`](UI_SPEC.md#design-tokens).

---

## 3. File and folder structure

The source tree, the dependency rules between projects, and the ownership of each path are in [`ARCHITECTURE.md`](ARCHITECTURE.md#source-tree). The core rules: the host project does not reference a module project, a module does not reference another module, and `plugins/` is what the host loads. Differences between the originally planned tree and the actual one are listed there.

---

## 4. UI and UX plan

Principles, the shell layout and chrome, screens, the first visit, copy, and accessibility are in [`UI_SPEC.md`](UI_SPEC.md). The non-negotiable principles: one primary action on each screen; status is a word; every long job shows a progress bar and a live log; failures offer buttons, never commands; every external tool shows its version; a plugin with no custom pages still has a complete generic page.

---

## 5. Implementation roadmap

Build in this order. Phases 0–4 are the first release. People who use Penelope reach every action through the browser, including during development builds that already include the tray shortcut. Developers may start the host from the SDK. That path is not a product feature and never appears in the interface.

Progress against these phases is tracked in [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md#phase-status), which also keeps the per-phase verification notes that used to live here. Phases 0-4 and the model check are release 0.1; phases 5 and 6 are releases 0.3 and 0.4 in [`ROADMAP.md`](ROADMAP.md).

### Phase 0 — Shell and plugin host

**Effort:** medium.

The solution builds. The host serves the React shell on loopback with the session cookie. The tray opens the browser. Tokens, sidebar, and header render. The host loads whatever it finds under `plugins/` and does not reference those projects. The generic module page can show status, version, Install, Update, Start, and Stop. `JobPanel` and `FailureCard` render from a fake job.

**Done when:** a second website cannot call a state-changing route, the page matches the [token table](UI_SPEC.md#design-tokens), and a throwaway plugin folder appears in Tools without a host rebuild.

### Phase 1 — See and control Ollama

**Effort:** medium. Includes the process-name spike.

The Ollama plugin is loaded from `plugins/ollama/`. Detect installed, stopped, and running. Start and Stop are buttons on the page. The status line is the state machine: Not installed, Installing, Stopped, Starting, Running, Stopping. The page shows the installed version.

**Done when:** start and stop work against a real Windows Ollama, including “already running”, and the chosen process strategy is written into `OllamaProcess`. The host project still has no reference to the Ollama project.

### Phase 2 — Models

**Effort:** large.

List, curated catalog with fit warnings, pull jobs with progress, live log, and cancel, load, unload, remove. Refreshing the browser during a pull shows the same job and the same log.

**Done when:** the acceptance items for download, Start, and Stop pass on a machine that can hold the small starter model, and the log is visible without opening a file.

### Phase 3 — This computer and the network switch

**Effort:** medium.

CPU, RAM, GPU, VRAM, disk free. The listen toggle, restart, firewall helper, and the copyable address.

**Done when:** the dashboard is still bound to `127.0.0.1` after the switch is on, and the firewall rule is gone after the switch is off.

### Phase 4 — Install, update, and first visit

**Effort:** large.

Official Ollama download and per-user silent install, reuse of an existing install, failure cards with real fixes, version check and Update, the three-step welcome, Penelope’s own Inno Setup installer, and “start Penelope when I sign in”.

**Done when:** the clean-account acceptance list in section 1 passes, including a failed install and an update, with no terminal and no host change required to see the Ollama plugin.

### Model check — Chat and a limited agent

**Effort:** medium.

A Chat page sends a short prompt to the configured model provider and streams the reply. A mode switch selects Agent. Agent mode can use web search and nothing else. The conversation lasts for the browser tab.

**Done when:** Chat streams a reply from an installed model, Agent mode is a separate request that can call only registered tools, web search is the first of those tools, and the host still does not reference the Ollama project.

### Phase 5 — Odysseus

**Effort:** large, and it starts with a spike.

The Demo page was saved from `http://192.168.0.10:7000/`. That shows Odysseus as a local web application with its own port. The spike has to confirm the current install method, the process to supervise, the port, how it is pointed at Ollama, and how its version is read. The result is a new folder under `plugins/`. Penelope’s Chat page only checks a model. Odysseus remains the full chat product, and Penelope does not replace it. The generic page covers Install, Update, Start, and Stop. A custom UI bundle may add an Open Odysseus button that launches the browser.

Any step the installer cannot finish becomes a failure-card button, such as opening a vendor page. It does not become instructions to type.

### Phase 6 — FlashML FreeToken

**Effort:** large, after its own spike.

There is no installer, port, or process description in this repository. The spike writes those facts down, then a plugin folder implements the same `IToolModule` actions, including version and update. Dependencies install when the vendor’s installer allows it. Anything that needs a person is a button on the failure card or on the plan shown before install.

### Suggested order of attack inside a phase

Host behavior first, with the fake Ollama server in tests. Then the page that exposes it. Then a pass in the browser on Windows. A phase is not done because the page renders sample data.

---

## 6. Risks, limitations, and later improvements

### Risks

| Risk | Level | Response |
| --- | --- | --- |
| Ollama on Windows is a tray app. Process names can change. | High | Isolate them in `OllamaProcess`. Re-check on each Ollama upgrade Penelope claims to support. |
| `0.0.0.0` publishes an API with no password. Anyone on the network can run models and fill the disk. | High | Off by default. Confirmation copy. Private-profile firewall rule. Easy way back. Dashboard stays on loopback. |
| Other websites can try to call localhost. | High | Session cookie, `SameSite=Strict`, origin check, no cross-origin allow-list. |
| The silent installer may request administrator rights, or the person may cancel. | Medium | Prefer the per-user install. Explain the prompt first. On cancel, show Try again and Open the download page. |
| GPU drivers are large, reboot the PC, and are easy to get wrong. | Medium | Detect and link. CPU fallback is an honest status, not an error. |
| VRAM bytes are exact on NVIDIA and estimated elsewhere. | Medium | AMD and Intel utilization comes from the GPU Engine counter. Memory fill is labeled “about” only when that counter is missing. Show Unavailable rather than a zero. |
| Models are many gigabytes. | Medium | Free-space check, one pull at a time, progress owned by the host. |
| Antivirus products dislike tools that start and stop processes. | Medium | Elevation is limited to one fixed-argument `netsh` request and the official installer (the planned fixed-purpose helper was not built for 0.1; see [`SECURITY.md`](SECURITY.md#elevation)). Code signing is a release task before wide distribution. |
| `keep_alive` is the load control today. | Low | It stays inside `OllamaClient`. The module may call the Ollama program itself. The page does not show that call as a command. |
| A plugin can crash the host if loading is careless. | Medium | Load each plugin in its own assembly context. A bad manifest or incompatible `hostApi` becomes a Tools card with a sentence, and the other plugins still load. Plugins are trusted code; the assembly context is not a security boundary ([`SECURITY.md`](SECURITY.md#plugin-trust)). |
| A plugin UI bundle can throw and blank the page. | Medium | Error containment with fallback to the generic page ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-safety)). Today only a failed import is caught. |
| Odysseus and FlashML have no installer facts in this repo. | Medium | They arrive as new plugin folders after a spike. The host stays unchanged. |
| An update can replace a working Ollama with a broken one. | Medium | Confirm first. Stream the installer log. If the installer fails, say so and keep the failure card. Do not mark the update successful unless the new version answers `/api/version`. |
| Web search depends on a public HTML page. | Medium | The parser is tested against the current result markup. A failed search is a sentence in the reply. Redirects cannot leave DuckDuckGo. |

### Limitations of the first release

- Penelope manages one Ollama on this computer, on one port.
- It does not import models from other applications.
- It does not update Penelope itself. External tools are updated from the page, and only after the person presses Update.
- LAN access is the whole API, because Ollama does not offer a separate read-only bind.
- GPU acceleration depends on a driver the person installs. The page can open the vendor’s download. It cannot finish that installer.
- ARM64 Windows is unsupported.
- A corporate PC that blocks unsigned installers or firewall changes still gets buttons: Try again, and open the relevant Windows or vendor page. Penelope will not fall back to asking for a typed command.
- The Chat page checks a model. It does not keep the conversation after the browser tab closes.
- Agent mode can search the public web through one tool. It cannot read files, change Ollama, or open the result links.
- A model that does not support tools is asked to use Chat instead.

### Later improvements

- Signed builds and a public checksum for Penelope’s installer.
- Penelope’s own update check against a release the project controls, using the same job and failure card as a plugin.
- Open Odysseus from a running model once that plugin exists.
- OpenDyslexic, which the demo already treats as an accessibility font.
- Per-model memory estimates from `GET /api/ps` (`size_vram`) shown on the row after the first Start.
- Windows on ARM, only after the x64 path is dull and reliable.

### Open points for the first implementation spike

Phase 1 should settle these with a current Ollama installer and write the answers into `OllamaProcess`:

1. Is the live installer per-user and silent without administrator rights?
2. Which executable, once started, reliably opens port 11434?
3. Does the tray app honor a changed `OLLAMA_HOST`, or must Penelope launch `ollama.exe serve` itself?
4. Which checksum does the release publish, and where?

Until those four are answered in the running code’s comments, the installer, the updater, and the LAN switch should not be built on guesses. The answers stay inside the Ollama plugin.
