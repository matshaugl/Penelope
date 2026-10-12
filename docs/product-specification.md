# Penelope

Penelope is a Windows application for people who want local AI models and do not want a command line. A shortcut opens a dashboard in the default browser, and every action is on that page. Ollama ships as a plugin. Odysseus and FlashML FreeToken are further plugins. Adding one does not change the Penelope host. The dashboard itself always stays on this computer.

The visual language comes from the Odysseus Chat page saved in `Demo/`. That folder is a reference. Penelope does not ship its scripts.

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
| Later plugins | Odysseus, FlashML FreeToken |
| Host | .NET 10, self-contained |
| Interface | React, TypeScript, Vite, CSS variables taken from the demo |
| Model Start and Stop | Load the model into memory, and unload it |
| GPU drivers | Detected and explained. A button opens the vendor’s page. Penelope does not install drivers. |
| Long jobs | Progress plus a live log, on the page |
| Install failure | A plain-language cause and one or more buttons that recover |
| Release cut | Phases 0–4 |

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
4. A small elevated helper adds one Windows Firewall inbound rule: TCP 11434, private profile only, scoped to that `ollama.exe`. The rule name is `Penelope Ollama LAN`.
5. The page shows `http://<this-pc>:<port>` and a copy button.

Turning it off deletes that firewall rule, sets `127.0.0.1`, and restarts Ollama. If the person refuses the administrator prompt, Penelope still keeps the listen address on localhost when the switch is off. When the switch is on and the firewall step is refused, the page says other devices may be blocked by Windows Firewall and offers Try again and Open Windows Firewall. Penelope launches that settings window. It does not print a `netsh` command for the person to run.

The Penelope dashboard never binds `0.0.0.0`.

A listen-address change stops the Ollama tray and starts `ollama.exe serve` with `OLLAMA_HOST` set on that process. The tray's own “Expose Ollama to the network” setting can override the user variable, so Penelope does not leave the tray as the parent of that server. Turning the switch off starts `ollama.exe serve` on `127.0.0.1` even if Windows Firewall declines the prompt. Turning it on, when the prompt is declined, leaves Ollama reachable on the network and says other devices may still be blocked. The firewall rule is added or removed by one elevated `netsh` request. The page never shows that command.

### Security of the dashboard

The host listens on `127.0.0.1` and an unprivileged port, default `47321`. If that port is taken, it tries the next 20 ports and writes the chosen port to `%LOCALAPPDATA%\Penelope\instance.json`.

A random session token is created in memory each time the host starts. The tray opens `http://127.0.0.1:<port>/session?token=...`. That response sets an `HttpOnly`, `SameSite=Strict` cookie and redirects to `/`. State-changing requests must carry that cookie and an `Origin` of the dashboard’s own origin. Cross-origin browser calls are refused. There is no `Access-Control-Allow-Origin` for other sites.

This matters because a page on the public web can otherwise try to call a localhost server.

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
| `state` | `queued`, `running`, `succeeded`, `failed`, `cancelled` |
| `message` | One line safe to show above the bar |
| `completed`, `total` | Bytes when known |
| `log` | Append-only lines: time, level (`info`, `warning`, `error`), text |

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

All routes are under the dashboard origin. Model names travel in JSON bodies, because tags contain colons.

The host exposes generic module operations only. Tool-specific routes are registered by the plugin. Adding Ollama’s model routes does not add a branch inside the host.

| Method | Path | Role |
| --- | --- | --- |
| GET | `/api/health` | Host is up |
| GET | `/api/system` | One sample of CPU, RAM, GPUs |
| GET | `/api/system/stream` | Server-sent samples |
| GET | `/api/modules` | Discovered plugins: id, name, availability, versions, whether a custom page is present |
| GET | `/api/modules/{id}` | Status, installed version, latest version, update availability |
| POST | `/api/modules/{id}/install` | Begin install. Returns a job. |
| POST | `/api/modules/{id}/upgrade` | Begin update. Returns a job. |
| POST | `/api/modules/{id}/check` | Refresh the latest-version lookup |
| POST | `/api/modules/{id}/start` | Start the tool |
| POST | `/api/modules/{id}/stop` | Stop the tool |
| GET | `/api/jobs/{id}` | Job snapshot, including the log so far |
| GET | `/api/jobs/{id}/events` | Server-sent progress and log lines |
| GET | `/api/chat/session` | Provider, active model, installed models, and registered agent tool names |
| POST | `/api/chat/stream` | Body `{ "model", "messages" }`. Streams a chat reply. No tools. |
| POST | `/api/agent/stream` | Same body. The agent may call registered tools, then streams the answer. |

