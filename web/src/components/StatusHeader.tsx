import type { ToolModule } from "../modules/types";
import { Button } from "./Button";
import { Logo } from "./Logo";
import type { LogoStatus } from "./logoStatus";

export function StatusHeader({
  module,
  status,
  busy,
  onStart,
  onStop,
}: {
  module: ToolModule | null;
  status: LogoStatus;
  busy: boolean;
  onStart: () => void;
  onStop: () => void;
}) {
  const state = module?.status?.state;
  const headline = state === "running" || state === "stopped" || state === "starting" || state === "stopping"
    ? module?.status?.headline ?? "Penelope is running"
    : "Penelope is running";
  const release = module?.release;
  const version = release?.installedVersion
    ? `${module?.name} ${release.installedVersion}${release.updateAvailable ? " · Update available" : ""}`
    : null;

  return (
    <header className="header">
      <div className="header-brand">
        <Logo status={status} size={40} />
        <div>
          <p aria-live="polite">{headline}</p>
          {version && <p className="version-line">{version}</p>}
        </div>
      </div>
      {state === "running" || state === "stopping" ? (
        <Button variant="secondary" onClick={onStop} disabled={busy}>Stop</Button>
      ) : state === "stopped" || state === "starting" ? (
        <Button variant="primary" onClick={onStart} disabled={busy}>Start</Button>
      ) : null}
    </header>
  );
}
