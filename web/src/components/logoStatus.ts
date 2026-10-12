import mark from "../../../branding/penelope.svg?raw";
import type { ToolModule, ToolRunState } from "../modules/types";

const brand = mark.trim();

export type LogoStatus = "running" | "busy" | "stopped";

export const logoColors: Record<LogoStatus, string> = {
  running: "#22c55e",
  busy: "#eab308",
  stopped: "#ef4444",
};

const actionable = new Set<ToolRunState>(["running", "stopped", "starting", "stopping", "installing"]);

export function logoStatusForModule(module: ToolModule): LogoStatus {
  if (module.availability === "error" || module.availability === "missing" || module.loadError) {
    return "stopped";
  }
  const state = module.status?.state;
  if (module.activeJobId || state === "installing" || state === "starting" || state === "stopping") {
    return "busy";
  }
  if (state === "running") {
    return "running";
  }
  return "stopped";
}

export function applicationLogoStatus(modules: ToolModule[]): LogoStatus {
  if (modules.length === 0) {
    return "stopped";
  }
  const tones = modules.map(logoStatusForModule);
  if (tones.includes("busy")) return "busy";
  if (tones.includes("running")) return "running";
  return "stopped";
}

export function primaryService(modules: ToolModule[]): ToolModule | null {
  return modules.find((module) => module.status && actionable.has(module.status.state)) ?? null;
}

export function logoLabel(status: LogoStatus): string {
  if (status === "running") return "Service is running";
  if (status === "busy") return "Service is working";
  return "Service is stopped";
}

export function brandSvg(): string {
  return brand;
}

export function logoSvg(status: LogoStatus): string {
  const dot = logoColors[status];
  const cx = read(brand, "data-status-cx");
  const cy = read(brand, "data-status-cy");
  const radius = read(brand, "data-status-r");
  return brand.replace("</svg>", `<circle cx="${cx}" cy="${cy}" r="${radius}" fill="${dot}"/></svg>`);
}

function read(svg: string, name: string): string {
  const match = svg.match(new RegExp(`${name}="([^"]+)"`));
  if (!match) {
    throw new Error(`The Penelope logo is missing ${name}.`);
  }
  return match[1];
}
