import { useEffect, useRef } from "react";
import type { Job } from "../modules/types";
import { Button } from "./Button";
import { ProgressBar } from "./ProgressBar";

export function JobPanel({ job, onCancel }: { job: Job; onCancel: () => void }) {
  const logRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = logRef.current;
    if (!el) return;
    const error = el.querySelector<HTMLElement>("[data-level='error']");
    if (job.state === "failed" && error) {
      error.scrollIntoView({ block: "nearest" });
    } else {
      el.scrollTop = el.scrollHeight;
    }
  }, [job]);

  const running = job.state === "queued" || job.state === "running";
  return (
    <section className="job" aria-label="Progress">
      <p>{job.message}</p>
      <ProgressBar completed={job.completed} total={job.total} label={job.message} />
      {running && job.cancelAllowed && (
        <div className="actions">
          <Button variant="secondary" onClick={onCancel}>Cancel</Button>
        </div>
      )}
      <div className="log" role="log" ref={logRef}>
        {job.log.map((line, index) => (
          <div key={`${line.time}-${index}`} className={line.level} data-level={line.level}>
            {line.text}
          </div>
        ))}
      </div>
    </section>
  );
}
