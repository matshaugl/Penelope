import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { useModules } from "../app/modules";
import { EmptyState } from "../components/EmptyState";
import { loadModuleUi } from "../modules/loadModuleUi";

export function PluginPage() {
  const location = useLocation();
  const { modules } = useModules();
  const host = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const match = modules.find((module) =>
    module.nav.some((item) => item.path === location.pathname),
  );

  useEffect(() => {
    if (!match?.hasCustomUi || !host.current) {
      return;
    }

    const element = host.current;
    let cancel = false;
    let unmount = () => {};
    setError(null);
    void loadModuleUi(`/plugins/${match.id}/ui/index.js`)
      .then((ui) => {
        if (cancel) return;
        unmount = ui.mount(element, { path: location.pathname });
      })
      .catch(() => {
        if (!cancel) setError("This page could not be opened.");
      });

    return () => {
      cancel = true;
      unmount();
    };
  }, [match?.id, match?.hasCustomUi, location.pathname]);

  if (!match) {
    return <EmptyState title="That page is not available." />;
  }

  if (!match.hasCustomUi) {
    return <EmptyState title={match.name} detail="This tool has no page for that section yet." />;
  }

  return (
    <>
      {error && <p className="muted">{error}</p>}
      <div ref={host} />
    </>
  );
}
