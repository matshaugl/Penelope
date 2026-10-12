import { brandSvg, logoLabel, logoSvg, type LogoStatus } from "./logoStatus";

export function Logo({
  status,
  size = 32,
}: {
  status?: LogoStatus;
  size?: number;
}) {
  return (
    <span
      className="logo"
      style={{ width: size, height: size }}
      role="img"
      aria-label={status ? logoLabel(status) : "Penelope"}
      dangerouslySetInnerHTML={{ __html: status ? logoSvg(status) : brandSvg() }}
    />
  );
}
