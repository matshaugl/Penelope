import { NavLink } from "react-router-dom";
import type { ToolModule } from "../modules/types";
import { Button } from "./Button";
import { Logo } from "./Logo";

export function Sidebar({
  modules,
  theme,
  startOnSignIn,
  onTheme,
  onStartOnSignIn,
  onQuit,
}: {
  modules: ToolModule[];
  theme: "dark" | "light";
  startOnSignIn: boolean;
  onTheme: () => void;
  onStartOnSignIn: (enabled: boolean) => Promise<void>;
  onQuit: () => void;
}) {
  const extra = modules.flatMap((module) =>
    module.nav.map((item) => ({ ...item, key: `${module.id}-${item.path}` })),
  );

  return (
    <nav className="sidebar" aria-label="Penelope">
      <div className="brand">
        <Logo size={32} />
        Penelope
      </div>
      <div className="nav">
        <NavLink to="/" end className={({ isActive }) => (isActive ? "active" : undefined)}>
          Home
        </NavLink>
        <NavLink to="/chat" className={({ isActive }) => (isActive ? "active" : undefined)}>
          Chat
        </NavLink>
        {extra.map((item) => (
          <NavLink key={item.key} to={item.path} className={({ isActive }) => (isActive ? "active" : undefined)}>
            {item.label}
          </NavLink>
        ))}
        <NavLink to="/system" className={({ isActive }) => (isActive ? "active" : undefined)}>
          This computer
        </NavLink>
        <NavLink to="/tools" end className={({ isActive }) => (isActive ? "active" : undefined)}>
          Tools
        </NavLink>
      </div>
      <div className="sidebar-footer">
        <label className="check">
          <input
            type="checkbox"
            checked={startOnSignIn}
            onChange={(event) => void onStartOnSignIn(event.target.checked)}
          />
          Start when I sign in
        </label>
        <Button variant="secondary" onClick={onTheme} aria-pressed={theme === "light"}>
          {theme === "dark" ? "Light theme" : "Dark theme"}
        </Button>
        <Button variant="danger" onClick={onQuit}>Quit</Button>
      </div>
    </nav>
  );
}
