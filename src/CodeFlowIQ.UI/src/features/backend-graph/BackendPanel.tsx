import { Search } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { ResultExplorer } from "../../components/common/ResultExplorer";
import { getExplorerTargetForPreviewRow, type ExplorerDrillTarget } from "../repository-explorer";

type BackendPanelProps = {
  rows: string[];
  disabled: boolean;
  onLoad: (kind?: string, targetOverride?: string, take?: number) => void;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

export function BackendPanel({ rows, disabled, onLoad, onOpenExplorer }: BackendPanelProps) {
  return (
    <div className="flow-layout">
      <FeatureIntro
        title="Backend Connections"
        description="See how backend methods, stored procedures, and database tables are connected. Start with a common question below, then open the full evidence when you need the source record."
        helper="Use this page when you want to answer: what code calls what, which procedures run, and which tables are read or written."
      />
      <div className="toolbar segmented">
        <button onClick={() => onLoad("executes_procedure")} disabled={disabled}>Runs procedures</button>
        <button onClick={() => onLoad("calls_method")} disabled={disabled}>Calls methods</button>
        <button onClick={() => onLoad("reads_table")} disabled={disabled}>Reads tables</button>
        <button onClick={() => onLoad("writes_table")} disabled={disabled}>Writes tables</button>
        <button onClick={() => onLoad("", "", 1000)} disabled={disabled}>
          <Search size={17} /> Browse all backend evidence
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No backend evidence loaded yet. Choose one of the buttons above to start."
        loadedLabel="backend records"
        rows={rows}
        searchPlaceholder="Search method, table, procedure, or file"
        onOpenRow={(row) => onOpenExplorer(getExplorerTargetForPreviewRow("backend", row))}
      />
    </div>
  );
}
