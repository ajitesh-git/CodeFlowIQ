import { GitBranch, Search } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { ResultExplorer } from "../../components/common/ResultExplorer";
import { getExplorerTargetForPreviewRow, type ExplorerDrillTarget } from "../repository-explorer";

type ApiSurfacePanelProps = {
  apiFilter: string;
  rows: string[];
  disabled: boolean;
  onApiFilterChange: (value: string) => void;
  onLoad: (routeOverride?: string, take?: number) => void;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

export function ApiSurfacePanel({
  apiFilter,
  rows,
  disabled,
  onApiFilterChange,
  onLoad,
  onOpenExplorer
}: ApiSurfacePanelProps) {
  return (
    <div className="flow-layout">
      <FeatureIntro
        title="API Endpoints"
        description="Review the routes this repository exposes and where each route is handled in code. Filter by route text when you already know the endpoint name."
        helper="Use this page to answer: which APIs exist, which controller or handler owns them, and where can I inspect the exact evidence?"
      />
      <div className="toolbar">
        <label>
          Find a route
          <input placeholder="Example: register, account, POST" value={apiFilter} onChange={(event) => onApiFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <GitBranch size={17} /> Show matching routes
        </button>
        <button onClick={() => {
          onApiFilterChange("");
          onLoad("", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all APIs
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No API routes loaded yet. Search for a route or browse all APIs."
        loadedLabel="API routes"
        rows={rows}
        searchPlaceholder="Search route, controller, handler, or HTTP verb"
        onOpenRow={(row) => onOpenExplorer(getExplorerTargetForPreviewRow("apis", row))}
      />
    </div>
  );
}
