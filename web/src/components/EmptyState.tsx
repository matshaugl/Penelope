import { Logo } from "./Logo";

export function EmptyState({ title, detail }: { title: string; detail?: string }) {
  return (
    <div className="card empty-state">
      <Logo size={40} />
      <h2>{title}</h2>
      {detail && <p className="muted">{detail}</p>}
    </div>
  );
}
