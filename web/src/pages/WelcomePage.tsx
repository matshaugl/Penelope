import { useEffect, useState } from "react";
import { api } from "../app/api";
import { useModules } from "../app/modules";
import { Button } from "../components/Button";
import { Logo } from "../components/Logo";
import { JobPanel } from "../components/JobPanel";
import type { Job, ToolModule } from "../modules/types";

type CatalogEntry = { name: string; tag: string; summary: string; role: string };

export function WelcomePage({
  startOnSignIn,
  onStartOnSignIn,
  onDone,
}: {
  startOnSignIn: boolean;
  onStartOnSignIn: (enabled: boolean) => Promise<void>;
  onDone: () => Promise<void>;
}) {
  const { modules } = useModules();
  const [step, setStep] = useState(0);
  const [detail, setDetail] = useState<ToolModule | null>(null);
  const [starter, setStarter] = useState<CatalogEntry | null>(null);
  const [job, setJob] = useState<Job | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const ollama = modules.find((module) => module.id === "ollama");
  const installed = ollama?.status?.state !== "not-installed" && ollama?.status?.state !== undefined;

  useEffect(() => {
    if (step !== 1) return;
    api<ToolModule>("/api/modules/ollama").then(setDetail).catch(() => {});
  }, [step]);

  useEffect(() => {
    if (step !== 2) return;
    api<{ entries: CatalogEntry[] }>("/api/modules/ollama/catalog")
      .then((catalog) => setStarter(catalog.entries.find((entry) => entry.role === "starter-tiny") ?? catalog.entries[0] ?? null))
      .catch(() => {});
  }, [step]);

  useEffect(() => {
    if (!jobId) return;
    const source = new EventSource(`/api/jobs/${jobId}/events`);
    const onJob = (event: Event) => setJob(JSON.parse((event as MessageEvent).data) as Job);
    source.addEventListener("job", onJob);
    return () => source.close();
  }, [jobId]);

  async function install() {
    setBusy(true);
    try {
      const result = await api<{ jobId: string }>("/api/modules/ollama/install", { method: "POST" });
      setJobId(result.jobId);
      setJob(null);
    } finally {
      setBusy(false);
    }
  }

  async function downloadStarter() {
    if (!starter) return;
    setBusy(true);
    try {
      const result = await api<{ jobId: string }>("/api/modules/ollama/models/pull", {
        method: "POST",
        body: JSON.stringify({ name: starter.tag }),
      });
      setJobId(result.jobId);
      setJob(null);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-label="Welcome">
      <div className="welcome-mark">
        <Logo size={64} />
      </div>
      <p className="muted">Step {step + 1} of 3</p>
      {step === 0 && (
        <>
          <h1>Welcome</h1>
          <p>Penelope installs and runs local models on this computer.</p>
          <label className="check">
            <input
              type="checkbox"
              checked={startOnSignIn}
              onChange={(event) => void onStartOnSignIn(event.target.checked)}
            />
            Start Penelope when I sign in
          </label>
          <div className="actions">
            <Button variant="primary" onClick={() => setStep(1)}>Continue</Button>
          </div>
        </>
      )}
      {step === 1 && (
        <>
          <h1>{installed ? "Ollama is already installed" : "Install Ollama"}</h1>
          {installed ? (
            <p>Penelope will use the Ollama already on this computer. It will not download another copy.</p>
          ) : (
            <ol className="steps">
              {(detail?.plan?.steps ?? []).map((item) => (
                <li key={item.label}><strong>{item.label}.</strong> {item.detail}</li>
              ))}
            </ol>
          )}
          {job && <JobPanel job={job} onCancel={() => void api(`/api/jobs/${job.id}/cancel`, { method: "POST" })} />}
          <div className="actions">
            {!installed && (
              <Button variant="primary" disabled={busy} onClick={() => void install()}>Install Ollama</Button>
            )}
            <Button variant={installed ? "primary" : "secondary"} onClick={() => setStep(2)}>Continue</Button>
          </div>
        </>
      )}
      {step === 2 && (
        <>
          <h1>Add a starter model</h1>
          {starter ? (
            <>
              <p><strong>{starter.name}.</strong> {starter.summary}</p>
              <p className="muted">{starter.tag}</p>
            </>
          ) : (
            <p>You can add a model from the catalog after this.</p>
          )}
          {job && <JobPanel job={job} onCancel={() => void api(`/api/jobs/${job.id}/cancel`, { method: "POST" })} />}
          <div className="actions">
            {starter && ollama?.status?.state === "running" && (
              <Button variant="primary" disabled={busy} onClick={() => void downloadStarter()}>Download {starter.name}</Button>
            )}
            <Button variant="secondary" onClick={() => void onDone()}>Skip</Button>
          </div>
        </>
      )}
    </section>
  );
}