The Ollama plugin contributes these, and another plugin would contribute its own:

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

Unknown module ids return 404. A plugin that loads but is not ready yet returns `"availability": "planned"` and rejects install, update, start, and stop with a failure card.

### Plugin contract

The host does not reference Ollama, Odysseus, or FlashML. At startup it scans a plugins directory and loads every valid manifest. The first release already works this way. Ollama is the first folder in that directory, not a special case in `Program.cs`.

A plugin folder:

```text
plugins/ollama/
  manifest.json
  Penelope.Modules.Ollama.dll
  catalog.json
  ui/index.js              optional custom pages
```

```json
{
  "id": "ollama",
  "name": "Ollama",
  "assembly": "Penelope.Modules.Ollama.dll",
  "ui": "ui/index.js",
  "hostApi": 1,
  "nav": [
    { "label": "Models", "path": "/ollama/models" },
    { "label": "Add a model", "path": "/ollama/catalog" },
    { "label": "Network", "path": "/ollama/network" }
  ]
}
```

`hostApi` is the contract version the plugin was built against. The host loads a plugin only when it understands that number. A plugin that asks for a newer host shows on Tools as unavailable, with the sentence “This tool needs a newer Penelope.” That is a failure card, not a load error buried in a file.

```text
IToolModule
  Id
  DisplayName
  Summary              one sentence for the generic page
  Availability         ready | planned | missing | error
  GetStatus()
  GetRelease()         installed version, latest version, checked-at, update available
  CheckForUpdate()
  PlanInstall()        steps the page can show before anything runs
  Install(jobLog)
  Upgrade(jobLog)
  Start()
  Stop()
  ContributeApi(app)   extra routes under /api/modules/{id}
```

`Install` and `Upgrade` write progress and log lines through the job. On failure they return a failure card. They do not throw raw installer text at the page.

A module owns its processes, ports, files, installers, and version source. The shell owns the session cookie, system readings, job storage, the generic tool page, the Chat page, and the tray icon that reopens the browser.

Two optional interfaces sit on the same host API version. A plugin can ignore both. `IModelProvider` answers chat completions. `IAgentToolSource` contributes agent tools. `hostApi` stays 1.

The shell builds navigation from `GET /api/modules`. Home and This computer belong to the shell. Every other sidebar entry comes from a plugin `nav` list. If a plugin has a `ui` entry, the shell loads that script from the plugin’s URL and mounts it. If it does not, the generic page still offers status, version, Install, Update, Start, Stop, progress, log, and failure buttons. A new tool with no custom pages is usable without a change to the React shell.

Ollama ships a custom UI bundle for Models, Add a model, and Network. That bundle is built as its own entry and copied into `plugins/ollama/ui/`. The shell’s main bundle does not import it. The host serves that script as `text/javascript`; a generic file response is not a module the browser will load. The Ollama bundle is built with `process.env.NODE_ENV` set to `production`, because the page has no Node `process` object.

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
| Firewall elevation | `Penelope.Elevation.exe` | `requireAdministrator` manifest. It accepts only `firewall-add` and `firewall-remove`. The main host stays as a normal user. |
| Installer | Inno Setup, per user | Startup entry, shortcut, uninstaller. A Store package makes the tray and firewall work harder for no v1 benefit. |
| Plugins | A folder scan, one assembly per manifest | The host does not reference tool projects. A new folder is a new tool. |
| Tests | xUnit, plus a fake Ollama HTTP server | Status parsing, job progress, catalog fit, and the “dashboard stays on loopback” rule can run without a GPU. |
| Frontend checks | TypeScript and Vitest for presentational helpers | Fit decisions stay on the host so the page cannot disagree with it. |
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

### Design tokens to implement first

Taken from `Demo/Odysseus Chat_files/style.css` and the inline theme on `Demo/Odysseus Chat.html`.

| Token | Dark (default) | Light |
| --- | --- | --- |
| `--bg` | `#282c34` | `#f5f5f5` |
| `--fg` | `#9cdef2` | `#2b2b2b` |
| `--panel` | `#111111` | `#ffffff` |
| `--border` | `#355a66` | `#bbbbbb` |
| `--accent` | `#e06c75` | `#e06c75` |
| `--green` | `#50fa7b` | `#2e7d32` |
| `--warn` | `#f0ad4e` | `#f0ad4e` |
| `--color-error` | `#ff4444` | `#c0392b` |
| `--color-subheader` | `#6b8a94` | `#4a6168` |
| `--sidebar-bg` | `#111111` | `#ffffff` |
| Font | Fira Code | Fira Code |

