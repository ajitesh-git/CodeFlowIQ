import { Search } from "lucide-react";
import { EmptyState } from "../../components/common/EmptyState";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import type { WorkspaceSummary } from "../../types";
import { getExplorerTargetForSummaryRow, type ExplorerDrillTarget } from "../repository-explorer";
import "./summary.css";

export function SummaryPanel({
  summary,
  onOpenExplorer
}: {
  summary: WorkspaceSummary | null;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
}) {
  return (
    <div className="summary-layout">
      <FeatureIntro
        title="Repository Snapshot"
        description="Start here for a quick count of what CodeFlowIQ found: languages, code building blocks, relationships between code, and cloud dependencies."
        helper="Click the search icon on any row to open matching evidence in Repository Explorer."
      />
      <div className="panel-grid">
        <ListPanel title="Languages" description="File types found in this repository." rows={summary?.languageCounts ?? []} onOpenExplorer={onOpenExplorer} />
        <ListPanel title="Code building blocks" sourceTitle="Symbols" description="Classes, methods, controllers, procedures, and other named code items." rows={summary?.symbolKindCounts ?? []} onOpenExplorer={onOpenExplorer} />
        <ListPanel title="Code connections" sourceTitle="Relationships" description="How files, methods, APIs, tables, and services connect to each other." rows={summary?.relationshipKindCounts ?? []} onOpenExplorer={onOpenExplorer} />
        <ListPanel title="Cloud services" sourceTitle="Azure" description="Azure dependencies referenced by the indexed code." rows={summary?.azureServiceCounts ?? []} onOpenExplorer={onOpenExplorer} />
      </div>
    </div>
  );
}

function ListPanel({
  title,
  sourceTitle,
  description,
  rows,
  onOpenExplorer
}: {
  title: string;
  sourceTitle?: string;
  description: string;
  rows: string[];
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
}) {
  return (
    <div className="list-panel">
      <h2>{title}</h2>
      <p>{description}</p>
      {rows.length === 0 ? (
        <EmptyState label="No data loaded yet" />
      ) : (
        rows.map((row) => {
          const [name, count] = row.split("\t");
          return (
            <div className="list-row" key={row}>
              <span>{formatSummaryLabel(name)}</span>
              <strong>{count}</strong>
              <button
                aria-label={`Open ${name} in Repository Explorer`}
                onClick={() => onOpenExplorer(getExplorerTargetForSummaryRow(sourceTitle ?? title, row))}
                title={`Open ${name} in Repository Explorer`}
                type="button"
              >
                <Search size={15} />
              </button>
            </div>
          );
        })
      )}
    </div>
  );
}

function formatSummaryLabel(value: string) {
  return value.replace(/_/g, " ");
}
