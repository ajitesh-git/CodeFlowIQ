import { FolderTree, Search } from "lucide-react";
import { ResultExplorer } from "../../components/common/ResultExplorer";

type FilesPanelProps = {
  languageFilter: string;
  folderFilter: string;
  rows: string[];
  disabled: boolean;
  onLanguageFilterChange: (value: string) => void;
  onFolderFilterChange: (value: string) => void;
  onLoad: (languageOverride?: string, folderOverride?: string, take?: number) => void;
};

export function FilesPanel({
  languageFilter,
  folderFilter,
  rows,
  disabled,
  onLanguageFilterChange,
  onFolderFilterChange,
  onLoad
}: FilesPanelProps) {
  return (
    <div className="flow-layout">
      <div className="toolbar">
        <label>
          Language
          <input placeholder="csharp, sql, javascript" value={languageFilter} onChange={(event) => onLanguageFilterChange(event.target.value)} />
        </label>
        <label>
          Folder
          <input placeholder="Client or Deloitte.Omnia.FinancialFacts" value={folderFilter} onChange={(event) => onFolderFilterChange(event.target.value)} />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <FolderTree size={17} /> Files
        </button>
        <button onClick={() => {
          onLanguageFilterChange("");
          onFolderFilterChange("");
          onLoad("", "", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all
        </button>
      </div>
      <ResultExplorer
        emptyLabel="No files loaded"
        loadedLabel="files"
        rows={rows}
        searchPlaceholder="Search path, folder, language"
      />
    </div>
  );
}
