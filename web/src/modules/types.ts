export type ToolAvailability = "ready" | "planned" | "missing" | "error";

export type ToolRunState =
  | "not-installed"
  | "installing"
  | "stopped"
  | "starting"
  | "running"
  | "stopping";

export type FixKind = "retry" | "check-again" | "open-url" | "open-firewall" | "open-disk";

export interface ModuleNavItem {
  label: string;
  path: string;
}

export interface ToolStatus {
  state: ToolRunState;
  headline: string;
}

export interface ToolRelease {
  installedVersion: string | null;
  latestVersion: string | null;
  checkedAt: string | null;
  updateAvailable: boolean;
  checkError: string | null;
}

export interface FailureFix {
  id: string;
  label: string;
  kind: FixKind;
  url: string | null;
}

export interface FailureCard {
  summary: string;
  detail: string;
  fixes: FailureFix[];
}

export interface InstallStep {
  label: string;
  detail: string;
}

export interface ToolModule {
  id: string;
  name: string;
  summary: string;
  availability: ToolAvailability;
  loadError: string | null;
  hasCustomUi: boolean;
  nav: ModuleNavItem[];
  status: ToolStatus | null;
  release: ToolRelease | null;
  plan: { steps: InstallStep[] } | null;
  activeJobId: string | null;
  latestJobId: string | null;
}

export interface JobLogLine {
  time: string;
  level: "info" | "warning" | "error";
  text: string;
}

export interface Job {
  id: string;
  moduleId: string;
  kind: string;
  state: "pending" | "running" | "completed" | "failed" | "cancelled";
  message: string;
  completed: number;
  total: number;
  cancelAllowed: boolean;
  log: JobLogLine[];
  failure: FailureCard | null;
}

export interface Reading {
  available: boolean;
  reason: string | null;
  percent: number | null;
  detail: string | null;
  capacityBytes?: number | null;
  actionLabel?: string | null;
  actionUrl?: string | null;
}

export interface SystemSnapshot {
  cpu: Reading;
  memory: Reading;
  gpus: Reading[];
  disk?: Reading;
}

export interface Settings {
  theme: "dark" | "light";
  openBrowserOnLaunch: boolean;
  setupComplete: boolean;
  startOnSignIn: boolean;
}
