# ADR-004: The interface is React in the person's own browser

- Status: Accepted
- Date: 2026-10-13 (records a decision made at project start)

## Context

Every action must be a control on one page, and the visual language must match the Odysseus Chat demo (dark theme, Fira Code, dense layout). Plugins need to contribute their own pages without the shell being rebuilt ([ADR-001](ADR-001-Plugin-System.md)), and a tool with no pages must still be usable.

## Decision

- The interface is a single-page app opened in the person's default browser. A tray icon reopens it.
- React 19, TypeScript, Vite. Production output is static files served by Kestrel from `wwwroot`; Node exists on the build machine only.
- Styling is plain CSS with variables taken from the demo. No component library.
- Plugin pages are separate Vite bundles (`plugins/<id>/ui/index.js`) exporting `mount(element, props)`, loaded at runtime by dynamic import. The shell never imports them at build time.
- The shell's generic module page covers any tool without custom pages.

Details: [`UI_SPEC.md`](../UI_SPEC.md), [`PLUGIN_SYSTEM.md`](../PLUGIN_SYSTEM.md#plugin-ui-bundles).

## Alternatives considered

| Option | Why not |
| --- | --- |
| Native WinForms/WPF UI | No easy plugin page model, duplicates the demo's look by hand, and needs a window the product does not want. |
| Blazor | Keeps one language, but plugin pages as runtime-loaded components are harder, and the WASM payload is large for a local dashboard. |
| Server-rendered pages | Live job logs, meters, and streaming chat favor a client app with SSE. |
| A component library (MUI, etc.) | Would fight the Odysseus colors, type, and density. |
| Plugin pages compiled into the shell | Every plugin page change would rebuild the shell; contradicts ADR-001. |

## Consequences

- Plugin UI bundles run in the dashboard origin. A crash in one must not take down the shell; see [Plugin UI safety](../PLUGIN_SYSTEM.md#plugin-ui-safety).
- Each plugin bundles its own React; the shell's copy is not part of the contract.
- The host must serve plugin scripts as `text/javascript`, and plugin bundles must not reference Node globals.
- Browser security (session cookie, origin checks) is load-bearing; see [ADR-002](ADR-002-Loopback-Only-Dashboard.md).
