import { Link } from "react-router-dom";
import { useModules } from "../app/modules";
import { EmptyState } from "../components/EmptyState";
import { Logo } from "../components/Logo";
import { logoStatusForModule } from "../components/logoStatus";

export function ToolsPage() {
  const { modules } = useModules();

  return (
    <>
      <h1>Tools</h1>
      <p className="muted">Each tool is a plugin. Adding one does not change Penelope itself.</p>
      {modules.length === 0 ? (
        <EmptyState title="No tools yet" detail="A plugin folder appears here after Penelope starts." />
      ) : (
        <div className="tool-list">
          {modules.map((module) => (
            <article className="card" key={module.id}>
              <div className="card-heading">
                <Logo status={logoStatusForModule(module)} size={28} />
                <h2>{module.name}</h2>
              </div>
              <p>{module.loadError ?? module.summary}</p>
              <p className="muted">{module.status?.headline ?? "Not ready"}</p>
              <Link to={`/tools/${module.id}`}>Open</Link>
            </article>
          ))}
        </div>
      )}
    </>
  );
}
