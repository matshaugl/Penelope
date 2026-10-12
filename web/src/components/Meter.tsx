import { api } from "../app/api";
import type { Reading } from "../modules/types";

export function Meter({ label, reading }: { label: string; reading: Reading }) {
  const value = reading.available && reading.percent != null
    ? `${Math.round(reading.percent)}%`
    : "Unavailable";
  return (
    <div className="meter">
      <span>{label}</span>
      <strong>{value}</strong>
      <p className="muted">{reading.available ? reading.detail : reading.reason}</p>
      {reading.actionUrl && reading.actionLabel && (
        <button
          className="btn btn-secondary"
          type="button"
          onClick={() => void api("/api/host/open-url", { method: "POST", body: JSON.stringify({ url: reading.actionUrl }) })}
        >
          {reading.actionLabel}
        </button>
      )}
    </div>
  );
}
