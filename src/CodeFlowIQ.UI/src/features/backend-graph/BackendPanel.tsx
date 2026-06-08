import { Search } from "lucide-react";
import { ResultExplorer } from "../../components/common/ResultExplorer";

type BackendPanelProps = {
  rows: string[];
  disabled: boolean;
  onLoad: (kind?: string, targetOverride?: string, take?: number) => void;
};

export function BackendPanel({ rows, disabled, onLoad }: BackendPanelProps) {
  return (
    <div className="flow-layout">
      <div className="toolbar segmented">
        <button onClick={() => onLoad("executes_procedure")} disabled={disabled}>Procedures</button>
        <button onClick={() => onLoad("calls_method")} disabled={disabled}>Methods</button>
        <button onClick={() => onLoad("reads_table")} disabled={disabled}>Reads</button>
        <button onClick={() => onLoad("writes_table")} disabled={disabled}>Writes</button>
        <button onClick={() => onLoad("", "", 1000)} disabled={disabled}>
          <Search size={17} /> Browse all
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No relationships loaded"
        loadedLabel="relationships"
        rows={rows}
        searchPlaceholder="Search relationships, methods, tables, procedures"
      />
    </div>
  );
}
