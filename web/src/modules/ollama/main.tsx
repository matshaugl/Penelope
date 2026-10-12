import { useEffect, useState } from "react";
import { createRoot, type Root } from "react-dom/client";

type Model = { name: string; size: number; inMemory: boolean };
type Entry = {
  name: string;
  tag: string;
  sizeBytes: number;
  summary: string;
  role: string;
  minRamGb: number;
  minVramGb: number;
};
type Failure = { summary: string; detail: string; fixes: { id: string; label: string; kind: string }[] };
type Job = {
  id: string;
  state: string;
  message: string;
  completed: number;
  total: number;
  cancelAllowed: boolean;
  log: { time: string; level: string; text: string }[];
  failure: Failure | null;
};

function bytes(value: number) {
  if (value >= 1_000_000_000) return `${(value / 1_000_000_000).toFixed(1)} GB`;
  if (value >= 1_000_000) return `${Math.round(value / 1_000_000)} MB`;
  return `${value} B`;
}

async function call<T>(path: string, method = "GET", body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const failure = (payload as { failure?: Failure }).failure;
    throw Object.assign(new Error(failure?.summary ?? "That didn't work."), { failure, status: response.status });
  }
  return payload as T;
}

function FailureView({ failure, jobId, onDone }: { failure: Failure; jobId?: string; onDone: (nextJobId?: string) => void }) {
  return (
    <section className="failure">
      <h2>{failure.summary}</h2>
      <p>{failure.detail}</p>
      {failure.fixes.length > 0 && (
        <div className="actions">
          {failure.fixes.map((fix) => (
            <button
              key={fix.id}
              className="btn btn-secondary"
              onClick={() => {
                const task = fix.kind === "open-disk"
                  ? call("/api/host/open-disk", "POST")
                  : jobId
                    ? call("/api/modules/ollama/fixes", "POST", { jobId, fixId: fix.id })
                    : Promise.resolve();
                void task.then((body) => onDone((body as { jobId?: string } | undefined)?.jobId));
              }}
            >
              {fix.label}
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

function JobView({ job, onCancel }: { job: Job; onCancel: () => void }) {
  const width = job.total > 0 ? Math.min(100, Math.round((job.completed / job.total) * 100)) : 0;
  return (
    <section className="job" aria-label="Progress">
      <p>{job.message}</p>
      <div className="progress-track" aria-hidden="true">
        <div className="progress-fill" style={{ width: `${width}%` }} />
      </div>
      {(job.state === "queued" || job.state === "running") && job.cancelAllowed && (
        <div className="actions">
          <button className="btn btn-secondary" onClick={onCancel}>Cancel</button>
        </div>
      )}
      <div className="log" role="log">
        {job.log.map((line, index) => (
          <div key={`${line.time}-${index}`} className={line.level}>{line.text}</div>
        ))}
      </div>
    </section>
  );
}

function useJob(jobId: string | null) {
  const [job, setJob] = useState<Job | null>(null);
  useEffect(() => {
    if (!jobId) return;
    setJob(null);
    const source = new EventSource(`/api/jobs/${jobId}/events`);
    const onJob = (event: Event) => setJob(JSON.parse((event as MessageEvent).data) as Job);
    source.addEventListener("job", onJob);
    return () => source.close();
  }, [jobId]);
  return job;
}

function Models({ onJob }: { onJob: (id: string | null) => void }) {
  const [models, setModels] = useState<Model[]>([]);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  async function refresh() {
    try {
      const body = await call<{ models: Model[] }>("/api/modules/ollama/models");
      setModels(body.models);
      setFailure(null);
    } catch (error) {
      setFailure((error as { failure?: Failure }).failure ?? null);
    }
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function act(name: string, action: "load" | "unload" | "remove") {
    setBusy(name);
    setFailure(null);
    try {
      await call(`/api/modules/ollama/models/${action}`, "POST", { name });
      await refresh();
    } catch (error) {
      setFailure((error as { failure?: Failure }).failure ?? null);
    } finally {
      setBusy(null);
      onJob(null);
    }
  }

  return (
    <>
      <h1>Models</h1>
      <p className="muted">Models on this computer. Start keeps one in memory. Stop unloads it.</p>
      {models.length === 0 && <p>No models are downloaded yet.</p>}
      {models.map((model) => (
        <article className="card" key={model.name} style={{ marginBottom: 12 }}>
          <h2>{model.name}</h2>
          <p className="muted">{bytes(model.size)} · {model.inMemory ? "In memory" : "On disk"}</p>
          <div className="actions">
            {model.inMemory ? (
              <button className="btn btn-secondary" disabled={busy === model.name} onClick={() => void act(model.name, "unload")}>Stop</button>
            ) : (
              <button className="btn btn-primary" disabled={busy === model.name} onClick={() => void act(model.name, "load")}>Start</button>
            )}
            <button className="btn btn-danger" disabled={busy === model.name} onClick={() => void act(model.name, "remove")}>Remove</button>
          </div>
        </article>
      ))}
      {failure && <FailureView failure={failure} onDone={() => void refresh()} />}
    </>
  );
}

function Catalog({ onJob }: { onJob: (id: string) => void }) {
  const [entries, setEntries] = useState<Entry[]>([]);
  const [note, setNote] = useState("");
  const [free, setFree] = useState<number | null>(null);
  const [other, setOther] = useState("");
  const [pending, setPending] = useState<string | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [ramBytes, setRamBytes] = useState<number | null>(null);
  const [vramBytes, setVramBytes] = useState<number | null>(null);

  useEffect(() => {
    void call<{ entries: Entry[]; note: string; freeBytes: number | null }>("/api/modules/ollama/catalog").then((body) => {
      setEntries(body.entries);
      setNote(body.note);
      setFree(body.freeBytes);
    });
    void call<{ memory: { available: boolean; capacityBytes?: number | null }; gpus: { available: boolean; capacityBytes?: number | null }[] }>("/api/system")
      .then((body) => {
        if (body.memory.available && body.memory.capacityBytes) setRamBytes(body.memory.capacityBytes);
        const video = body.gpus
          .filter((gpu) => gpu.available && gpu.capacityBytes)
          .map((gpu) => gpu.capacityBytes ?? 0);
        if (video.length > 0) setVramBytes(Math.max(...video));
      })
      .catch(() => {});
  }, []);

  async function pull(name: string) {
    setFailure(null);
    setPending(null);
    try {
      const body = await call<{ jobId: string }>("/api/modules/ollama/models/pull", "POST", { name });
      onJob(body.jobId);
    } catch (error) {
      setFailure((error as { failure?: Failure }).failure ?? null);
    }
  }

  return (
    <>
      <h1>Add a model</h1>
      <p className="muted">
        {ramBytes || vramBytes
          ? "Penelope is comparing these models with the memory and video memory on this computer."
          : note}
      </p>
      {free !== null && <p className="muted">{bytes(free)} free for models.</p>}
      {entries.map((entry) => (
        <article className="card" key={entry.tag} style={{ marginBottom: 12 }}>
          <h2>{entry.name}</h2>
          <p>{entry.summary}</p>
          <p className="muted">{entry.tag} · about {bytes(entry.sizeBytes)} · {entry.minRamGb} GB memory</p>
          {ramBytes !== null && ramBytes < entry.minRamGb * 1_073_741_824 && (
            <p>This computer has {bytes(ramBytes)} of memory. This model asks for {entry.minRamGb} GB.</p>
          )}
          {vramBytes !== null && vramBytes < entry.minVramGb * 1_073_741_824 && (
            <p>This computer reports {bytes(vramBytes)} of video memory. This model asks for {entry.minVramGb} GB.</p>
          )}
          <div className="actions">
            <button className="btn btn-primary" onClick={() => setPending(entry.tag)}>Download</button>
          </div>
        </article>
      ))}
      <h2>Other model</h2>
      <div className="actions">
        <input
          value={other}
          onChange={(event) => setOther(event.target.value)}
          placeholder="name:tag"
          aria-label="Other model"
          style={{ padding: 8, borderRadius: 6, border: "1px solid var(--border)", background: "var(--panel)", color: "var(--fg)" }}
        />
        <button className="btn btn-secondary" onClick={() => setPending(other.trim())}>Download</button>
      </div>
      {pending && (
        <div className="confirm">
          <p>Download {pending}? Closing Penelope stops the download.</p>
          <div className="actions">
            <button className="btn btn-primary" onClick={() => void pull(pending)}>Download {pending}</button>
            <button className="btn btn-secondary" onClick={() => setPending(null)}>Cancel</button>
          </div>
        </div>
      )}
      {failure && <FailureView failure={failure} onDone={() => setFailure(null)} />}
    </>
  );
}

type NetworkState = {
  exposeOnLan: boolean;
  port: number;
  running: boolean;
  shareable: string[];
  firewallRule: boolean;
  warning: string;
};

function Network({ onJob }: { onJob: (id: string) => void }) {
  const [state, setState] = useState<NetworkState | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [copied, setCopied] = useState("");

  function refresh() {
    return call<NetworkState>("/api/modules/ollama/network").then(setState).catch(() => {});
  }

  useEffect(() => {
    void refresh();
  }, []);

  async function change(exposeOnLan: boolean) {
    setFailure(null);
    setConfirm(false);
    try {
      const body = await call<{ jobId: string }>("/api/modules/ollama/network", "PUT", { exposeOnLan });
      onJob(body.jobId);
    } catch (error) {
      setFailure((error as { failure?: Failure }).failure ?? null);
    }
  }

  async function copy(url: string) {
    await navigator.clipboard.writeText(url);
    setCopied(url);
  }

  return (
    <>
      <h1>Network</h1>
      <p>{state?.warning ?? "Ollama has no password."}</p>
      <p className="muted">Penelope itself stays on this computer.</p>
      {state && (
        <p aria-live="polite">{state.exposeOnLan ? "Other devices on this network can reach Ollama." : "Only this computer can reach Ollama."}</p>
      )}
      {state?.shareable.map((url) => (
        <div className="actions" key={url}>
          <code>{url}</code>
          <button className="btn btn-secondary" onClick={() => void copy(url)}>{copied === url ? "Copied" : "Copy"}</button>
        </div>
      ))}
      {state && !state.exposeOnLan && state.firewallRule && (
        <p>A firewall rule may still allow the port. Ollama is listening only on this computer.</p>
      )}
      <div className="actions">
        {state?.exposeOnLan ? (
          <button className="btn btn-secondary" onClick={() => void change(false)}>Only this computer</button>
        ) : (
          <button className="btn btn-primary" onClick={() => setConfirm(true)}>Other devices on my network</button>
        )}
        {state && !state.exposeOnLan && state.firewallRule && (
          <button className="btn btn-secondary" onClick={() => void call("/api/host/open-firewall", "POST")}>Open Windows Firewall</button>
        )}
      </div>
      {confirm && (
        <div className="confirm">
          <p>Other devices will be able to use the models. Ollama has no password. Turn this off when you are finished. Ollama will restart.</p>
          <div className="actions">
            <button className="btn btn-primary" onClick={() => void change(true)}>Allow other devices</button>
            <button className="btn btn-secondary" onClick={() => setConfirm(false)}>Cancel</button>
          </div>
        </div>
      )}
      {failure && <FailureView failure={failure} onDone={() => void refresh()} />}
    </>
  );
}

const jobStorageKey = "penelope.ollama.job";

function Page({ path }: { path: string }) {
  const [jobId, setJobIdState] = useState<string | null>(() => sessionStorage.getItem(jobStorageKey));
  const job = useJob(jobId);

  function setJobId(id: string | null) {
    setJobIdState(id);
    if (id) sessionStorage.setItem(jobStorageKey, id);
    else sessionStorage.removeItem(jobStorageKey);
  }

  useEffect(() => {
    if (!jobId) return;
    void fetch(`/api/jobs/${jobId}`).then((response) => {
      if (response.status === 404) setJobId(null);
    });
  }, [jobId]);

  return (
    <>
      {path.endsWith("/catalog") ? <Catalog onJob={setJobId} /> : path.endsWith("/network") ? <Network onJob={setJobId} /> : <Models onJob={setJobId} />}
      {job && (
        <>
          <JobView job={job} onCancel={() => void call(`/api/jobs/${job.id}/cancel`, "POST")} />
          {job.failure && <FailureView failure={job.failure} jobId={job.id} onDone={(next) => { if (next) setJobId(next); }} />}
        </>
      )}
    </>
  );
}

let root: Root | null = null;

export function mount(element: HTMLElement, props: { path: string }) {
  root?.unmount();
  root = createRoot(element);
  root.render(<Page path={props.path} />);
  return () => {
    root?.unmount();
    root = null;
  };
}
