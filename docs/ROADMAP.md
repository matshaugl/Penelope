# Roadmap

High-level direction only. Task-level progress lives in [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md). Requirements and acceptance criteria live in the [product specification](product-specification.md). Why Penelope is a control center for many tools is in [ADR-005](ADR/ADR-005-Local-AI-Control-Center.md).

Each release after 0.1 adds one provider as a plugin folder. The host stays unchanged unless a release says otherwise. A release that needs a new plugin contract feature bumps `hostApi` minor ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#hostapi-versioning)).

## Release 0.1 — Ollama control center

Current functionality. Corresponds to product-specification phases 0–4 plus the model check.

- Per-user installer, tray, loopback-only browser dashboard, first-visit flow.
- Ollama as the first plugin: install, update, start, stop, version display.
- Models: curated catalog with fit warnings, pull, load, unload, remove.
- This computer: CPU, memory, GPU, VRAM, models disk.
- Ollama LAN switch with a private firewall rule; the dashboard stays local.
- Chat page to check a model; Agent mode with web search.
- Shared infrastructure: job runner, download manager, event bus, diagnostic log.
- Plugin contract `hostApi` 1.0.

Exit theme: a beginner reaches a running local model without a terminal, and the remaining clean-account acceptance checks pass.

## Release 0.2 — Docker provider

- A `docker` provider plugin: detect Docker Desktop, explain what is missing, start and stop the engine from the page.
- Proves the provider model on a tool Penelope does not install the same way as Ollama.
- Likely first users of `tool.error` and the `download.*` lifecycle events.
- Foundation for later providers that run as containers.

## Release 0.3 — Odysseus provider

- An `odysseus` provider plugin, after a spike that settles install method, process, port, version source, and how it points at Ollama.
- Odysseus remains the full chat product. Penelope installs, updates, starts, stops, and opens it; Penelope's Chat page stays a model check.
- First cross-tool relationship (Odysseus depends on a running Ollama), handled over HTTP, never by module references.

## Release 0.4 — FlashML FreeToken provider

- A `flashml-freetoken` provider plugin, after its own spike.
- Same `IToolModule` actions, including version and update. Anything that needs a person is a button.

## Cross-cutting themes

These are directions, not commitments to a release.

- **Trust and distribution** — signed builds, a public checksum for Penelope's installer, Penelope's own update check using the same job and failure-card patterns.
- **Resilience** — plugin UI error boundaries with fallback to the generic page ([`PLUGIN_SYSTEM.md`](PLUGIN_SYSTEM.md#plugin-ui-safety)), optional job history across restarts.
- **Agent tools** — more `IAgentToolSource` tools (diagnostics, tool actions) under the same "no terminal" and loopback rules.
- **Accessibility** — OpenDyslexic as an alternative font.

## Future possibilities

Candidates for additional provider plugins. None is scheduled.

- **OpenWebUI** — browser chat front end for Ollama.
- **ComfyUI** — node-based image generation.
- **llama.cpp** — lightweight server for GGUF models.
- **LocalAI** — OpenAI-compatible local server.
- Additional providers as the local AI ecosystem evolves.

Long-term platform options, each only after the Windows x64 path is dull and reliable: Windows on ARM, a winget package, and third-party plugin distribution (which first needs a plugin trust decision, see [`SECURITY.md`](SECURITY.md#plugin-trust)).

## Explicitly out of direction

Chat, prompts, documents, and agents as a product belong to Odysseus. Penelope will not grow into them. Accounts, telemetry, multi-user servers, and automatic GPU-driver installation are not planned.