Fira Code is the demo’s voice. Bundle the SIL Open Font License files under `web/src/design/fonts/`. They are not in the Demo folder today. Root size is 14px. Instructions use `--fg`. Secondary labels may use `--color-subheader`. `#888`, which the demo uses as `--color-muted`, is too faint for text a person must read, so Penelope does not use it for copy.

Light theme variables ship in the first stylesheet. A sidebar control switches them. Dark is the default, matching the reference.

### Brand mark

The mark is the dark rounded tile and the white P in `branding/penelope.svg`. That file is the only logo source, and it does not contain the status dot. `scripts/BrandExport` draws it into `src/Penelope.Host/Assets/penelope.ico` and `web/public/favicon.ico`. The program icon is that file, so the taskbar, shortcuts, Add/Remove Programs, the setup wizard, and the uninstaller show the clean mark. The browser tab uses `favicon.svg`. The sidebar, welcome flow, loading state, empty states, and the locked page use the same clean mark. The colored dot is added only where a live state is shown: the dashboard header, a tool card, a tool page, and the tray icon. Its position is `data-status-cx`, `data-status-cy`, and `data-status-r` on the source SVG. Green means running, yellow means working, and red means stopped.

---

## 3. File and folder structure

```text
Penelope/
  Demo/                              Reference site. Not compiled. Not shipped.
  docs/
    product-specification.md
  Penelope.sln
  src/
    Penelope.Abstractions/
      IToolModule.cs
      ToolStatus.cs
      InstallPlan.cs
      JobModels.cs
    Penelope.Host/
      Program.cs                     Kestrel, static files, plugin scan
      Security/
        SessionToken.cs
        LoopbackOnly.cs
      Api/
        HealthEndpoints.cs
        SystemEndpoints.cs
        JobEndpoints.cs
        ModuleEndpoints.cs
      Plugins/
        PluginCatalog.cs             Load manifests. No tool names.
      Jobs/
        JobRunner.cs
        JobLog.cs
      SystemInfo/
        CpuRamProbe.cs
        GpuProbe.cs
      Tray/
        TrayIcon.cs
      wwwroot/                       Vite build output. Generated.
      appsettings.json               Log levels only.
    Penelope.Elevation/
      Program.cs                     firewall-add, firewall-remove
      app.manifest                   requireAdministrator
    Penelope.Modules.Ollama/
      OllamaModule.cs
      OllamaClient.cs                HTTP only. No UI.
      OllamaProcess.cs               Start, stop, OLLAMA_HOST
      OllamaInstaller.cs
      OllamaRelease.cs               Installed version and latest release
      catalog.json
    Penelope.Host.Tests/
  plugins/                           Runtime layout. Host scans this. Not a project reference.
    ollama/                          Publish output of the Ollama module plus its UI bundle
  web/
    package.json
    vite.config.ts
    index.html
    src/
      main.tsx
      app/
        App.tsx
        api.ts
      design/
        tokens.css
        global.css
        fonts/
      components/
        AppShell.tsx
        Sidebar.tsx
        StatusHeader.tsx
        Button.tsx
        Toggle.tsx
        Meter.tsx
        ProgressBar.tsx
        JobPanel.tsx                 Progress bar and live log
        FailureCard.tsx              Summary, detail, fix buttons
        EmptyState.tsx
        Disclosure.tsx
      pages/
        SetupPage.tsx                First visit. Hands install to the Ollama plugin.
        HomePage.tsx
        SystemPage.tsx               This computer. Host-owned.
        ToolsPage.tsx
        GenericModulePage.tsx        Used when a plugin ships no custom UI
      modules/
        types.ts
        loadModuleUi.ts              Dynamic import of a plugin's ui/index.js
        ollama/                      Separate bundle, copied to plugins/ollama/ui/
          ModelsPage.tsx
          CatalogPage.tsx
          NetworkPage.tsx
          ModelRow.tsx
  installer/
    penelope.iss
  scripts/
    publish.ps1
```

### Ownership

