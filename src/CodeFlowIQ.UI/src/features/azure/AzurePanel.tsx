import { Cloud, Search } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { ResultExplorer } from "../../components/common/ResultExplorer";
import { getExplorerTargetForPreviewRow, type ExplorerDrillTarget } from "../repository-explorer";

type AzurePanelProps = {
  serviceFilter: string;
  rows: string[];
  disabled: boolean;
  onServiceFilterChange: (value: string) => void;
  onLoad: (serviceOverride?: string, take?: number) => void;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

export function AzurePanel({
  serviceFilter,
  rows,
  disabled,
  onServiceFilterChange,
  onLoad,
  onOpenExplorer
}: AzurePanelProps) {
  return (
    <div className="flow-layout">
      <FeatureIntro
        title="Cloud Dependencies"
        description="Find Azure services referenced by the codebase and the files or methods that use them."
        helper="Use this page to answer: does this repo use queues, blobs, service bus, functions, or other cloud services?"
      />
      <div className="toolbar">
        <label>
          Find a service
          <input placeholder="Example: Service Bus, Blob, Queue" value={serviceFilter} onChange={(event) => onServiceFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <Cloud size={17} /> Show matching services
        </button>
        <button onClick={() => {
          onServiceFilterChange("");
          onLoad("", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all cloud evidence
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No cloud dependencies loaded yet. Search for a service or browse all cloud evidence."
        loadedLabel="cloud references"
        rows={rows}
        searchPlaceholder="Search service, file, method, or dependency"
        onOpenRow={(row) => onOpenExplorer(getExplorerTargetForPreviewRow("azure", row))}
      />
    </div>
  );
}
