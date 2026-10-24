import { useEffect, useState } from "react";
import { api } from "../app/api";

type LogEntry = { time: string; level: string; category: string; message: string };

export function DiagnosticsPage() {
  const [logs, setLogs] = useState<LogEntry[]>([]);

  useEffect(() => {
    let gone = false;
    const load = () => {
      api<LogEntry[]>("/api/diagnostics/logs?limit=200")
        .then((entries) => {
          if (!gone) setLogs(entries);
        })
        .catch(() => {});
    };
    load();
    const timer = window.setInterval(load, 3000);
    return () => {
      gone = true;
      window.clearInterval(timer);
    };
  }, []);

  return (
    <section className="page">
      <h1>Diagnostics</h1>
      <p>Recent Penelope log lines. The full log is also saved on this computer.</p>
      <div className="log" role="log">
        {logs.map((line, index) => (
          <div key={`${line.time}-${index}`} className={line.level}>
            [{line.level}] {line.category}: {line.message}
          </div>
        ))}
      </div>
    </section>
  );
}