| Path | Owns |
| --- | --- |
| `Penelope.Abstractions` | Types every module and the host share. No Windows calls. |
| `Penelope.Host` | Browser session, tray, jobs, system readings, static files, plugin scan. |
| `Penelope.Elevation` | The two firewall verbs. Nothing else. |
| `Penelope.Modules.Ollama` | Installer, process, releases, Ollama HTTP, catalog fit. Output is copied into `plugins/ollama/`. |
| `web/src/design` | Tokens and chrome. Plugin pages consume these. They do not restyle the shell. |
| `web/src/modules/ollama` | Ollama’s custom pages, emitted as a plugin UI bundle. |
| `web/src/pages/GenericModulePage.tsx` | The page every plugin gets even with no custom UI. |
| `plugins/` | What the host loads. Adding a folder here does not change the host. |
| `installer` | The thing a person double-clicks. It ships the host and the plugins that are ready. |

The host project does not reference a module project. A module does not reference another module. Odysseus, when it exists, calls Ollama through Ollama’s HTTP port or through the host’s module status API. Its source can live in its own folder and its build output is another directory under `plugins/`.

---

## 4. UI and UX plan

### Principles

1. One primary action on each screen.
2. Status is a word. Color supports the word.
3. Every long job shows a progress bar and a live log on that screen.
4. A failed install or update names the cause and offers buttons. It never offers a command.
5. Every external tool shows its version, and Update when a newer release exists.
6. A plugin with no custom pages still has a complete generic page.

### Shell

The demo’s structure is a fixed sidebar and a full-height workspace. Penelope keeps that. The demo’s full chat product stays in Odysseus. Penelope’s Chat page only checks that a local model answers.

```text
+------------------+----------------------------------------------+
| Penelope         | Ollama is running                 [ Stop ]   |
|                  | Ollama 0.12.1 · Update available             |
| Home             |                                              |
| Chat             |                                              |
| Models           |  CPU 12%      RAM 9 / 32 GB     GPU 40%      |
| Add a model      |                                              |
| This computer    |  Models in memory                            |
| Network          |  General  ·  4.7 GB  ·  In memory   [ Stop ] |
| Tools            |                                              |
|                  |                                              |
| theme      Quit  |                                              |
+------------------+----------------------------------------------+
```

| Chrome | Rule |
| --- | --- |
| Sidebar | 240px, `--panel`, 1px `--border` on the right. Brand “Penelope” at the top. |
| Nav row | 8px padding, 4px radius. Active row: accent mixed about 12% into the panel. |
| Header | Status sentence on the left. The service Start or Stop on the right. |
| Workspace | `--bg`, content column max 960px, 24px page padding. |
| Radius | 4px rows, 6px buttons and fields, 8px cards and menus. |
| Space | 4, 8, 12, 16, 24. |
| Hover | Foreground mixed about 6% into the surface. |
| Focus | 2px `--accent` outline, 2px offset. |
| Scrollbar | Thin, thumb `--accent`, track `--panel`. |
| Motion | 150ms. Turned off when the system asks for reduced motion. |
| Primary button | Fill `--accent`, near-black label, weight 600. |
| Secondary button | Transparent, 1px `--border`, label `--fg`. |
| Destructive | Text `--color-error`, no fill. Confirm before removing a model. |

Home, Chat, This computer, and Tools belong to the shell. Models, Add a model, and Network are sidebar entries from the Ollama plugin manifest. A later plugin adds its own entries the same way. Quit sits at the bottom of the sidebar, next to the theme control.

### Screens

| Screen | Primary action | What the person sees |
| --- | --- | --- |
| Welcome | Continue | One sentence: Penelope installs and runs local models on this computer. |
| Install | Install Ollama | What will happen, then a progress bar and a live log. |
| Home | The one next step | Status, version, three meters, models currently in memory. The button is Install, Start, Update, or Add a model, depending on state. |
| Chat | Send | The active model, Chat or Agent, and the current tab’s conversation. Agent can search the web. |
| Models | Start or Stop on a row | Name, size, On disk or In memory. Empty state asks them to add a model. |
| Add a model | Download on one card | A few recommended cards. Other computers see the same catalog filtered by this computer’s memory. A text field accepts another tag. |
| This computer | None | CPU, RAM, each GPU, VRAM, disk free on the models volume. |
| Network | The listen toggle | “Only this computer” or “Other devices on my network”, the address, and the warning. |
| Tools | Open a tool | One card per discovered plugin. A plugin without custom pages opens the generic page: version, Install, Update, Start, Stop. |
| Job | Cancel, while the download is still in progress | Progress bar, percent when known, and the log underneath. |
| Failure | The first fix | Summary, cause, buttons, log scrolled to the error. |

