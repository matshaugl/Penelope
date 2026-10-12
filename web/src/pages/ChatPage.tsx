import { useEffect, useRef, useState } from "react";
import { api } from "../app/api";
import { useModules } from "../app/modules";
import { readServerEvents } from "../app/stream";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";

type Mode = "chat" | "agent";

type Turn = {
  role: "user" | "assistant";
  content: string;
  model?: string;
};

type ChatSession = {
  ready: boolean;
  provider: string | null;
  model: string | null;
  models: string[];
  reason: string | null;
  tools: string[];
};

const storageKey = "penelope.chat";

export function ChatPage() {
  const { modules } = useModules();
  const ollamaState = modules.find((module) => module.id === "ollama")?.status?.state;
  const stored = useRef(load()).current;
  const [session, setSession] = useState<ChatSession | null>(null);
  const [mode, setMode] = useState<Mode>(stored.mode);
  const [model, setModel] = useState(stored.model);
  const [messages, setMessages] = useState<Turn[]>(stored.messages);
  const [draft, setDraft] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const transcript = useRef<HTMLDivElement>(null);
  const abort = useRef<AbortController | null>(null);

  useEffect(() => {
    let gone = false;
    api<ChatSession>("/api/chat/session")
      .then((next) => {
        if (!gone) setSession(next);
      })
      .catch(() => {
        if (!gone) {
          setSession({
            ready: false,
            provider: null,
            model: null,
            models: [],
            reason: "The model list didn't load.",
            tools: [],
          });
        }
      });
    return () => {
      gone = true;
    };
  }, [ollamaState]);

  useEffect(() => {
    if (!session?.ready) {
      return;
    }

    setModel((current) => (current && session.models.includes(current) ? current : session.model ?? ""));
  }, [session]);

  useEffect(() => {
    sessionStorage.setItem(storageKey, JSON.stringify({ mode, model, messages }));
  }, [mode, model, messages]);

  useEffect(() => {
    const node = transcript.current;
    if (node) {
      node.scrollTop = node.scrollHeight;
    }
  }, [messages, status]);

  const activeModel = model || session?.model || "";
  const modeLabel = mode === "chat" ? "Chat" : "Agent";

  async function send() {
    const text = draft.trim();
    if (!text || streaming || !session?.ready || !activeModel) {
      return;
    }

    const history = [...messages, { role: "user" as const, content: text }];
    setMessages([...history, { role: "assistant", content: "", model: activeModel }]);
    setDraft("");
    setError(null);
    setStatus(null);
    setStreaming(true);
    const controller = new AbortController();
    abort.current = controller;

    try {
      const response = await fetch(mode === "agent" ? "/api/agent/stream" : "/api/chat/stream", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        signal: controller.signal,
        body: JSON.stringify({
          model: activeModel,
          messages: history,
        }),
      });
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(body?.message || "The model didn't answer.");
      }

      await readServerEvents(response, (name, data) => {
        const payload = JSON.parse(data) as { text?: string | null };
        if (name === "token" && payload.text) {
          setMessages((current) => {
            const next = current.slice();
            const last = next[next.length - 1];
            if (last?.role === "assistant") {
              next[next.length - 1] = { ...last, content: last.content + payload.text };
            }
            return next;
          });
        } else if (name === "status") {
          setStatus(payload.text ?? null);
        } else if (name === "error") {
          setError(payload.text || "The model didn't answer.");
        }
      });
    } catch (caught) {
      if (!(caught instanceof DOMException && caught.name === "AbortError")) {
        setError(caught instanceof Error ? caught.message : "The model didn't answer.");
      }
    } finally {
      setStreaming(false);
      setStatus(null);
      abort.current = null;
      setMessages((current) => current.filter((turn, index) => turn.content.length > 0 || index < current.length - 1 || turn.role === "user"));
    }
  }

  function stop() {
    abort.current?.abort();
  }

  function clear() {
    setMessages([]);
    setError(null);
    setStatus(null);
  }

  return (
    <div className="chat-page">
      <div className="page-heading">
        <h1>Chat</h1>
      </div>
      <p>Send a short message to check that this model answers.</p>
      <div className="chat-bar">
        <div className="segment" role="group" aria-label="Mode">
          <button type="button" aria-pressed={mode === "chat"} onClick={() => setMode("chat")}>Chat</button>
          <button type="button" aria-pressed={mode === "agent"} onClick={() => setMode("agent")}>Agent</button>
        </div>
        <label className="model-pick">
          Model
          <select
            aria-label="Model"
            value={activeModel}
            disabled={!session?.ready || streaming || (session?.models.length ?? 0) === 0}
            onChange={(event) => setModel(event.target.value)}
          >
            {activeModel && !(session?.models ?? []).includes(activeModel) && <option value={activeModel}>{activeModel}</option>}
            {(session?.models ?? []).map((name) => <option key={name} value={name}>{name}</option>)}
            {!activeModel && <option value="">No model</option>}
          </select>
        </label>
        <Button variant="secondary" onClick={clear} disabled={streaming || messages.length === 0}>Clear</Button>
      </div>
      <p className="version-line">
        {modeLabel} mode
        {session?.provider ? ` · ${session.provider}` : ""}
        {activeModel ? ` · ${activeModel}` : ""}
      </p>
      <p className="muted">
        {mode === "chat"
          ? "The model answers on its own."
          : "The model can search the web. It cannot change this computer."}
      </p>
      {session && !session.ready && <EmptyState title={session.reason ?? "A model isn't ready."} />}
      <div className="transcript" ref={transcript} role="log" aria-live="polite" aria-relevant="additions">
        {messages.map((turn, index) => (
          <div key={`${turn.role}-${index}`} className={`bubble ${turn.role}`}>
            <div className="who">{turn.role === "user" ? "You" : turn.model || activeModel || "Model"}</div>
            <div>
              {turn.content
                || (streaming && index === messages.length - 1 ? status || "…" : "")}
            </div>
          </div>
        ))}
      </div>
      {error && <p className="chat-error">{error}</p>}
      <div className="composer">
        <textarea
          aria-label="Message"
          placeholder="Ask the model something"
          value={draft}
          rows={2}
          disabled={!session?.ready || streaming}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              void send();
            }
          }}
        />
        <Button variant="primary" disabled={!session?.ready || streaming || draft.trim().length === 0} onClick={() => void send()}>
          Send
        </Button>
        {streaming && <Button variant="secondary" onClick={stop}>Stop</Button>}
      </div>
    </div>
  );
}

function load(): { mode: Mode; model: string; messages: Turn[] } {
  try {
    const raw = sessionStorage.getItem(storageKey);
    if (!raw) {
      return { mode: "chat", model: "", messages: [] };
    }

    const parsed = JSON.parse(raw) as { mode?: string; model?: string; messages?: Turn[] };
    const messages = Array.isArray(parsed.messages)
      ? parsed.messages.filter((turn) =>
        (turn.role === "user" || turn.role === "assistant") && typeof turn.content === "string" && turn.content.length > 0)
        .map((turn) => ({
          role: turn.role,
          content: turn.content,
          model: typeof turn.model === "string" ? turn.model : undefined,
        }))
      : [];
    return {
      mode: parsed.mode === "agent" ? "agent" : "chat",
      model: typeof parsed.model === "string" ? parsed.model : "",
      messages,
    };
  } catch {
    return { mode: "chat", model: "", messages: [] };
  }
}
