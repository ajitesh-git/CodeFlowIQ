import { Loader2 } from "lucide-react";
import type { ApiHealth } from "../../types";

type StatusLineProps = {
  busy: string | null;
  message: string;
  health: ApiHealth | null;
};

export function StatusLine({ busy, message, health }: StatusLineProps) {
  return (
    <footer className="status-line">
      <span className={busy ? "busy-status" : ""}>
        {busy && <Loader2 className="spin" size={14} />}
        {message}
      </span>
      {health?.runtimeFile && <span>{health.runtimeFile}</span>}
    </footer>
  );
}
