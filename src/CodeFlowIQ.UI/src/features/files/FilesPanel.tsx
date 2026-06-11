import { FolderTree, Search } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { ResultExplorer } from "../../components/common/ResultExplorer";
import { getExplorerTargetForPreviewRow, type ExplorerDrillTarget } from "../repository-explorer";

type FilesPanelProps = {
  languageFilter: string;
  folderFilter: string;
  rows: string[];
  disabled: boolean;
  onLanguageFilterChange: (value: string) => void;
  onFolderFilterChange: (value: string) => void;
  onLoad: (languageOverride?: string, folderOverride?: string, take?: number) => void;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

export function FilesPanel({
  languageFilter,
  folderFilter,
  rows,
  disabled,
  onLanguageFilterChange,
  onFolderFilterChange,
  onLoad,
  onOpenExplorer
}: FilesPanelProps) {
  return (
    <div className="flow-layout">
      <FeatureIntro
        title="Files and Folders"
        description="Browse the files CodeFlowIQ indexed, grouped by language or folder. This is the simplest way to confirm what the tool can see."
        helper="Use this page when a result looks incomplete: first check whether the file or folder was indexed."
      />
      <div className="toolbar">
        <label>
          Language
          <input placeholder="Example: csharp, sql, javascript" value={languageFilter} onChange={(event) => onLanguageFilterChange(event.target.value)} />
        </label>
        <label>
          Folder
          <input placeholder="Example: Client, Api, Data" value={folderFilter} onChange={(event) => onFolderFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <FolderTree size={17} /> Show matching files
        </button>
        <button onClick={() => {
          onLanguageFilterChange("");
          onFolderFilterChange("");
          onLoad("", "", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all files
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No files loaded yet. Choose a language/folder filter or browse all files."
        loadedLabel="files"
        rows={rows}
        searchPlaceholder="Search file path, folder, or language"
        onOpenRow={(row) => onOpenExplorer(getExplorerTargetForPreviewRow("files", row))}
      />
    </div>
  );
}
