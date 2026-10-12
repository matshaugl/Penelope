import { useEffect, useState } from "react";
import { Meter } from "../components/Meter";
import type { SystemSnapshot } from "../modules/types";

export function SystemPage() {
  const [system, setSystem] = useState<SystemSnapshot | null>(null);

  useEffect(() => {
    const source = new EventSource("/api/system/stream");
    const apply = (event: Event) => setSystem(JSON.parse((event as MessageEvent).data) as SystemSnapshot);
    source.addEventListener("system", apply);
    return () => source.close();
  }, []);

  return (
    <>
      <h1>This computer</h1>
      <p className="muted">These readings stay on this computer. Penelope does not invent a number when a source is missing.</p>
      {system && (
        <div className="meters">
          <Meter label="CPU" reading={system.cpu} />
          <Meter label="Memory" reading={system.memory} />
          {system.gpus.map((gpu, index) => (
            <Meter key={index} label={system.gpus.length > 1 ? `GPU ${index + 1}` : "GPU"} reading={gpu} />
          ))}
          {system.disk && <Meter label="Models drive" reading={system.disk} />}
        </div>
      )}
    </>
  );
}
