import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { api, ApiError } from "../app/api";
import { useModules } from "../app/modules";
import { Button } from "../components/Button";
import { Logo } from "../components/Logo";
import { logoStatusForModule } from "../components/logoStatus";
import { EmptyState } from "../components/EmptyState";
import { FailureCard } from "../components/FailureCard";
import { JobPanel } from "../components/JobPanel";
import type { FailureCard as FailureModel, FailureFix, Job, ToolModule } from "../modules/types";

export function GenericModulePage() {
  const { id = "" } = useParams();
  const { modules, refresh } = useModules();
  const [module, setModule] = useState<ToolModule | null>(null);
  const [missing, setMissing] = useState(false);
  const [job, setJob] = useState<Job | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [actionFailure, setActionFailure] = useState<FailureModel | null>(null);
  const [confirmUpdate, setConfirmUpdate] = useState(false);
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const next = await api<ToolModule>(`/api/modules/${id}`);
      setModule(next);
      setMissing(false);
      setJobId((current) => current ?? next.activeJobId ?? next.latestJobId);
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setMissing(true);
      }
    }
  }

  useEffect(() => {
    setJob(null);
    setJobId(null);
    setActionFailure(null);
    void load();
    // Reload when the route id changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  useEffect(() => {
    const listed = modules.find((item) => item.id === id);
    if (!listed) return;
    setModule((current) => current ? {
      ...current,
      status: listed.status,
      release: listed.release,
      availability: listed.availability,
      loadError: listed.loadError,
      summary: listed.summary,
    } : current);
  }, [modules, id]);

  useEffect(() => {
    if (!jobId) return;
    const source = new EventSource(`/api/jobs/${jobId}/events`);
    const onJob = (event: Event) => {
      const next = JSON.parse((event as MessageEvent).data) as Job;
      setJob(next);
      if (next.state === "completed" || next.state === "failed" || next.state === "cancelled") {
        void refresh();
        void load();
      }
    };
    source.addEventListener("job", onJob);
    return () => source.close();
    // load is recreated each render; the subscription only depends on the job id.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobId]);

  async function begin(kind: "install" | "upgrade") {
    setBusy(true);
    setActionFailure(null);
    setConfirmUpdate(false);
    try {
      const result = await api<{ jobId: string }>(`/api/modules/${id}/${kind}`, { method: "POST" });
      setJobId(result.jobId);
      setJob(null);
    } catch (error) {
      const failure = readFailure(error);
      if (failure) setActionFailure(failure);
    } finally {
      setBusy(false);
    }
  }

  async function run(action: "start" | "stop") {
    setBusy(true);
    setActionFailure(null);
    try {
      await api(`/api/modules/${id}/${action}`, { method: "POST" });
      await refresh();
      await load();
    } catch (error) {
      const failure = readFailure(error);
      if (failure) setActionFailure(failure);
    } finally {
      setBusy(false);
    }
  }

  async function onFix(fix: FailureFix) {
    if (!job) return;
    setBusy(true);
    try {
      const result = await api<{ jobId?: string }>(`/api/modules/${id}/fixes`, {
        method: "POST",
        body: JSON.stringify({ jobId: job.id, fixId: fix.id }),
      });
      if (result.jobId) {
        setJobId(result.jobId);
        setJob(null);
        setActionFailure(null);
      }
    } finally {
      setBusy(false);
      await refresh();
      await load();
    }
  }

  if (missing) {
    return <EmptyState title="That tool is not available." />;
  }

  if (!module) {
    return (
      <div className="loading-mark">
        <Logo size={48} />
        <p className="muted">Loading…</p>
      </div>
    );
  }

  const runningJob = job && (job.state === "pending" || job.state === "running");
  const installed = Boolean(module.release?.installedVersion);
  const failure = job?.failure ?? actionFailure;

  return (
    <>
      <div className="page-heading">
        <Logo status={logoStatusForModule(module)} size={40} />
        <h1>{module.name}</h1>
      </div>
      <p>{module.loadError ?? module.summary}</p>
      <p aria-live="polite">{module.status?.headline ?? "Not ready"}</p>
      <p className="version-line">
        {module.release?.installedVersion
          ? `${module.release.installedVersion} installed`
          : "Not installed"}
        {module.release?.latestVersion ? ` · latest ${module.release.latestVersion}` : ""}
        {module.release?.updateAvailable ? " · Update available" : ""}
        {module.release?.checkError ? ` · ${module.release.checkError}` : ""}
      </p>
      <div className="actions">
        {!installed && (
          <Button variant="primary" disabled={busy || Boolean(runningJob)} onClick={() => void begin("install")}>
            Install {module.name}
          </Button>
        )}
        {module.release?.updateAvailable && (
          <Button variant="primary" disabled={busy || Boolean(runningJob)} onClick={() => setConfirmUpdate(true)}>
            Update {module.name}
          </Button>
        )}
        {installed && module.status?.state !== "running" && (
          <Button variant="secondary" disabled={busy || Boolean(runningJob)} onClick={() => void run("start")}>
            Start
          </Button>
        )}
        {module.status?.state === "running" && (
          <Button variant="secondary" disabled={busy || Boolean(runningJob)} onClick={() => void run("stop")}>
            Stop
          </Button>
        )}
        <Button
          variant="secondary"
          disabled={busy}
          onClick={() => void api(`/api/modules/${id}/check`, { method: "POST" }).then(() => load())}
        >
          Check for updates
        </Button>
      </div>
      {confirmUpdate && (
        <div className="confirm">
          <p>
            Update {module.name} from {module.release?.installedVersion} to {module.release?.latestVersion}? Penelope will not update it unless you confirm.
          </p>
          <div className="actions">
            <Button variant="primary" onClick={() => void begin("upgrade")}>Update {module.name}</Button>
            <Button variant="secondary" onClick={() => setConfirmUpdate(false)}>Cancel</Button>
          </div>
        </div>
      )}
      {module.plan && !job && (
        <>
          <h2>What install will do</h2>
          <ol className="steps">
            {module.plan.steps.map((step) => (
              <li key={step.label}><strong>{step.label}.</strong> {step.detail}</li>
            ))}
          </ol>
        </>
      )}
      {failure && <FailureCard failure={failure} onFix={(fix) => void onFix(fix)} />}
      {job && (
        <JobPanel
          job={job}
          onCancel={() => void api(`/api/jobs/${job.id}/cancel`, { method: "POST" })}
        />
      )}
    </>
  );
}

function readFailure(error: unknown): FailureModel | null {
  if (!(error instanceof ApiError) || !error.body || typeof error.body !== "object") {
    return null;
  }
  const failure = (error.body as { failure?: FailureModel }).failure;
  return failure?.summary ? failure : null;
}
