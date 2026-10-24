# ADR-002: The dashboard is loopback-only

- Status: Accepted
- Date: 2026-10-13 (records a decision made at project start)

## Context

The dashboard can install software, start and stop processes, change environment variables, request firewall changes, and quit the host. It serves people who are not security experts, on home networks, with no account system. Separately, the product must let a person share Ollama's model API with other devices on their network.

A localhost server is also reachable from any web page the person visits, through their browser, so binding to loopback alone is not enough.

## Decision

- The Penelope dashboard binds `127.0.0.1` only, never `0.0.0.0` or a LAN address, and there is no setting to change that.
- The LAN switch changes **Ollama's** listen address only.
- Every request is gated: a per-launch random session token delivered by the tray link becomes an `HttpOnly`, `SameSite=Strict` cookie; state-changing requests also need a matching `Origin`; the `Host` header must be `127.0.0.1`; no CORS headers are ever sent.

Details: [`SECURITY.md`](../SECURITY.md).

## Alternatives considered

| Option | Why not |
| --- | --- |
| Bind all interfaces with a password | Requires accounts or password management for beginners, and exposes an install-and-execute surface to the LAN. |
| Optional "remote dashboard" setting | One checkbox away from the above; a beginner cannot judge the risk. |
| Loopback without a session token | Any public website could drive the dashboard through the person's browser (CSRF / DNS rebinding). |
| Native window instead of a browser | Avoids the browser threat model but contradicts [ADR-004](ADR-004-React-Frontend.md). |

## Consequences

- Managing Penelope from a phone or another PC is not possible. Accepted.
- The tray link (or console link in development) is the only way in; opening the bare URL shows a locked page by design.
- Tests must keep proving that the dashboard stays on `127.0.0.1` after the LAN switch is turned on, and that cross-origin calls are refused.
- Any future remote-management feature needs a new ADR that supersedes this one.
