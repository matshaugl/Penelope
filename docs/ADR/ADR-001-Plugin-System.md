# ADR-001: Tools are folder-discovered plugins

- Status: Accepted
- Date: 2026-10-13 (records a decision made at project start)

## Context

Penelope manages external tools: Ollama first, then Odysseus, FlashML FreeToken, Docker, and possibly others. Each tool has its own installer, process model, ports, version source, and failure modes. If each tool were a branch inside the host, every new integration would touch the host, the shell, and the installer, and tool-specific bugs would leak into shared code.

The product also names a second kind of person: someone who writes a plugin. They should be able to add a tool without editing the host.

## Decision

- Each tool is a plugin: a folder under `plugins/<id>/` with `manifest.json`, one .NET assembly implementing `IToolModule`, and an optional `ui/index.js` page bundle.
- The host scans `plugins/` at startup and loads each assembly into its own `AssemblyLoadContext`, sharing only `Penelope.Abstractions` and `Penelope.Infrastructure`.
- `Penelope.Host` never references a module project. A test enforces this.
- A tool with no custom UI is fully usable through the shell's generic module page.
- Plugins declare the contract version they need with `"hostApi": "MAJOR.MINOR"`; the host refuses incompatible plugins with a sentence on the Tools page, never a crash.
- Ollama is the first plugin, not a special case.

Details: [`PLUGIN_SYSTEM.md`](../PLUGIN_SYSTEM.md), [`PROVIDER_SYSTEM.md`](../PROVIDER_SYSTEM.md).

## Alternatives considered

| Option | Why not |
| --- | --- |
| Tools compiled into the host | Every new tool changes the host; violates the plugin-author goal; Ollama specifics leak everywhere. |
| Out-of-process plugins (separate executables over IPC/HTTP) | Strong isolation, but each plugin needs its own process lifecycle, IPC contract, and installer story. Too heavy for v1; can be revisited if plugins become third-party. |
| Script plugins (PowerShell, JS) | Weak typing for a contract that includes jobs, failure cards, and streaming; PowerShell conflicts with the "no terminal" spirit and has execution-policy friction. |
| MEF / a DI-container plugin framework | Adds a framework where a folder scan and one interface suffice. |

## Consequences

- New tools ship as folders; the host, shell, and installer logic stay unchanged.
- The `Penelope.Abstractions` surface is a public contract. Changes follow the `hostApi` compatibility rules.
- Plugins run in-process with full trust. `AssemblyLoadContext` is not a security boundary, so only plugins Penelope builds are shipped ([`SECURITY.md`](../SECURITY.md#plugin-trust)).
- A broken plugin must never block other plugins; load errors become Tools cards.
- Cross-tool features (Odysseus using Ollama) go over HTTP, never module-to-module references.
