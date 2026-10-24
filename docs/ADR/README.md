# Architecture decision records

An ADR records **why** a significant decision was made, so future maintainers and AI agents do not reverse it by accident or re-litigate it without new information.

| ADR | Title | Status |
| --- | --- | --- |
| [ADR-001](ADR-001-Plugin-System.md) | Tools are folder-discovered plugins | Accepted |
| [ADR-002](ADR-002-Loopback-Only-Dashboard.md) | The dashboard is loopback-only | Accepted |
| [ADR-003](ADR-003-DotNet-Host.md) | The host is a self-contained .NET 10 app | Accepted |
| [ADR-004](ADR-004-React-Frontend.md) | The interface is React in the person's own browser | Accepted |
| [ADR-005](ADR-005-Local-AI-Control-Center.md) | Penelope is a local AI control center, not a chat product | Accepted |

## When to write one

- Reversing or weakening any rule listed in [`docs/README.md`](../README.md#non-negotiable-rules).
- Changing a dependency rule in [`ARCHITECTURE.md`](../ARCHITECTURE.md#dependency-rules).
- A major `hostApi` bump, or supporting third-party plugins.
- Choosing a new runtime, framework, or process model.

## Rules

- Numbered sequentially, never reused. File name `ADR-NNN-Short-Title.md`.
- An accepted ADR is not edited for substance. To change a decision, write a new ADR that supersedes it and set the old one's status to `Superseded by ADR-NNN`.
- Keep each ADR short. Link to the canonical document for details instead of copying them.

## Template

```markdown
# ADR-NNN: Title

- Status: Proposed | Accepted | Superseded by ADR-NNN
- Date: YYYY-MM-DD

## Context
What forces are at play? What problem needs a decision?

## Decision
What we decided, stated plainly.

## Alternatives considered
Each option and why it was not chosen.

## Consequences
What becomes easier, what becomes harder, and what we must now keep true.
```
