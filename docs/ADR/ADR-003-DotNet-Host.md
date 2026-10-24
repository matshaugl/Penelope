# ADR-003: The host is a self-contained .NET 10 app

- Status: Accepted
- Date: 2026-10-13 (records a decision made at project start)

## Context

The host must serve a web app, start and stop Windows programs, read performance counters and DXGI adapter data, call `GlobalMemoryStatusEx`, write user environment variables and the `Run` key, request elevated firewall changes, show a tray icon, and install with a double-click for people who will never install a runtime. It must also load plugins as typed assemblies ([ADR-001](ADR-001-Plugin-System.md)).

.NET 8 leaves support in November 2026, so it is the wrong base for a new Windows app. .NET 10 is the current long-term release.

## Decision

- ASP.NET Core on .NET 10, Kestrel serving the dashboard and API in the same process that controls tools.
- Published `win-x64`, self-contained, single-file, wrapped in a per-user Inno Setup installer.
- Tray via WinForms `NotifyIcon` on an STA thread; there is no main window.
- Plugins are .NET class libraries loaded at runtime.

Details: [product specification §2](../product-specification.md#2-technology-stack).

## Alternatives considered

| Option | Why not |
| --- | --- |
| Electron | Ships a second browser. Penelope is meant to be used in the browser the person already has. |
| Tauri | A good webview shell. It adds a Rust application before the Windows service, firewall, and GPU problems are solved. |
| Go | A strong single binary. Firewall rules, elevation, and GPU counters are more natural in .NET on this target. |
| Python | Fast to prototype. A weak double-click install for this audience. |
| Node as the host | The installed app would carry a Node runtime, or a packager that is painful to support. |
| .NET 8 | Leaves support in November 2026. |

## Consequences

- Building needs the .NET 10 SDK (`global.json` pins the feature band); running needs nothing.
- The installer is larger than a framework-dependent build. Accepted for a beginner audience.
- Single-file publish unpacks to a temporary folder, so plugin discovery looks beside `Penelope.Host.exe` first (`PluginRoots`).
- Windows-only by design; Linux and macOS are out of scope.
