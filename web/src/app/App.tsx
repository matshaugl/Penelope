import { useState } from "react";
import { Outlet } from "react-router-dom";
import { api } from "./api";
import { ModulesProvider, useModules, useSettings, useTheme } from "./modules";
import { Logo } from "../components/Logo";
import { Sidebar } from "../components/Sidebar";
import { StatusHeader } from "../components/StatusHeader";
import { applicationLogoStatus, primaryService } from "../components/logoStatus";
import { WelcomePage } from "../pages/WelcomePage";

export function App() {
  return (
    <ModulesProvider>
      <Shell />
    </ModulesProvider>
  );
}

function Shell() {
  const { modules, refresh } = useModules();
  const { settings, reload } = useSettings();
  const { theme, toggle } = useTheme();
  const [busy, setBusy] = useState(false);
  const [quitting, setQuitting] = useState(false);
  const service = primaryService(modules);
  const status = applicationLogoStatus(modules);

  async function start() {
    if (!service) return;
    setBusy(true);
    try {
      await api(`/api/modules/${service.id}/start`, { method: "POST" });
      await refresh();
    } finally {
      setBusy(false);
    }
  }

  async function stop() {
    if (!service) return;
    setBusy(true);
    try {
      await api(`/api/modules/${service.id}/stop`, { method: "POST" });
      await refresh();
    } finally {
      setBusy(false);
    }
  }

  async function quit() {
    if (!quitting) {
      setQuitting(true);
      return;
    }
    try {
      await api("/api/host/quit", { method: "POST" });
    } catch {
      /* The host closes the connection as it stops. */
    }
  }

  return (
    <div className="shell">
      <Sidebar
        modules={modules}
        theme={theme}
        startOnSignIn={settings?.startOnSignIn ?? false}
        onTheme={() => void toggle()}
        onStartOnSignIn={async (enabled) => {
          await api("/api/settings", { method: "PUT", body: JSON.stringify({ startOnSignIn: enabled }) });
          await reload();
        }}
        onQuit={() => void quit()}
      />
      <div className="workspace">
        <StatusHeader module={service} status={status} busy={busy} onStart={() => void start()} onStop={() => void stop()} />
        <main className="content">
          <div className="content-inner">
            {quitting && (
              <div className="confirm">
                <p>Penelope will close. Tools that are already running keep running.</p>
                <div className="actions">
                  <button className="btn btn-danger" onClick={() => void quit()}>Quit Penelope</button>
                  <button className="btn btn-secondary" onClick={() => setQuitting(false)}>Stay</button>
                </div>
              </div>
            )}
            {settings === null ? (
              <div className="loading-mark">
                <Logo size={64} />
                <p className="muted">Loading…</p>
              </div>
            ) : settings.setupComplete ? (
              <Outlet />
            ) : (
              <WelcomePage
                startOnSignIn={settings.startOnSignIn}
                onStartOnSignIn={async (enabled) => {
                  await api("/api/settings", { method: "PUT", body: JSON.stringify({ startOnSignIn: enabled }) });
                  await reload();
                }}
                onDone={async () => {
                  await api("/api/settings", { method: "PUT", body: JSON.stringify({ setupComplete: true }) });
                  await reload();
                }}
              />
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
