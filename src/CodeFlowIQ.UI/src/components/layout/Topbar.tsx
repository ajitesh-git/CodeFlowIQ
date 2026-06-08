import { Loader2, Server, ShieldCheck } from "lucide-react";
import { FormEvent } from "react";
import type { ApiHealth } from "../../types";

type TopbarProps = {
  apiBaseUrl: string;
  apiSource: string;
  health: ApiHealth | null;
  busy: string | null;
  onApiBaseUrlChange: (value: string) => void;
  onApiSourceChange: (value: "saved") => void;
  onCheckHealth: () => void;
};

export function Topbar({
  apiBaseUrl,
  apiSource,
  health,
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
          API ({apiSource})
          <input value={apiBaseUrl} onChange={(event) => {
            onApiSourceChange("saved");
            onApiBaseUrlChange(event.target.value);
          }} />
        </label>
        <button type="submit" className="icon-button" title="Check API">
          {busy === "Checking API" ? <Loader2 className="spin" size={18} /> : <Server size={18} />}
        </button>
      </form>

      <div className={health?.status === "healthy" ? "status-pill ok" : "status-pill"}>
        <ShieldCheck size={16} />
        {health?.status ?? "offline"}
      </div>
    </section>
  );
}