### First visit

1. Welcome.
2. Install, or “Ollama is already installed” and continue.
3. Optional pick of a starter model. Skip is allowed. The home page then carries Add a model as its primary action.

Three steps is the ceiling. There is no account step.

### Copy

| Situation | Heading | Button |
| --- | --- | --- |
| Ollama missing | Ollama is not installed | Install Ollama |
| Installed, stopped | Ollama is stopped | Start Ollama |
| Running, no models | Ollama is running | Add a model |
| Download in progress | Downloading {name} | Cancel |
| Model on disk | On disk | Start |
| Model in memory | In memory | Stop |
| Remove | Remove this model from the computer? | Remove |
| LAN off | Only this computer can use Ollama | Other devices on my network |
| LAN on | Other devices on your network can use Ollama. There is no password. | Only this computer |
| Update available | Ollama {installed} is installed. {latest} is available. | Update Ollama |
| Updating | Updating Ollama | Cancel, until the installer itself starts |
| Install failed | Ollama didn’t install | Try again |
| Permission cancelled | Windows asked for permission and the request was cancelled. | Try again |

Helper line under a model’s Stop: “This frees memory. It does not delete the model.”

If a download is large, the card states the size and the free space before Download enables. If VRAM is below the catalog hint, the card says the model can run in system memory and will be slower.

A failure card is the shape of every install and update error. “There is not enough memory to start this model. Stop another model, or choose a smaller one.” is the shape of an action error. A stack trace is never the only message, and a command line is never the fix.

### Accessibility

Every action is reachable from the keyboard. The status line is text, not only a colored dot. Meters include the percent as text. The accent focus ring is always visible.

---

## 5. Implementation roadmap

Build in this order. Phases 0–4 are the first release. People who use Penelope reach every action through the browser, including during development builds that already include the tray shortcut. Developers may start the host from the SDK. That path is not a product feature and never appears in the interface.

**Status on 12 October 2026**, from the source rather than from this list alone:

| Phase | State |
| --- | --- |
| 0 Shell and plugin host | Done. Loopback session, plugin scan, generic tool page, jobs, and the sample plugin. |
| 1 See and control Ollama | Done. `plugins/ollama` detects, starts, and stops the real Windows app. The host project does not reference it. Spike notes are in `OllamaProcess`. |
| 2 Models | Done. List, catalog, pull, load, unload, and remove. `qwen2.5:0.5b` was downloaded, loaded, and unloaded on this PC. Catalog cards warn when installed memory or video memory is below the model's request. |
| 3 This computer and the network switch | Partial. CPU, memory, graphics, and the models drive are live. NVIDIA utilization comes from `nvidia-smi`. AMD and Intel utilization comes from the GPU Engine counter, with a memory-fill fallback labeled “about”. The Network page can restart Ollama onto `0.0.0.0` or back to `127.0.0.1` and asks Windows for one private firewall rule. This PC was left on loopback, so the live firewall round trip is still open. |
| 4 Install, update, and first visit | Partial. The page can download the official installer, check its published checksum, and run the per-user silent install. The first visit is Welcome, then Install or “already installed”, then an optional starter model. “Start Penelope when I sign in” writes the current-user Run key and is off until the person turns it on. `scripts\publish.ps1` builds `artifacts\PenelopeSetup.exe`. A forced Ollama install failure and a watched update on a clean account are still open. |
| Model check | Done. Chat streams to the running model. Agent mode can call web search only. Tools use `IAgentToolSource`. The conversation stays in the browser tab. |
| Brand mark | Done. `branding/penelope.svg` is the clean P tile. The program, installer, shortcuts, favicon, sidebar, welcome, loading, empty states, and locked page use it. The status dot stays on the header, tool cards, tool pages, and the tray. |
| 5 Odysseus | Not started. |
| 6 FlashML FreeToken | Not started. |

### Phase 0 — Shell and plugin host

**Effort:** medium.

The solution builds. The host serves the React shell on loopback with the session cookie. The tray opens the browser. Tokens, sidebar, and header render. The host loads whatever it finds under `plugins/` and does not reference those projects. The generic module page can show status, version, Install, Update, Start, and Stop. `JobPanel` and `FailureCard` render from a fake job.

**Done when:** a second website cannot call a state-changing route, the page matches the token table, and a throwaway plugin folder appears in Tools without a host rebuild.

