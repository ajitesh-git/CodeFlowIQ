import { Metric } from "../common/Metric";
import type { WorkspaceSummary } from "../../types";

export function MetricBand({ summary }: { summary: WorkspaceSummary | null }) {
  return (
    <section className="metric-band">
      <Metric label="Files" value={summary?.fileCount} />
      <Metric label="Symbols" value={summary?.symbolCount} />
      <Metric label="Relationships" value={summary?.relationshipCount} />
      <Metric label="Workspace" value={summary?.kind ?? "n/a"} />
    </section>
  );
}
