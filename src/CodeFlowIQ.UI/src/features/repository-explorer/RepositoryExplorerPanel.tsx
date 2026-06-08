import { Braces, Cloud, Database, FolderTree, Layers3, RefreshCw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { EmptyState } from "../../components/common/EmptyState";
import "./repository-explorer.css";

export type RepositoryExplorerSurface = "files" | "apis" | "backend" | "azure";

export type RepositoryExplorerRows = Record<RepositoryExplorerSurface, string[]>;

type RepositoryExplorerPanelProps = {
  activeSurface: RepositoryExplorerSurface;
  rowsBySurface: RepositoryExplorerRows;
  disabled: boolean;
  onSurfaceChange: (surface: RepositoryExplorerSurface) => void;
  onLoadSurface: (surface?: RepositoryExplorerSurface) => void;
  onLoadAll: () => void;
};

const surfaces: Array<{
  id: RepositoryExplorerSurface;
  label: string;
  icon: typeof FolderTree;
  emptyLabel: string;
  searchPlaceholder: string;
}> = [
  {
    id: "files",
    label: "Files",
    icon: FolderTree,
    emptyLabel: "No files loaded",
    searchPlaceholder: "Search path, folder, language"
  },
  {
    id: "apis",
    label: "APIs",
    icon: Braces,
    emptyLabel: "No APIs loaded",
    searchPlaceholder: "Search route, controller, verb"
  },
  {
    id: "backend",
    label: "Backend",
    icon: Database,
    emptyLabel: "No backend relationships loaded",
    searchPlaceholder: "Search methods, tables, procedures"
  },
  {
    id: "azure",
    label: "Azure",
    icon: Cloud,
    emptyLabel: "No Azure dependencies loaded",
    searchPlaceholder: "Search service, dependency, file"
  }
];

export function RepositoryExplorerPanel({
  activeSurface,
  rowsBySurface,
  disabled,
  onSurfaceChange,
  onLoadSurface,
  onLoadAll
}: RepositoryExplorerPanelProps) {
  const [query, setQuery] = useState("");
  const [selectedRow, setSelectedRow] = useState<string | null>(null);
  const rows = rowsBySurface[activeSurface];
  const activeConfig = surfaces.find((surface) => surface.id === activeSurface) ?? surfaces[0];

  const filteredRows = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (normalized.length === 0) {
      return rows;
    }

    return rows.filter((row) => row.toLowerCase().includes(normalized));
  }, [query, rows]);

  const groupedRows = useMemo(() => groupRows(filteredRows, activeSurface), [activeSurface, filteredRows]);
  const selectedDetail = selectedRow && filteredRows.includes(selectedRow) ? selectedRow : filteredRows[0] ?? null;

  function changeSurface(surface: RepositoryExplorerSurface) {
    onSurfaceChange(surface);
    setSelectedRow(null);
    setQuery("");
    if (rowsBySurface[surface].length === 0) {
      onLoadSurface(surface);
    }
  }

  return (
    <section className="repository-explorer">
      <header className="repository-explorer-header">
        <div>
          <span className="eyebrow">Repository Explorer</span>
          <h2>Browse all indexed repo intelligence</h2>
          <p>
            Move from curated previews into searchable full lists, then inspect one record at a time without losing
            orientation.
          </p>
        </div>
        <div className="repository-explorer-actions">
          <button onClick={() => onLoadSurface()} disabled={disabled}>
            <RefreshCw size={17} /> Load current
          </button>
          <button onClick={onLoadAll} disabled={disabled}>
            <Layers3 size={17} /> Load all
          </button>
        </div>
      </header>

      <div className="repository-tabs" role="tablist" aria-label="Repository explorer sections">
        {surfaces.map(({ id, label, icon: Icon }) => (
          <button
            aria-selected={activeSurface === id}
            className={activeSurface === id ? "active" : ""}
            key={id}
            onClick={() => changeSurface(id)}
            role="tab"
            type="button"
          >
            <Icon size={16} />
            <span>{label}</span>
            <strong>{rowsBySurface[id].length}</strong>
          </button>
        ))}
      </div>

      <div className="repository-workspace">
        <section className="repository-results" aria-label={`${activeConfig.label} results`}>
          <div className="repository-search">
            <label>
              Search {activeConfig.label.toLowerCase()}
              <span>
                <Search size={16} />
                <input
                  placeholder={activeConfig.searchPlaceholder}
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                />
              </span>
            </label>
            <p>
              <strong>{filteredRows.length}</strong> shown of {rows.length} loaded
            </p>
          </div>

          <div className="repository-group-list">
            {filteredRows.length === 0 ? (
              <EmptyState label={rows.length === 0 ? activeConfig.emptyLabel : "No loaded records match this search"} />
            ) : (
              groupedRows.map((group) => (
                <section className="repository-group" key={group.label}>
                  <h3>
                    {group.label}
                    <span>{group.rows.length}</span>
                  </h3>
                  {group.rows.map((row, index) => (
                    <button
                      className={selectedDetail === row ? "active" : ""}
                      key={`${row}-${index}`}
                      onClick={() => setSelectedRow(row)}
                      type="button"
                    >
                      <span>{getPrimaryText(row, activeSurface)}</span>
                      <small>{getSecondaryText(row, activeSurface)}</small>
                    </button>
                  ))}
                </section>
              ))
            )}
          </div>
        </section>

        <aside className="repository-detail" aria-label="Selected repository record">
          {selectedDetail ? (
            <>
              <span className="eyebrow">Selected Evidence</span>
              <h3>{getPrimaryText(selectedDetail, activeSurface)}</h3>
              <dl>
                <div>
                  <dt>Section</dt>
                  <dd>{activeConfig.label}</dd>
                </div>
                <div>
                  <dt>Group</dt>
                  <dd>{getGroupLabel(selectedDetail, activeSurface)}</dd>
                </div>
              </dl>
              <pre>{selectedDetail}</pre>
            </>
          ) : (
            <EmptyState label="Select a loaded record to inspect details" />
          )}
        </aside>
      </div>
    </section>
  );
}

