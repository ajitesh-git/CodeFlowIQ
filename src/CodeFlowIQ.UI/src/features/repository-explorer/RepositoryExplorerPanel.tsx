import { Braces, Cloud, Database, FolderTree, Layers3, RefreshCw, Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { EmptyState } from "../../components/common/EmptyState";
import type { RepositoryExplorerItem } from "../../types";
import "./repository-explorer.css";

export type RepositoryExplorerSurface = "files" | "apis" | "backend" | "azure";

export type RepositoryExplorerRows = Record<RepositoryExplorerSurface, RepositoryExplorerItem[]>;

type RepositoryExplorerPanelProps = {
  activeSurface: RepositoryExplorerSurface;
  incomingQuery: string;
  incomingSelectedItemId?: string | null;
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
  incomingQuery,
  incomingSelectedItemId,
  rowsBySurface,
  disabled,
  onSurfaceChange,
  onLoadSurface,
  onLoadAll
}: RepositoryExplorerPanelProps) {
  const [query, setQuery] = useState("");
  const [selectedRow, setSelectedRow] = useState<RepositoryExplorerItem | null>(null);
  const rowRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const rows = rowsBySurface[activeSurface];
  const activeConfig = surfaces.find((surface) => surface.id === activeSurface) ?? surfaces[0];

  useEffect(() => {
    setQuery(incomingQuery);
    setSelectedRow(null);
  }, [activeSurface, incomingQuery, incomingSelectedItemId]);

  useEffect(() => {
    if (!incomingSelectedItemId) {
      return;
    }

    const exactRow = rows.find((row) => row.id === incomingSelectedItemId);
    if (exactRow) {
      setSelectedRow(exactRow);
    }
  }, [incomingSelectedItemId, rows]);

  const filteredRows = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (normalized.length === 0) {
      return rows;
    }

    return rows.filter((row) => getSearchableText(row).includes(normalized));
  }, [query, rows]);

  const hasFocusedEmptyState = rows.length > 0 && filteredRows.length === 0 && query.trim().length > 0;
  const exactSelectedRow = incomingSelectedItemId ? rows.find((row) => row.id === incomingSelectedItemId) ?? null : null;
  const exactSelectionMissesFilter =
    exactSelectedRow !== null && !filteredRows.some((row) => row.id === exactSelectedRow.id);
  const visibleRows =
    (hasFocusedEmptyState && incomingQuery.trim().length > 0) || exactSelectionMissesFilter ? rows : filteredRows;
  const groupedRows = useMemo(() => groupRows(visibleRows, activeSurface), [activeSurface, visibleRows]);
  const selectedDetail =
    selectedRow && visibleRows.some((row) => row.id === selectedRow.id) ? selectedRow : visibleRows[0] ?? null;
  const populatedSurfaces = surfaces.filter((surface) => surface.id !== activeSurface && rowsBySurface[surface.id].length > 0);

  useEffect(() => {
    if (!incomingSelectedItemId || selectedDetail?.id !== incomingSelectedItemId) {
      return;
    }

    rowRefs.current[incomingSelectedItemId]?.scrollIntoView({ block: "center", behavior: "smooth" });
  }, [incomingSelectedItemId, selectedDetail]);

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
          {incomingQuery.trim().length > 0 && (
            <div className="repository-context-note">
              Showing drill-down results for <strong>{incomingQuery}</strong>
              {incomingSelectedItemId && <span>Exact evidence selected</span>}
            </div>
          )}
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
              <strong>{visibleRows.length}</strong> shown of {rows.length} loaded
            </p>
          </div>

          <div className="repository-group-list">
            {hasFocusedEmptyState && incomingQuery.trim().length > 0 && (
              <div className="repository-filter-empty compact">
                <strong>No focused matches for {incomingQuery}</strong>
                <p>Showing all loaded {activeConfig.label.toLowerCase()} instead. The drill-down opened the right Explorer tab, but this Runtime Map label does not appear verbatim in the indexed rows.</p>
                <button onClick={() => setQuery("")} type="button">
                  Clear focused search
                </button>
              </div>
            )}
            {exactSelectionMissesFilter && exactSelectedRow && (
              <div className="repository-filter-empty compact">
                <strong>Exact evidence selected</strong>
                <p>The selected Runtime Map evidence row does not match the current text filter, so the full loaded {activeConfig.label.toLowerCase()} list is shown.</p>
                <button onClick={() => setQuery("")} type="button">
                  Clear focused search
                </button>
              </div>
            )}
            {visibleRows.length === 0 ? (
              rows.length === 0 && populatedSurfaces.length > 0 ? (
                <div className="repository-filter-empty">
                  <strong>{activeConfig.emptyLabel}</strong>
                  <p>This Explorer section has no indexed rows for the current workspace. Other sections do have data loaded.</p>
                  <div className="repository-empty-actions">
                    {populatedSurfaces.map((surface) => (
                      <button key={surface.id} onClick={() => changeSurface(surface.id)} type="button">
                        Open {surface.label} ({rowsBySurface[surface.id].length})
                      </button>
                    ))}
                  </div>
                </div>
              ) : (
                <EmptyState label={rows.length === 0 ? activeConfig.emptyLabel : "No loaded records match this search"} />
              )
            ) : (
              groupedRows.map((group) => (
                <section className="repository-group" key={group.label}>
                  <h3>
                    {group.label}
                    <span>{group.rows.length}</span>
                  </h3>
                  {group.rows.map((row) => (
                    <button
                      className={selectedDetail?.id === row.id ? "active" : ""}
                      data-explorer-item-id={row.id}
                      key={row.id}
                      onClick={() => setSelectedRow(row)}
                      ref={(element) => {
                        rowRefs.current[row.id] = element;
                      }}
                      type="button"
                    >
                      <span>{row.title}</span>
                      <small>{row.subtitle}</small>
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
              <h3>{selectedDetail.title}</h3>
              <dl>
                <div>
                  <dt>Section</dt>
                  <dd>{activeConfig.label}</dd>
                </div>
                <div>
                  <dt>Group</dt>
                  <dd>{getGroupLabel(selectedDetail, activeSurface)}</dd>
                </div>
                {selectedDetail.relationshipKind && (
                  <div>
                    <dt>Relationship</dt>
                    <dd>{selectedDetail.relationshipKind}</dd>
                  </div>
                )}
                {selectedDetail.filePath && (
                  <div>
                    <dt>File</dt>
                    <dd>{selectedDetail.filePath}</dd>
                  </div>
                )}
                {selectedDetail.lineNumber && (
                  <div>
                    <dt>Line</dt>
                    <dd>{selectedDetail.lineNumber}</dd>
                  </div>
                )}
              </dl>
              <pre>{selectedDetail.detail}</pre>
              <div className="repository-detail-fields">
                <code>{selectedDetail.sourceKind}:{selectedDetail.sourceIdentifier}</code>
                {selectedDetail.targetIdentifier && <code>{selectedDetail.targetKind}:{selectedDetail.targetIdentifier}</code>}
                {selectedDetail.metadata && <code>{selectedDetail.metadata}</code>}
              </div>
            </>
          ) : (
            <EmptyState label="Select a loaded record to inspect details" />
          )}
        </aside>
      </div>
    </section>
  );
}

function groupRows(rows: RepositoryExplorerItem[], surface: RepositoryExplorerSurface) {
  const groups = new Map<string, RepositoryExplorerItem[]>();
  rows.forEach((row) => {
    const label = getGroupLabel(row, surface);
    groups.set(label, [...(groups.get(label) ?? []), row]);
  });

  return Array.from(groups, ([label, groupRows]) => ({ label, rows: groupRows }));
}

function getGroupLabel(row: RepositoryExplorerItem, surface: RepositoryExplorerSurface) {
  if (surface === "files") {
    const path = row.filePath ?? row.sourceIdentifier;
    const normalized = path.replaceAll("\\", "/");
    const firstFolder = normalized.split("/").filter(Boolean)[0];
    return firstFolder || "Root files";
  }

  if (surface === "backend") {
    if (row.relationshipKind === "reads_table") {
      return "Reads tables";
    }
    if (row.relationshipKind === "writes_table") {
      return "Writes tables";
    }
    if (row.relationshipKind === "executes_procedure") {
      return "Executes procedures";
    }
    if (row.relationshipKind === "calls_method") {
      return "Calls methods";
    }
    return "Backend relationships";
  }

  if (surface === "apis") {
    const verb = row.title.match(/\b(GET|POST|PUT|PATCH|DELETE)\b/i)?.[0]?.toUpperCase();
    return verb ? `${verb} endpoints` : "API endpoints";
  }

  const service = row.title.match(/\b(blob|queue|service bus|key vault|storage|sql|cosmos|function|app service)\b/i)?.[0];
  return service ? `${titleCase(service)} dependencies` : "Azure dependencies";
}

function titleCase(value: string) {
  return value.replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function getSearchableText(row: RepositoryExplorerItem) {
  return [
    row.title,
    row.subtitle,
    row.detail,
    row.sourceKind,
    row.sourceIdentifier,
    row.relationshipKind,
    row.targetKind,
    row.targetIdentifier,
    row.filePath,
    row.metadata
  ].filter(Boolean).join(" ").toLowerCase();
}