### Phase 1 — See and control Ollama

**Effort:** medium. Includes the process-name spike.

The Ollama plugin is loaded from `plugins/ollama/`. Detect installed, stopped, and running. Start and Stop are buttons on the page. The status line is the state machine: Not installed, Installing, Stopped, Starting, Running, Stopping. The page shows the installed version.

**Done when:** start and stop work against a real Windows Ollama, including “already running”, and the chosen process strategy is written into `OllamaProcess`. The host project still has no reference to the Ollama project.

**State:** done. Verified against Ollama 0.35.1 on this PC, including the already-running case.

### Phase 2 — Models

**Effort:** large.

List, curated catalog with fit warnings, pull jobs with progress, live log, and cancel, load, unload, remove. Refreshing the browser during a pull shows the same job and the same log.

**Done when:** the acceptance items for download, Start, and Stop pass on a machine that can hold the small starter model, and the log is visible without opening a file.

**State:** done for the model actions. The small starter downloaded with a job log, then Start and Stop changed its in-memory flag. A refresh keeps that job on the page while Penelope is still running. Catalog warnings use the live memory and video-memory readings.

### Phase 3 — This computer and the network switch

**Effort:** medium.

CPU, RAM, GPU, VRAM, disk free. The listen toggle, restart, firewall helper, and the copyable address.

**Done when:** the dashboard is still bound to `127.0.0.1` after the switch is on, and the firewall rule is gone after the switch is off.

**State:** CPU, memory, graphics cards, and free space on the models drive are live. NVIDIA utilization comes from `nvidia-smi` on this PC. AMD and Intel utilization is the busier of the summed 3D and Compute GPU Engine counters for that card. Tests cover adding several processes, preferring Compute, ignoring Copy, treating a quiet card as zero, and ignoring an unknown adapter. If the engine counter is missing, the meter uses dedicated-memory fill and says “about”. The Network page, the serve restart, and the private firewall rule are implemented. Tests cover turning the switch on, turning it off when the administrator prompt is refused, and the dashboard source staying on `127.0.0.1`. This PC was left on loopback, so that firewall round trip has not been watched live.

### Phase 4 — Install, update, and first visit

**Effort:** large.

Official Ollama download and per-user silent install, reuse of an existing install, failure cards with real fixes, version check and Update, the three-step welcome, Penelope’s own Inno Setup installer, and “start Penelope when I sign in”.

**Done when:** the clean-account acceptance list in section 1 passes, including a failed install and an update, with no terminal and no host change required to see the Ollama plugin.

**State:** the Ollama download, checksum, silent install, failure cards, version check, and Update are implemented. The first visit shows three steps and then sets `setupComplete`. Sign-in startup is a checkbox. It writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Penelope` to the quoted `Penelope.Host.exe` and removes that value when turned off. Tests keep the switch in memory so they do not change the signed-in user's registry. Penelope’s own installer compiles to `artifacts\PenelopeSetup.exe`. A silent install into a temporary folder opened the dashboard, loaded the Ollama plugin, and did not load the sample plugin. Uninstall removed the program and both shortcuts. A clean Windows account, a forced Ollama install failure, and a watched Ollama update are still open.

### Model check — Chat and a limited agent

**Effort:** medium.

A Chat page sends a short prompt to the configured model provider and streams the reply. A mode switch selects Agent. Agent mode can use web search and nothing else. The conversation lasts for the browser tab.

**Done when:** Chat streams a reply from an installed model, Agent mode is a separate request that can call only registered tools, web search is the first of those tools, and the host still does not reference the Ollama project.

**State:** done. The shell page posts to `/api/chat/stream` or `/api/agent/stream`. Ollama implements `IModelProvider`. Web search is an `IAgentToolSource` in the host. A plugin can add tools by implementing that same interface. Tests cover a successful tool round, a refused unknown tool, the three-call stop, streamed chat lines, a model that cannot use tools, search HTML, and a redirect that leaves DuckDuckGo.

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
| Antivirus products dislike tools that start and stop processes. | Medium | The elevation helper is tiny and fixed-purpose. Code signing is a release task before wide distribution. |
| `keep_alive` is the load control today. | Low | It stays inside `OllamaClient`. The module may call the Ollama program itself. The page does not show that call as a command. |
| A plugin can crash the host if loading is careless. | Medium | Load each plugin in its own assembly context. A bad manifest becomes a Tools card with a sentence, and the other plugins still load. |
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