function groupRows(rows: string[], surface: RepositoryExplorerSurface) {
  const groups = new Map<string, string[]>();
  rows.forEach((row) => {
    const label = getGroupLabel(row, surface);
    groups.set(label, [...(groups.get(label) ?? []), row]);
  });

  return Array.from(groups, ([label, groupRows]) => ({ label, rows: groupRows }));
}

function getGroupLabel(row: string, surface: RepositoryExplorerSurface) {
  if (surface === "files") {
    const path = getFilePath(row);
    const normalized = path.replaceAll("\\", "/");
    const firstFolder = normalized.split("/").filter(Boolean)[0];
    return firstFolder || "Root files";
  }

  if (surface === "backend") {
    if (row.includes("reads_table")) {
      return "Reads tables";
    }
    if (row.includes("writes_table")) {
      return "Writes tables";
    }
    if (row.includes("executes_procedure")) {
      return "Executes procedures";
    }
    if (row.includes("calls_method")) {
      return "Calls methods";
    }
    return "Backend relationships";
  }

  if (surface === "apis") {
    const verb = row.match(/\b(GET|POST|PUT|PATCH|DELETE)\b/i)?.[0]?.toUpperCase();
    return verb ? `${verb} endpoints` : "API endpoints";
  }

  const service = row.match(/\b(blob|queue|service bus|key vault|storage|sql|cosmos|function|app service)\b/i)?.[0];
  return service ? `${titleCase(service)} dependencies` : "Azure dependencies";
}

function getPrimaryText(row: string, surface: RepositoryExplorerSurface) {
  if (surface === "files") {
    return getFilePath(row);
  }

  const arrowParts = row.split(/\s*->\s*/);
  if (arrowParts.length > 1) {
    return arrowParts[0].trim();
  }

  return row.length > 110 ? `${row.slice(0, 110)}...` : row;
}

function getSecondaryText(row: string, surface: RepositoryExplorerSurface) {
  if (surface === "files") {
    return row.replace(getPrimaryText(row, surface), "").trim() || "Indexed file";
  }

  const arrowParts = row.split(/\s*->\s*/);
  if (arrowParts.length > 1) {
    return arrowParts.slice(1).join(" -> ").trim();
  }

  return getGroupLabel(row, surface);
}

function titleCase(value: string) {
  return value.replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function getFilePath(row: string) {
  const parts = row
    .split(/\t|\s{2,}|\s+\|\s+/)
    .map((part) => part.trim())
    .filter(Boolean);
  const pathPart = parts.find((part) => part.includes("\\") || part.includes("/") || part.includes("."));
  return pathPart ?? parts[parts.length - 1] ?? row;
}
