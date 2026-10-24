# Plugin system

Canonical document for the plugin folder layout, `manifest.json`, `hostApi` versioning, the `IToolModule` contract, optional interfaces, and plugin UI bundles. Provider kinds and the host services handed to plugins are in [`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md). Why plugins exist at all is in [ADR-001](ADR/ADR-001-Plugin-System.md).

## Principle

The host does not reference Ollama, Odysseus, or FlashML. At startup it scans a plugins directory and loads every valid manifest. The first release already works this way. Ollama is the first folder in that directory, not a special case in `Program.cs`.

A second kind of person writes a plugin. They add a folder. They do not edit the host, the shell, or the installer.

## Plugin folder

```text
plugins/ollama/
  manifest.json
  Penelope.Modules.Ollama.dll
  catalog.json
  ui/index.js              optional custom pages
```

The host resolves the plugins directory beside `Penelope.Host.exe` first, then the single-file unpack folder (`PluginRoots`). Assembly and UI paths in the manifest must stay inside the plugin folder; paths that escape it are refused.

## Manifest

```json
{
  "id": "ollama",
  "name": "Ollama",
  "assembly": "Penelope.Modules.Ollama.dll",
  "hostApi": "1.0",
  "ui": "ui/index.js",
  "summary": "The local model server. Penelope starts and stops it on this computer.",
  "nav": [
    { "label": "Models", "path": "/ollama/models" },
    { "label": "Add a model", "path": "/ollama/catalog" },
    { "label": "Network", "path": "/ollama/network" }
  ]
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | Yes | Stable lowercase id. Must equal `IToolModule.Id`. Becomes the route prefix `/api/modules/{id}`. |
| `name` | Yes | Display name used when the assembly cannot load |
| `assembly` | Yes | Assembly file inside the folder |
| `hostApi` | Yes | Plugin contract version, `"MAJOR.MINOR"`. See below. |
| `ui` | No | Custom page bundle. Without it the generic page is used. |
| `summary` | No | Fallback one-sentence summary |
| `nav` | No | Sidebar entries. `IToolModule.Nav` wins when non-empty. |

## `hostApi` versioning

### Decision

`hostApi` is a two-part `"MAJOR.MINOR"` string. The field keeps its name (rather than `apiVersion`) because it describes the contract between a plugin and the host, not the HTTP API version, and renaming it would break every existing manifest for no gain.

The host's version is `HostApi.Current` in `Penelope.Abstractions` (`HostApi.Major`, `HostApi.Minor`). Today it is **1.0**.

A bare integer (`"hostApi": 1`) is the original manifest form. The host still reads it as `MAJOR.0`, so `1` means `"1.0"`. New manifests use the string form.

### Why two parts

A single integer forced a choice on every contract change: bump it and strand every existing plugin, or leave it and let a new plugin silently load on an old host that lacks what it needs. A minor number separates additive changes from breaking ones.

### Compatibility rules

A host at `H.h` loads a plugin that declares `P.p` only when `P == H` and `p <= h`.

| Plugin declares | Host 1.0 | Host 1.2 | Host 2.0 |
| --- | --- | --- | --- |
| `1.0` (or `1`) | Loads | Loads | "This tool was built for an older Penelope." |
| `1.1` | "This tool needs a newer Penelope." | Loads | "This tool was built for an older Penelope." |
| `2.0` | "This tool needs a newer Penelope." | "This tool needs a newer Penelope." | Loads |
| missing | "This tool was built for an older Penelope." (treated as `0.0`) | same | same |
| unreadable (`"one"`, `1.5`) | "This tool couldn't be loaded." | same | same |

A plugin that cannot load still appears on Tools as a card with that sentence and `availability: "error"`. That is a failure card, not a load error buried in a file. The other plugins still load.

### What bumps which number

| Change to the plugin contract | Bump |
| --- | --- |
| New optional interface a plugin may implement (`IModelProvider`, `IAgentToolSource` were this kind) | Minor |
| New member on `HostInfrastructure`, new optional manifest field, new event name, new `FixKind` the host understands | Minor |
| New required member on `IToolModule`, removed or renamed member, changed semantics of an existing member | Major |
| Change to how `Penelope.Abstractions` or `Penelope.Infrastructure` is shared with plugins | Major |
| Internal host changes, new host-only routes, UI changes | None |

Rule of thumb: if a plugin compiled against the old abstractions still runs correctly on the new host, it is a minor bump. Otherwise it is major.

`IModelProvider` and `IAgentToolSource` were added before versioning was formalized and are part of 1.0.

### Future versions

- **1.1, 1.2** — additive. Old 1.x plugins keep loading. A plugin that needs a 1.2 feature declares `"1.2"` and older hosts refuse it with "needs a newer Penelope" instead of crashing at first use.
- **2.0** — breaking. Every 1.x plugin shows "built for an older Penelope" until it is rebuilt. Before shipping a major bump, decide whether the host should carry a compatibility shim for the previous major; if it does, extend `HostApiVersion.CheckAgainst` to accept that major explicitly and record the decision in an ADR.

## Module contract

```text
IToolModule
  Id
  DisplayName
  Summary              one sentence for the generic page
  Availability         ready | planned | missing | error
  Nav                  sidebar entries (optional; manifest nav is the fallback)
  GetStatus()
  GetRelease()         installed version, latest version, checked-at, update available
  CheckForUpdateAsync()
  PlanInstall()        steps the page can show before anything runs
  InstallAsync(jobLog)
  UpgradeAsync(jobLog)
  StartAsync()
  StopAsync()
  ContributeApi(endpoints)   extra routes under /api/modules/{id}
```

`InstallAsync` and `UpgradeAsync` write progress and log lines through the job. On failure they return a failure card. They do not throw raw installer text at the page.

A plugin that loads but is not ready yet returns `availability: "planned"` and the host rejects install, update, start, and stop with a failure card.

### Optional interfaces

All are part of host API 1.0. A plugin can ignore every one of them.

| Interface | Purpose |
| --- | --- |
| `IToolProvider` | Declares a `ProviderKind`. See [`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md). |
| `IInfrastructureModule` | Receives `HostInfrastructure` (downloads, events, diagnostics, jobs) after load |
| `IJobAwareModule` | Receives `IJobQueue` to start its own jobs (for example model pulls) |
| `IModelProvider` | Answers chat completions for the Chat page. The host uses the first loaded provider. |
| `IAgentToolSource` | Contributes `IAgentTool`s to Agent mode. The first registration of a name wins, so a plugin cannot replace `web_search`. |

Wiring order (in `ModuleEndpoints`): load → `UseInfrastructure` → `UseJobs` → `ContributeApi`.

## Ownership boundary

A module owns its processes, ports, files, installers, and version source. The shell owns the session cookie, system readings, job storage, the generic tool page, the Chat page, and the tray icon that reopens the browser.

A module does not reference another module. Odysseus, when it exists, calls Ollama through Ollama's HTTP port or through the host's module status API.

## Plugin UI bundles

The shell builds navigation from `GET /api/modules`. Home and This computer belong to the shell. Every other sidebar entry comes from a plugin `nav` list. If a plugin has a `ui` entry, the shell loads that script from the plugin's URL and mounts it. If it does not, the generic page still offers status, version, Install, Update, Start, Stop, progress, log, and failure buttons. A new tool with no custom pages is usable without a change to the React shell.

The bundle is an ES module that exports:

```ts
mount(element: HTMLElement, props: { path: string }): () => void   // returns unmount
```

Ollama ships a custom UI bundle for Models, Add a model, and Network. That bundle is built as its own entry and copied into `plugins/ollama/ui/`. The shell's main bundle does not import it. The host serves that script as `text/javascript` from `/plugins/{id}/ui/{file}`; a generic file response is not a module the browser will load. The Ollama bundle is built with `process.env.NODE_ENV` set to `production`, because the page has no Node `process` object.

Plugin pages consume the shell's design tokens. They do not restyle the shell. See [`UI_SPEC.md`](UI_SPEC.md).

## Plugin UI safety

Status: **guidance for future work.** Today `PluginPage` catches a failed import or a missing `mount` export and shows "This page could not be opened." It does not yet catch errors thrown after mount.

### Threat model

A plugin UI bundle is code from a plugin folder running in the dashboard's origin. A bug in it (an exception during render, an unhandled promise, an infinite loop) must not take down the shell, the sidebar, the header Start/Stop, or Quit. A person must always be able to reach the generic page for that tool.

This section is about **fault isolation**, not hostile code. A plugin's .NET assembly already runs with full trust inside the host (see [`SECURITY.md`](SECURITY.md#plugin-trust)), so a sandbox around its UI alone would not stop a malicious plugin.

### Requirements for the plugin page host

1. **Error boundary around every plugin mount.** `PluginPage` should wrap the mount point in a React Error Boundary owned by the shell. Because plugin bundles render into their own React root, the boundary alone does not see their render errors; the shell must also:
   - wrap the `mount()` call and the returned `unmount()` in `try/catch`;
   - listen for `error` and `unhandledrejection` on `window` while a plugin page is mounted, and attribute errors whose stack points at `/plugins/{id}/ui/` to that plugin.
2. **Graceful fallback to `GenericModulePage`.** When a plugin page fails to load or crashes, show a short sentence ("This tool's page stopped working.") above the tool's generic page, which still offers status, version, Install, Update, Start, Stop, the job panel, and failure fixes. The person never lands on a blank workspace.
3. **Shell stays alive.** Sidebar, status header, theme switch, and Quit are outside the plugin's mount element and must remain interactive. A crashed plugin page is unmounted and its DOM node replaced.
4. **Log the crash.** Report the error summary to the host so it reaches the diagnostic log under category `ui:{pluginId}` (needs a small diagnostics POST route; not built yet). Never show a stack trace as the only message.
5. **No retry loop.** After a crash, do not remount automatically on every render. Offer a "Try again" button that remounts once.
6. **Clean unmount.** Navigating away calls the plugin's `unmount()` inside `try/catch`. An exception there is logged and ignored.

### Expectations for plugin authors

- Render only inside the element passed to `mount`. Do not touch `document.body`, the shell's DOM, or global styles outside CSS variables.
- Call only `/api/...` routes, preferably your own `/api/modules/{id}/...`. Do not rely on shell internals; they are not part of the plugin contract.
- Bundle your own React. Do not assume the shell's copy.
- Return an `unmount` that removes listeners, timers, and EventSources.

### Future sandboxing

If Penelope ever loads plugins from third parties it does not ship, UI isolation should be revisited together with assembly trust:

- **Sandboxed iframe** per plugin page (`sandbox="allow-scripts"`, separate origin such as a second loopback port) with a `postMessage` bridge exposing a narrow API. Strongest isolation; costs shared styling and needs its own session handling.
- **Shadow DOM** mount for style isolation only. Cheap; does not isolate script errors.
- **Content Security Policy** restricting plugin scripts to `/plugins/{id}/ui/`.
- **Signed plugin folders** so the host can refuse unknown code before any of the above matters.

None of these is planned for the current roadmap. Record the choice in an ADR when it is made.
