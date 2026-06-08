import { GitBranch, Search } from "lucide-react";
import { ResultExplorer } from "../../components/common/ResultExplorer";

type ApiSurfacePanelProps = {
  apiFilter: string;
  rows: string[];
  disabled: boolean;
  onApiFilterChange: (value: string) => void;
  onLoad: (routeOverride?: string, take?: number) => void;
};

export function ApiSurfacePanel({
  apiFilter,
  rows,
  disabled,
  onApiFilterChange,
  onLoad
}: ApiSurfacePanelProps) {
  return (
    <div className="flow-layout">
      <div className="toolbar">
        <label>
          Route filter
          <input value={apiFilter} onChange={(event) => onApiFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <GitBranch size={17} /> Routes
        </button>
        <button onClick={() => {
          onApiFilterChange("");
          onLoad("", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No APIs loaded"
        loadedLabel="API routes"
        rows={rows}
        searchPlaceholder="Search route, controller, method"
      />
    </div>
  );
}
