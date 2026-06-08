import { Cloud, Search } from "lucide-react";
import { ResultExplorer } from "../../components/common/ResultExplorer";

type AzurePanelProps = {
  serviceFilter: string;
  rows: string[];
  disabled: boolean;
  onServiceFilterChange: (value: string) => void;
  onLoad: (serviceOverride?: string, take?: number) => void;
};

export function AzurePanel({
  serviceFilter,
  rows,
  disabled,
  onServiceFilterChange,
  onLoad
}: AzurePanelProps) {
  return (
    <div className="flow-layout">
      <div className="toolbar">
        <label>
          Service filter
          <input value={serviceFilter} onChange={(event) => onServiceFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <Cloud size={17} /> Services
        </button>
        <button onClick={() => {
          onServiceFilterChange("");
          onLoad("", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No Azure dependencies loaded"
        loadedLabel="Azure references"
        rows={rows}
        searchPlaceholder="Search service, source, dependency"
      />
    </div>
  );
}
