export function ProgressBar({ completed, total, label }: { completed: number; total: number; label: string }) {
  const known = total > 0;
  const percent = known ? Math.max(0, Math.min(100, Math.round((completed / total) * 100))) : 0;
  return (
    <div
      className="progress-track"
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={known ? 100 : undefined}
      aria-valuenow={known ? percent : undefined}
      aria-valuetext={label}
    >
      <div className="progress-fill" style={{ width: known ? `${percent}%` : "40%" }} />
    </div>
  );
}
