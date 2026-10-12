import { Link } from "react-router-dom";
import { useEffect, useState } from "react";
import { api } from "../app/api";
import { useModules } from "../app/modules";
import { Button } from "../components/Button";
import { Meter } from "../components/Meter";
import type { SystemSnapshot } from "../modules/types";

const empty: SystemSnapshot = {
  cpu: { available: false, reason: "Reading the computer…", percent: null, detail: null },
  memory: { available: false, reason: "Reading the computer…", percent: null, detail: null },
  gpus: [],
};

export function HomePage() {
  const { modules, refresh } = useModules();
  const [system, setSystem] = useState<SystemSnapshot>(empty);
  const [loaded, setLoaded] = useState<string[]>([]);
  const ollama = modules.find((module) => module.id === "ollama");

  useEffect(() => {
    const source = new EventSource("/api/system/stream");
    const apply = (event: Event) => setSystem(JSON.parse((event as MessageEvent).data) as SystemSnapshot);
    source.addEventListener("system", apply);
    return () => source.close();
  }, []);

  useEffect(() => {
    if (ollama?.status?.state !== "running") {
      setLoaded([]);
      return;
    }
    api<{ models: { name: string; inMemory: boolean }[] }>("/api/modules/ollama/models")
      .then((body) => setLoaded(body.models.filter((model) => model.inMemory).map((model) => model.name)))
      .catch(() => setLoaded([]));
  }, [ollama?.status?.state]);

  const gpu = system.gpus[0] ?? {
    available: false,
    reason: "No graphics reading yet.",
    percent: null,
    detail: null,
  };

  return (
    <>
      <h1>Penelope</h1>
      <p>Penelope installs and runs local AI tools from this page.</p>
      {modules.length === 0 ? (
        <p className="muted">No tools are installed yet.</p>
      ) : (
        <p>
          <Link to="/tools">{modules.length === 1 ? "One tool is available." : `${modules.length} tools are available.`}</Link>
        </p>
      )}
      <HomeAction ollama={ollama} loaded={loaded} onStart={async () => {
        await api("/api/modules/ollama/start", { method: "POST" });
        await refresh();
      }} />
      {loaded.length > 0 && (
        <>
          <h2>Models in memory</h2>
          <ul className="steps">
            {loaded.map((name) => <li key={name}>{name}</li>)}
          </ul>
        </>
      )}
      <div className="meters">
        <Meter label="CPU" reading={system.cpu} />
        <Meter label="Memory" reading={system.memory} />
        <Meter label="GPU" reading={gpu} />
      </div>
    </>
  );
}

function HomeAction({
  ollama,
  loaded,
  onStart,
}: {
  ollama: { status?: { state: string } | null; release?: { updateAvailable: boolean } | null } | undefined;
  loaded: string[];
  onStart: () => Promise<void>;
}) {
  const state = ollama?.status?.state;
  if (!ollama || state === "not-installed") {
    return <p><Link className="btn btn-primary" to="/tools/ollama">Install Ollama</Link></p>;
  }
  if (ollama.release?.updateAvailable) {
    return <p><Link className="btn btn-primary" to="/tools/ollama">Update Ollama</Link></p>;
  }
  if (state === "stopped") {
    return <p><Button variant="primary" onClick={() => void onStart()}>Start Ollama</Button></p>;
  }
  if (state === "running" && loaded.length === 0) {
    return <p><Link className="btn btn-primary" to="/ollama/catalog">Add a model</Link></p>;
  }
  return null;
}
