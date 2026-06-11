import { Loader2, Server, ShieldCheck } from "lucide-react";
import { FormEvent } from "react";
import type { ApiHealth, WorkspaceSummary } from "../../types";
import { MetricBand } from "./MetricBand";

type TopbarProps = {
  apiBaseUrl: string;
  apiSource: string;
  health: ApiHealth | null;
  summary: WorkspaceSummary | null;
  busy: string | null;
  onApiBaseUrlChange: (value: string) => void;
  onApiSourceChange: (value: "saved") => void;
  onCheckHealth: () => void;
};

export function Topbar({
  apiBaseUrl,
  apiSource,
  health,
  summary,
  busy,
  onApiBaseUrlChange,
  onApiSourceChange,
  onCheckHealth
}: TopbarProps) {
  function submitConnection(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onCheckHealth();
  }

  return (
    <section className="topbar">
      <form className="connection-form" onSubmit={submitConnection}>
        <label>
          CodeFlowIQ API ({apiSource})
          <input value={apiBaseUrl} onChange={(event) => {
            onApiSourceChange("saved");
            onApiBaseUrlChange(event.target.value);
          }} />
        </label>
        <button type="submit" className="icon-button" title="Check API connection">
          {busy === "Checking API" ? <Loader2 className="spin" size={18} /> : <Server size={18} />}
        </button>
        <div className={health?.status === "healthy" ? "status-pill ok" : "status-pill"}>
          <ShieldCheck size={16} />
          {health?.status === "healthy" ? "API connected" : "API offline"}
        </div>
      </form>

      <MetricBand summary={summary} />
    </section>
  );
}
