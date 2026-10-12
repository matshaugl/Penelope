import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import type { Settings, ToolModule } from "../modules/types";

interface ModulesState {
  modules: ToolModule[];
  refresh: () => Promise<void>;
}

const ModulesContext = createContext<ModulesState>({ modules: [], refresh: async () => {} });

export function ModulesProvider({ children }: { children: ReactNode }) {
  const [modules, setModules] = useState<ToolModule[]>([]);

  async function refresh() {
    const list = await api<ToolModule[]>("/api/modules");
    setModules(list);
  }

  useEffect(() => {
    let gone = false;
    const tick = () => {
      api<ToolModule[]>("/api/modules")
        .then((list) => {
          if (!gone) setModules(list);
        })
        .catch(() => {});
    };
    tick();
    const timer = window.setInterval(tick, 2000);
    return () => {
      gone = true;
      window.clearInterval(timer);
    };
  }, []);

  return <ModulesContext.Provider value={{ modules, refresh }}>{children}</ModulesContext.Provider>;
}

export function useModules() {
  return useContext(ModulesContext);
}

export function useSettings() {
  const [settings, setSettings] = useState<Settings | null>(null);

  async function reload() {
    const next = await api<Settings>("/api/settings");
    setSettings(next);
    return next;
  }

  useEffect(() => {
    void reload().catch(() => {});
  }, []);

  return { settings, reload };
}

export function useTheme() {
  const [theme, setTheme] = useState<"dark" | "light">(
    document.documentElement.classList.contains("light") ? "light" : "dark",
  );

  useEffect(() => {
    api<Settings>("/api/settings")
      .then((settings) => applyTheme(settings.theme === "light" ? "light" : "dark", setTheme))
      .catch(() => {});
  }, []);

  async function toggle() {
    const next = theme === "dark" ? "light" : "dark";
    applyTheme(next, setTheme);
    await api("/api/settings", { method: "PUT", body: JSON.stringify({ theme: next }) });
  }

  return { theme, toggle };
}

function applyTheme(theme: "dark" | "light", setTheme: (theme: "dark" | "light") => void) {
  document.documentElement.classList.toggle("light", theme === "light");
  localStorage.setItem("penelope-theme", theme);
  setTheme(theme);
}
