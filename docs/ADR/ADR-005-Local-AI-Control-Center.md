# ADR-005: Penelope is a local AI control center, not a chat product

- Status: Accepted
- Date: 2026-10-13 (records a decision made at project start)

## Context

People who want local AI on Windows face a terminal: installing runtimes, configuring servers, remembering model tags, managing ports, and checking GPU drivers. Separately, Odysseus already provides a full AI workspace (chat, agents, research, workflows). Penelope could either compete with such products or prepare the ground they run on.

The name comes from The Odyssey: Penelope maintained the household while Odysseus travelled.

## Decision

- Penelope is a **control center** for local AI tools: install, update, start, stop, monitor, and configure them from one browser page, with no terminal.
- It is tool-agnostic. Ollama is first; Docker, Odysseus, FlashML FreeToken, and others follow as plugins ([`ROADMAP.md`](../ROADMAP.md)).
- The Chat page exists only to check that a local model answers. Agent mode exists only to check that a model can use a tool. Neither grows into a general assistant.
- The full chat and agent product is Odysseus. Penelope installs and maintains it; it does not embed or replace it.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Full chat workspace inside Penelope | Duplicates Odysseus, multiplies scope (documents, prompts, history), and dilutes the beginner-installer focus. |
| Ollama-only manager | Simpler, but the plugin architecture costs little and the audience will want more than one tool. |
| No chat at all | Beginners need a quick way to confirm that the model they downloaded actually answers. |

## Consequences

- Feature requests for chat history, document upload, prompt libraries, or multi-step agents are out of scope and point to Odysseus.
- Chat transcripts stay in browser `sessionStorage`; the host stores no conversations.
- Agent tools are limited and register through `IAgentToolSource`; web search is the only shipped tool.
- Success is measured by "a beginner reaches a running model without a terminal", not by chat features.
