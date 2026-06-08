import { EmptyState } from "../../components/common/EmptyState";
import type { WorkspaceSummary } from "../../types";
import "./summary.css";

export function SummaryPanel({ summary }: { summary: WorkspaceSummary | null }) {
  return (
    <div className="panel-grid">
      <ListPanel title="Languages" rows={summary?.languageCounts ?? []} />
      <ListPanel title="Symbols" rows={summary?.symbolKindCounts ?? []} />
      <ListPanel title="Relationships" rows={summary?.relationshipKindCounts ?? []} />
      <ListPanel title="Azure" rows={summary?.azureServiceCounts ?? []} />
    </div>
  );
}

function ListPanel({ title, rows }: { title: string; rows: string[] }) {
  return (
    <div className="list-panel">
      <h2>{title}</h2>
      {rows.length === 0 ? (
        <EmptyState label="No data" />
      ) : (
        rows.map((row) => {
          const [name, count] = row.split("\t");
          return (
            <div className="list-row" key={row}>
              <span>{name}</span>
              <strong>{count}</strong>
            </div>
          );
        })
      )}
    </div>
  );
}
