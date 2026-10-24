# Security

Canonical document for Penelope's security model. Why the dashboard is loopback-only is in [ADR-002](ADR/ADR-002-Loopback-Only-Dashboard.md). Route-level rules are summarized in [`API.md`](API.md#conventions).

## Assets and threats

| Asset | Threat | Primary control |
| --- | --- | --- |
| Dashboard actions (install, start, network switch, quit) | A web page on the public internet calls `http://127.0.0.1:<port>` from the person's browser | Session cookie + origin check + Host check |
| Dashboard actions | Another device on the LAN | Dashboard binds `127.0.0.1` only |
| Ollama API on `0.0.0.0` | Anyone on the network runs models and fills the disk | Off by default, confirmation, private-profile firewall rule, easy way back |
| Installers Penelope runs | Tampered download | HTTPS-only redirects, published checksum verification |
| The person's machine | A plugin folder containing hostile code | Out of scope today; see [Plugin trust](#plugin-trust) |

## Security of the dashboard

The host listens on `127.0.0.1` and an unprivileged port, default `47321`. If that port is taken, it tries the next 20 ports and writes the chosen port to `%LOCALAPPDATA%\Penelope\instance.json`.

A random session token is created in memory each time the host starts. The tray opens `http://127.0.0.1:<port>/session?token=...`. That response sets an `HttpOnly`, `SameSite=Strict` cookie and redirects to `/`. State-changing requests must carry that cookie and an `Origin` of the dashboard's own origin. Cross-origin browser calls are refused. There is no `Access-Control-Allow-Origin` for other sites.

This matters because a page on the public web can otherwise try to call a localhost server.

Implementation details (`Security/SessionGate.cs`):

- Every `/api/*` request, including `GET`, needs the cookie (`401` otherwise), so another site cannot read system readings or job logs either.
- A request whose `Host` header is not `127.0.0.1` is refused with `400`. This defeats DNS-rebinding attacks, where a hostile domain resolves to `127.0.0.1`.
- Non-API paths without a session return a static locked page that says to open Penelope from its shortcut. Only the favicons are served without a session.
- The token is compared in constant time and lives only in memory; restarting the host invalidates old links.

The Penelope dashboard never binds `0.0.0.0`. The LAN switch changes Ollama's listen address only. A test checks that the dashboard source stays on `127.0.0.1`.

## LAN exposure of Ollama

Full product behavior is in the [product specification](product-specification.md#network-exposure). Security-relevant rules:

- Default `OLLAMA_HOST=127.0.0.1:11434`.
- Turning it on requires a plain-language confirmation that says Ollama has no password.
- One inbound firewall rule, `Penelope Ollama LAN`: TCP on the Ollama port, **private profile only**, scoped to that `ollama.exe`.
- Turning it off deletes the rule and returns Ollama to `127.0.0.1`, even if the firewall prompt is refused.
- The page never shows a `netsh` command.

## Elevation

The host runs as the normal user. Windows asks for administrator rights only in two cases:

| Case | How | Owner |
| --- | --- | --- |
| Ollama installer needs admin | The official installer is started with the `runas` verb only when required | Ollama plugin (`OllamaSetup`) |
| Firewall rule add/remove | One elevated `cmd.exe /c netsh advfirewall ...` request via `runas`, with fixed arguments built from the rule name, port, and program path | Ollama plugin (`LanFirewall`) |

A refused prompt (Win32 error 1223) becomes a failure card with Try again and Open Windows Firewall.

The product specification planned a separate `Penelope.Elevation.exe` with a `requireAdministrator` manifest accepting only `firewall-add` and `firewall-remove`. That project was not built; the in-plugin `runas` call replaced it. The trade-off is recorded in [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md#deviations-from-the-product-specification). If antivirus or code-signing concerns grow, reintroducing a fixed-purpose helper remains the preferred hardening.

## Download integrity

Details in [`DOWNLOAD_MANAGER.md`](DOWNLOAD_MANAGER.md).

- Redirects must stay on `https://`.
- Installers are verified against the published checksum before they run. For Ollama, a published `sha256sum.txt` and the GitHub asset digest must agree.
- A mismatch keeps the file for inspection and does not run it.

## Opening URLs and Windows surfaces

- A failure-card `open-url` fix opens only `https://` addresses.
- `/api/host/open-url` opens only a fixed allow-list of GPU-vendor driver pages.
- Fixes never contain a command line.

## Plugin trust

A plugin assembly runs **in-process with full trust**. `AssemblyLoadContext` isolates dependency versions; it is not a security boundary. A plugin can do anything the signed-in user can do.

Consequences:

- Penelope ships only plugins it builds (`plugins/ollama`). The installer does not include the sample plugin.
- Dropping a folder into `plugins/` is equivalent to running a program. Documentation for plugin authors and users must say so.
- Plugin UI bundles run in the dashboard origin and can call any `/api/*` route with the person's session. Fault isolation for UI is described in [`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-safety); it is not a defense against hostile plugins.
- Third-party plugin distribution requires a separate decision (signing, sandboxing, permission prompts) recorded in an ADR before it is supported.

## Data at rest

- `%LOCALAPPDATA%\Penelope\config.json` — settings, no secrets.
- `%LOCALAPPDATA%\Penelope\logs\` — diagnostic log. Never log secrets, session tokens, or URLs with credentials ([`LOGGING.md`](LOGGING.md)).
- Chat transcripts stay in browser `sessionStorage`; the host does not store them.
- `HKCU\...\Run\Penelope` — written only when the person turns on "Start Penelope when I sign in".

There is no account and no telemetry. Agent-mode web search is the only outbound call made on the person's behalf beyond tool installers and release checks.
