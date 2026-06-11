import { ArrowRight, Braces, Check, ChevronLeft, ChevronRight, Cloud, Copy, Database, FolderTree, Layers3, RefreshCw, Search } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { EmptyState } from "../../components/common/EmptyState";
import type { RepositoryExplorerItem, RepositoryExplorerRelatedGroup } from "../../types";
import "./repository-explorer.css";

export type RepositoryExplorerSurface = "files" | "apis" | "backend" | "azure";

export type RepositoryExplorerRows = Record<RepositoryExplorerSurface, RepositoryExplorerItem[]>;

type RepositoryExplorerPanelProps = {
  activeSurface: RepositoryExplorerSurface;
  incomingQuery: string;
  incomingOriginLabel: string;
  incomingSelectedItemId?: string | null;
  rowsBySurface: RepositoryExplorerRows;
  disabled: boolean;
  onSurfaceChange: (surface: RepositoryExplorerSurface) => void;
  onLoadSurface: (surface?: RepositoryExplorerSurface, selectedItemId?: string | null) => void;
  onLoadAll: () => void;
  onLoadRelatedEvidence: (surface: RepositoryExplorerSurface, itemId: string) => Promise<RepositoryExplorerRelatedGroup[]>;
};

type EvidenceTrailItem = {
  id: string;
  surface: RepositoryExplorerSurface;
  title: string;
  subtitle: string;
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
    label: "Backend & data",
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
const repositoryRowsPerPage = 50;

export function RepositoryExplorerPanel({
  activeSurface,
  incomingQuery,
  incomingOriginLabel,
  incomingSelectedItemId,
  rowsBySurface,
  disabled,
  onSurfaceChange,
  onLoadSurface,
  onLoadAll,
  onLoadRelatedEvidence
}: RepositoryExplorerPanelProps) {
  const [query, setQuery] = useState("");
  const [relationshipKindFilter, setRelationshipKindFilter] = useState("all");
  const [sourceKindFilter, setSourceKindFilter] = useState("all");
  const [targetKindFilter, setTargetKindFilter] = useState("all");
  const [filePathFilter, setFilePathFilter] = useState("");
  const [evidenceQualityFilter, setEvidenceQualityFilter] = useState("all");
  const [selectedRow, setSelectedRow] = useState<RepositoryExplorerItem | null>(null);
  const [pendingRelatedItemId, setPendingRelatedItemId] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [evidenceTrail, setEvidenceTrail] = useState<EvidenceTrailItem[]>([]);
  const [copiedEvidenceId, setCopiedEvidenceId] = useState<string | null>(null);
  const [relatedEvidenceFromApi, setRelatedEvidenceFromApi] = useState<RepositoryExplorerRelatedGroup[]>([]);
  const [relatedEvidenceStatus, setRelatedEvidenceStatus] = useState<"idle" | "loading" | "error">("idle");
  const rowRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const rows = rowsBySurface[activeSurface];
  const activeConfig = surfaces.find((surface) => surface.id === activeSurface) ?? surfaces[0];

  useEffect(() => {
    setQuery(incomingQuery);
    setRelationshipKindFilter("all");
    setSourceKindFilter("all");
    setTargetKindFilter("all");
    setFilePathFilter("");
    setEvidenceQualityFilter("all");
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

  useEffect(() => {
    if (!pendingRelatedItemId) {
      return;
    }

    const pendingRow = rows.find((row) => row.id === pendingRelatedItemId);
    if (pendingRow) {
      setSelectedRow(pendingRow);
      setPendingRelatedItemId(null);
    }
  }, [pendingRelatedItemId, rows]);

  const filterOptions = useMemo(() => getEvidenceFilterOptions(rows), [rows]);
  const hasActiveEvidenceFilters = relationshipKindFilter !== "all"
    || sourceKindFilter !== "all"
    || targetKindFilter !== "all"
    || filePathFilter.trim().length > 0
    || evidenceQualityFilter !== "all";

  const filteredRows = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    const normalizedFilePath = filePathFilter.trim().toLowerCase();
    return rows.filter((row) => {
      const matchesQuery = normalized.length === 0 || getSearchableText(row).includes(normalized);
      const matchesRelationship = relationshipKindFilter === "all" || row.relationshipKind === relationshipKindFilter;
      const matchesSource = sourceKindFilter === "all" || row.sourceKind === sourceKindFilter;
      const matchesTarget = targetKindFilter === "all" || (row.targetKind ?? "") === targetKindFilter;
      const matchesFilePath = normalizedFilePath.length === 0 || getFileFilterText(row).includes(normalizedFilePath);
      const matchesQuality = evidenceQualityFilter === "all" || getEvidenceQuality(row).id === evidenceQualityFilter;
      return matchesQuery && matchesRelationship && matchesSource && matchesTarget && matchesFilePath && matchesQuality;
    });
  }, [
    evidenceQualityFilter,
    filePathFilter,
    query,
    relationshipKindFilter,
    rows,
    sourceKindFilter,
    targetKindFilter
  ]);

  const hasFocusedEmptyState = rows.length > 0
    && filteredRows.length === 0
    && (query.trim().length > 0 || hasActiveEvidenceFilters);
  const exactSelectedRow = incomingSelectedItemId ? rows.find((row) => row.id === incomingSelectedItemId) ?? null : null;
  const exactSelectionMissesFilter =
    exactSelectedRow !== null && !filteredRows.some((row) => row.id === exactSelectedRow.id);
  const visibleRows =
    (hasFocusedEmptyState && incomingQuery.trim().length > 0) || exactSelectionMissesFilter ? rows : filteredRows;
  const pageCount = Math.max(1, Math.ceil(visibleRows.length / repositoryRowsPerPage));
  const safeCurrentPage = Math.min(currentPage, pageCount);
  const pageStartIndex = (safeCurrentPage - 1) * repositoryRowsPerPage;
  const pagedRows = useMemo(
    () => visibleRows.slice(pageStartIndex, pageStartIndex + repositoryRowsPerPage),
    [pageStartIndex, visibleRows]
  );
  const groupedRows = useMemo(() => groupRows(pagedRows, activeSurface), [activeSurface, pagedRows]);
  const visibleRowOccurrences = useMemo(() => getRowOccurrenceMap(visibleRows), [visibleRows]);
  const selectedDetail =
    selectedRow && visibleRows.some((row) => row.id === selectedRow.id) ? selectedRow : pagedRows[0] ?? null;
  const populatedSurfaces = surfaces.filter((surface) => surface.id !== activeSurface && rowsBySurface[surface.id].length > 0);
  const relatedEvidence = useMemo(
    () => getRelatedEvidence(selectedDetail, rowsBySurface),
    [rowsBySurface, selectedDetail]
  );
  const visibleRelatedEvidence = relatedEvidenceFromApi.length > 0 ? relatedEvidenceFromApi : relatedEvidence;
  const loadedSurfaceCount = surfaces.filter((surface) => rowsBySurface[surface.id].length > 0).length;
  const hasUnloadedContext = loadedSurfaceCount < surfaces.length;
  const previousTrailItem = evidenceTrail.length > 1 ? evidenceTrail[evidenceTrail.length - 2] : null;

  useEffect(() => {
    setCurrentPage(1);
  }, [
    activeSurface,
    evidenceQualityFilter,
    filePathFilter,
    query,
    relationshipKindFilter,
    rows.length,
    sourceKindFilter,
    targetKindFilter
  ]);

  useEffect(() => {
    if (currentPage > pageCount) {
      setCurrentPage(pageCount);
    }
  }, [currentPage, pageCount]);

  useEffect(() => {
    if (!selectedDetail) {
      return;
    }

    const selectedIndex = visibleRows.findIndex((row) => row.id === selectedDetail.id);
    if (selectedIndex < 0) {
      return;
    }

    const selectedPage = Math.floor(selectedIndex / repositoryRowsPerPage) + 1;
    if (selectedPage !== safeCurrentPage) {
      setCurrentPage(selectedPage);
    }
  }, [safeCurrentPage, selectedDetail?.id, visibleRows]);

  useEffect(() => {
    let cancelled = false;
    if (!selectedDetail) {
      setRelatedEvidenceFromApi([]);
      setRelatedEvidenceStatus("idle");
      return;
    }

    setRelatedEvidenceStatus("loading");
    onLoadRelatedEvidence(activeSurface, selectedDetail.id)
      .then((groups) => {
        if (!cancelled) {
          setRelatedEvidenceFromApi(groups);
          setRelatedEvidenceStatus("idle");
        }
      })
      .catch(() => {
        if (!cancelled) {
          setRelatedEvidenceFromApi([]);
          setRelatedEvidenceStatus("error");
        }
      });

    return () => {
      cancelled = true;
    };
  }, [activeSurface, selectedDetail?.id]);

  useEffect(() => {
    if (!incomingSelectedItemId || selectedDetail?.id !== incomingSelectedItemId) {
      return;
    }

    rowRefs.current[incomingSelectedItemId]?.scrollIntoView({ block: "center", behavior: "smooth" });
  }, [incomingSelectedItemId, selectedDetail]);

  useEffect(() => {
    if (!selectedDetail) {
      return;
    }

    rowRefs.current[selectedDetail.id]?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [pagedRows, safeCurrentPage, selectedDetail?.id]);

  useEffect(() => {
    if (!selectedDetail) {
      return;
    }

    const nextTrailItem = toEvidenceTrailItem(selectedDetail);
    setEvidenceTrail((current) => {
      if (current.at(-1)?.id === nextTrailItem.id) {
        return current;
      }

      return [...current.filter((item) => item.id !== nextTrailItem.id), nextTrailItem].slice(-6);
    });
  }, [selectedDetail?.id]);

  function changeSurface(surface: RepositoryExplorerSurface) {
    onSurfaceChange(surface);
    setSelectedRow(null);
    setQuery("");
    if (rowsBySurface[surface].length === 0) {
      onLoadSurface(surface);
    }
  }

  function openRelatedRow(row: RepositoryExplorerItem) {
    if (row.surface === activeSurface) {
      if (rowsBySurface[row.surface].some((item) => item.id === row.id)) {
        setSelectedRow(row);
        rowRefs.current[row.id]?.scrollIntoView({ block: "center", behavior: "smooth" });
      } else {
        setPendingRelatedItemId(row.id);
        onLoadSurface(row.surface, row.id);
      }
      return;
    }

    setPendingRelatedItemId(row.id);
    onSurfaceChange(row.surface);
    setQuery("");
    if (rowsBySurface[row.surface].length === 0) {
      onLoadSurface(row.surface, row.id);
    } else if (!rowsBySurface[row.surface].some((item) => item.id === row.id)) {
      onLoadSurface(row.surface, row.id);
    }
  }

  function reopenTrailItem(item: EvidenceTrailItem) {
    if (item.surface === activeSurface) {
      const row = rowsBySurface[item.surface].find((candidate) => candidate.id === item.id);
      if (row) {
        setSelectedRow(row);
        return;
      }
    }

    setPendingRelatedItemId(item.id);
    onSurfaceChange(item.surface);
    setQuery("");
    if (!rowsBySurface[item.surface].some((candidate) => candidate.id === item.id)) {
      onLoadSurface(item.surface, item.id);
    }
  }

  function reopenPreviousTrailItem() {
    if (previousTrailItem) {
      reopenTrailItem(previousTrailItem);
    }
  }

  async function copyEvidenceReference(row: RepositoryExplorerItem) {
    await navigator.clipboard.writeText(formatEvidenceReference(row));
    setCopiedEvidenceId(row.id);
    window.setTimeout(() => setCopiedEvidenceId(null), 1400);
  }

  function clearEvidenceFilters() {
    setRelationshipKindFilter("all");
    setSourceKindFilter("all");
    setTargetKindFilter("all");
    setFilePathFilter("");
    setEvidenceQualityFilter("all");
  }

  return (
    <section className="repository-explorer">
      <header className="repository-explorer-header">
        <div>
          <span className="eyebrow">Repository Explorer</span>
          <h2>Browse the full repository evidence</h2>
          <p>
            Use this when a preview is not enough. Pick a section, search the complete indexed records, and inspect one
            source-backed item at a time.
          </p>
          {(incomingQuery.trim().length > 0 || incomingOriginLabel) && (
            <div className="repository-context-note">
              Opened from <strong>{incomingOriginLabel || "another page"}</strong>
              {incomingQuery.trim().length > 0 && <span>Focused on {incomingQuery}</span>}
              {incomingSelectedItemId && <span>Exact source record selected</span>}
            </div>
          )}
        </div>
        <div className="repository-explorer-actions">
          <button onClick={() => onLoadSurface()} disabled={disabled}>
            <RefreshCw size={17} /> Load this section
          </button>
          <button onClick={onLoadAll} disabled={disabled}>
            <Layers3 size={17} /> Load all sections
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
              <strong>{visibleRows.length}</strong> matching of {rows.length} loaded
            </p>
          </div>

          <div className="repository-evidence-filters" aria-label="Evidence filters">
            <label>
              Relationship
              <select value={relationshipKindFilter} onChange={(event) => setRelationshipKindFilter(event.target.value)}>
                <option value="all">All relationships</option>
                {filterOptions.relationshipKinds.map((value) => (
                  <option key={value} value={value}>{formatRelationshipLabel(value)}</option>
                ))}
              </select>
            </label>
            <label>
              Source
              <select value={sourceKindFilter} onChange={(event) => setSourceKindFilter(event.target.value)}>
                <option value="all">All sources</option>
                {filterOptions.sourceKinds.map((value) => (
                  <option key={value} value={value}>{formatKindLabel(value)}</option>
                ))}
              </select>
            </label>
            <label>
              Target
              <select value={targetKindFilter} onChange={(event) => setTargetKindFilter(event.target.value)}>
                <option value="all">All targets</option>
                {filterOptions.targetKinds.map((value) => (
                  <option key={value} value={value}>{formatKindLabel(value)}</option>
                ))}
              </select>
            </label>
            <label>
              File or path
              <input
                placeholder="Example: controller, service, .sql"
                value={filePathFilter}
                onChange={(event) => setFilePathFilter(event.target.value)}
              />
            </label>
            <label>
              Evidence quality
              <select value={evidenceQualityFilter} onChange={(event) => setEvidenceQualityFilter(event.target.value)}>
                <option value="all">All evidence</option>
                <option value="source-backed">Source-backed</option>
                <option value="inferred-link">Inferred links</option>
                <option value="with-location">Has file or line</option>
              </select>
            </label>
            <button onClick={clearEvidenceFilters} disabled={!hasActiveEvidenceFilters} type="button">
              Clear filters
            </button>
          </div>

          {visibleRows.length > 0 && (
            <RepositoryPagination
              currentPage={safeCurrentPage}
              end={Math.min(pageStartIndex + pagedRows.length, visibleRows.length)}
              onPageChange={setCurrentPage}
              pageCount={pageCount}
              pageSize={repositoryRowsPerPage}
              start={pageStartIndex + 1}
              total={visibleRows.length}
            />
          )}

          <div className="repository-group-list">
            {hasFocusedEmptyState && incomingQuery.trim().length > 0 && (
              <div className="repository-filter-empty compact">
                <strong>No focused matches for {incomingQuery}</strong>
                <p>Showing all loaded {activeConfig.label.toLowerCase()} instead. The source record opened the right section, but the readable label does not appear word-for-word in the indexed data.</p>
                <button onClick={() => setQuery("")} type="button">
                  Clear focused search
                </button>
              </div>
            )}
            {hasFocusedEmptyState && incomingQuery.trim().length === 0 && (
              <div className="repository-filter-empty compact">
                <strong>No evidence matches the current filters</strong>
                <p>Try clearing a filter or loading a broader section of repository evidence.</p>
                <button onClick={() => {
                  setQuery("");
                  clearEvidenceFilters();
                }} type="button">
                  Clear search and filters
                </button>
              </div>
            )}
            {exactSelectionMissesFilter && exactSelectedRow && (
              <div className="repository-filter-empty compact">
                <strong>Exact evidence selected</strong>
                <p>The selected source record does not match the current text filter, so the full loaded {activeConfig.label.toLowerCase()} list is shown.</p>
                <button onClick={() => setQuery("")} type="button">
                  Clear focused search
                </button>
              </div>
            )}
            {visibleRows.length === 0 ? (
              rows.length === 0 && populatedSurfaces.length > 0 ? (
                <div className="repository-filter-empty">
                  <strong>{activeConfig.emptyLabel}</strong>
                  <p>This section has no indexed rows for the current workspace. Other sections do have data loaded.</p>
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
                    <span className="repository-group-title">{group.label}</span>
                    <small>{group.summary}</small>
                    <span className="repository-group-count">{group.rows.length}</span>
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
                      <span>{getRepositoryRowTitle(row)}</span>
                      <small>{getRepositoryRowSubtitle(row)}</small>
                      <em>{getRepositoryRowLocator(row, visibleRowOccurrences.get(row.id))}</em>
                      <span className={`repository-quality-badge ${getEvidenceQuality(row).id}`}>
                        {getEvidenceQuality(row).label}
                      </span>
                    </button>
                  ))}
                </section>
              ))
            )}
          </div>
          {visibleRows.length > repositoryRowsPerPage && (
            <RepositoryPagination
              currentPage={safeCurrentPage}
              end={Math.min(pageStartIndex + pagedRows.length, visibleRows.length)}
              onPageChange={setCurrentPage}
              pageCount={pageCount}
              pageSize={repositoryRowsPerPage}
              start={pageStartIndex + 1}
              total={visibleRows.length}
            />
          )}
        </section>

        <aside className="repository-detail" aria-label="Selected repository record">
          {selectedDetail ? (
            <>
              <section className="repository-detail-hero" aria-label="Selected evidence summary">
                <div className="repository-detail-breadcrumb" aria-label="Explorer navigation context">
                  <span>{incomingOriginLabel || "Repository Explorer"}</span>
                  <ArrowRight size={13} />
                  <span>{getSurfaceLabel(selectedDetail.surface)}</span>
                  <ArrowRight size={13} />
                  <strong>{getRepositoryRowTitle(selectedDetail)}</strong>
                </div>
                <div>
                  <span className="eyebrow">Selected Evidence</span>
                  <h3>{selectedDetail.title}</h3>
                  <p className="repository-detail-summary">{describeSelectedEvidence(selectedDetail)}</p>
                </div>
                <span className={`repository-quality-badge detail ${getEvidenceQuality(selectedDetail).id}`}>
                  {getEvidenceQuality(selectedDetail).label}
                </span>
              </section>

              <section className="repository-detail-section" aria-label="Evidence source">
                <div className="repository-section-heading">
                  <span className="eyebrow">Where Found</span>
                  <strong>{getSurfaceLabel(selectedDetail.surface)}</strong>
                </div>
                <div className="repository-evidence-path" aria-label="Evidence location">
                  <span>{activeConfig.label}</span>
                  <ArrowRight size={14} />
                  <span>{getGroupLabel(selectedDetail, activeSurface)}</span>
                  <ArrowRight size={14} />
                  <strong>{getRepositoryRowTitle(selectedDetail)}</strong>
                </div>
                <dl className="repository-source-grid">
                  <div>
                    <dt>Source area</dt>
                    <dd>{formatKindLabel(selectedDetail.sourceKind)}</dd>
                  </div>
                  <div>
                    <dt>Target area</dt>
                    <dd>{selectedDetail.targetKind ? formatKindLabel(selectedDetail.targetKind) : "None"}</dd>
                  </div>
                  {selectedDetail.relationshipKind && (
                    <div>
                      <dt>Relationship</dt>
                      <dd>{formatRelationshipLabel(selectedDetail.relationshipKind)}</dd>
                    </div>
                  )}
                  <div>
                    <dt>Location</dt>
                    <dd>{getSourceLocationSummary(selectedDetail)}</dd>
                  </div>
                </dl>
                <div className="repository-detail-actions">
                  <button onClick={() => copyEvidenceReference(selectedDetail)} type="button">
                    {copiedEvidenceId === selectedDetail.id ? <Check size={16} /> : <Copy size={16} />}
                    {copiedEvidenceId === selectedDetail.id ? "Copied reference" : "Copy evidence reference"}
                  </button>
                </div>
              </section>

              {evidenceTrail.length > 1 && (
                <section className="repository-evidence-trail" aria-label="Recently opened evidence">
                  <div>
                    <div>
                      <span className="eyebrow">Recent Evidence Path</span>
                      <strong>{evidenceTrail.length} opened</strong>
                    </div>
                    <button onClick={reopenPreviousTrailItem} disabled={!previousTrailItem} type="button">
                      <ChevronLeft size={14} /> Back one step
                    </button>
                  </div>
                  <div>
                    {evidenceTrail.map((item) => (
                      <button
                        className={item.id === selectedDetail.id ? "active" : ""}
                        key={item.id}
                        onClick={() => reopenTrailItem(item)}
                        type="button"
                      >
                        <span>{item.title}</span>
                        <small>{getSurfaceLabel(item.surface)} / {item.subtitle}</small>
                      </button>
                    ))}
                  </div>
                </section>
              )}

              <section className="repository-related-evidence" aria-label="Related repository evidence">
                <div className="repository-related-heading">
                  <div>
                    <span className="eyebrow">Connected Evidence</span>
                    <strong>{visibleRelatedEvidence.reduce((sum, group) => sum + group.rows.length, 0)} matches</strong>
                    {relatedEvidenceStatus === "loading" && <small>Looking for related source records...</small>}
                    {relatedEvidenceStatus === "error" && <small>Showing related records already loaded on this page</small>}
                  </div>
                  {hasUnloadedContext && relatedEvidenceFromApi.length === 0 && (
                    <button onClick={onLoadAll} disabled={disabled} type="button">
                      <Layers3 size={15} /> Load all sections
                    </button>
                  )}
                </div>
                {visibleRelatedEvidence.length === 0 ? (
                  <EmptyState label={relatedEvidenceStatus === "loading" ? "Finding nearby evidence" : "No nearby evidence found"} />
                ) : (
                  visibleRelatedEvidence.map((group) => (
                    <div className="repository-related-group" key={group.label}>
                      <h4>
                        {group.label}
                        <span>{group.rows.length}</span>
                      </h4>
                      {group.rows.map((row) => (
                        <button key={row.id} onClick={() => openRelatedRow(row)} type="button">
                          <span>{getRepositoryRowTitle(row)}</span>
                          <small>
                            {getSurfaceLabel(row.surface)} / {getRepositoryRowSubtitle(row)}
                          </small>
                          <em>{getRepositoryRowLocator(row)}</em>
                          <ArrowRight size={15} />
                        </button>
                      ))}
                    </div>
                  ))
                )}
              </section>

              <section className="repository-detail-section muted" aria-label="Raw evidence reference">
                <div className="repository-section-heading">
                  <span className="eyebrow">Evidence Reference</span>
                  <strong>Source-backed record</strong>
                </div>
                <pre>{selectedDetail.detail}</pre>
                <div className="repository-detail-fields">
                  <code>{selectedDetail.sourceKind}:{selectedDetail.sourceIdentifier}</code>
                  {selectedDetail.targetIdentifier && <code>{selectedDetail.targetKind}:{selectedDetail.targetIdentifier}</code>}
                  {selectedDetail.metadata && <code>{selectedDetail.metadata}</code>}
                </div>
              </section>
            </>
          ) : (
            <EmptyState label="Select a loaded record to inspect details" />
          )}
        </aside>
      </div>
    </section>
  );
}

function RepositoryPagination({
  currentPage,
  end,
  onPageChange,
  pageCount,
  pageSize,
  start,
  total
}: {
  currentPage: number;
  end: number;
  onPageChange: (page: number) => void;
  pageCount: number;
  pageSize: number;
  start: number;
  total: number;
}) {
  return (
    <div className="repository-pagination" aria-label="Repository evidence pagination">
      <div>
        <strong>{total}</strong>
        <span>matching evidence records</span>
        <small>Showing {start}-{end} in pages of {pageSize}</small>
      </div>
      <div className="repository-pagination-actions">
        <button onClick={() => onPageChange(currentPage - 1)} disabled={currentPage <= 1} type="button">
          <ChevronLeft size={16} /> Previous
        </button>
        <span>Page {currentPage} of {pageCount}</span>
        <button onClick={() => onPageChange(currentPage + 1)} disabled={currentPage >= pageCount} type="button">
          Next <ChevronRight size={16} />
        </button>
      </div>
    </div>
  );
}

function getRelatedEvidence(
  selectedRow: RepositoryExplorerItem | null,
  rowsBySurface: RepositoryExplorerRows
): RepositoryExplorerRelatedGroup[] {
  if (!selectedRow) {
    return [];
  }

  const allRows = Object.values(rowsBySurface)
    .flat()
    .filter((row) => row.id !== selectedRow.id);
  const groups: RepositoryExplorerRelatedGroup[] = [];

  addRelatedGroup(groups, "Outgoing from this evidence", allRows.filter((row) =>
    isSameIdentifier(row.sourceIdentifier, selectedRow.targetIdentifier)
    || isSameIdentifier(row.sourceIdentifier, selectedRow.sourceIdentifier)
  ));

  addRelatedGroup(groups, "Incoming to this evidence", allRows.filter((row) =>
    isSameIdentifier(row.targetIdentifier, selectedRow.sourceIdentifier)
    || isSameIdentifier(row.targetIdentifier, selectedRow.targetIdentifier)
  ));

  addRelatedGroup(groups, "Same dependency or target", allRows.filter((row) =>
    isSameIdentifier(row.targetIdentifier, selectedRow.targetIdentifier)
    || isSameIdentifier(row.sourceIdentifier, selectedRow.targetIdentifier)
  ));

  addRelatedGroup(groups, "Same file or source area", allRows.filter((row) =>
    hasSharedFileContext(row, selectedRow)
  ));

  return dedupeRelatedGroups(groups);
}

function addRelatedGroup(groups: RepositoryExplorerRelatedGroup[], label: string, rows: RepositoryExplorerItem[]) {
  const uniqueRows = dedupeRows(rows).slice(0, 6);
  if (uniqueRows.length > 0) {
    groups.push({ label, rows: uniqueRows });
  }
}

function dedupeRelatedGroups(groups: RepositoryExplorerRelatedGroup[]) {
  const seen = new Set<string>();
  return groups
    .map((group) => ({
      ...group,
      rows: group.rows.filter((row) => {
        if (seen.has(row.id)) {
          return false;
        }

        seen.add(row.id);
        return true;
      })
    }))
    .filter((group) => group.rows.length > 0);
}

function dedupeRows(rows: RepositoryExplorerItem[]) {
  const seen = new Set<string>();
  return rows.filter((row) => {
    if (seen.has(row.id)) {
      return false;
    }

    seen.add(row.id);
    return true;
  });
}

function isSameIdentifier(left?: string | null, right?: string | null) {
  if (!left || !right) {
    return false;
  }

  const normalizedLeft = normalizeIdentifier(left);
  const normalizedRight = normalizeIdentifier(right);
  return normalizedLeft.length > 0
    && normalizedRight.length > 0
    && (normalizedLeft === normalizedRight
      || getIdentifierMember(normalizedLeft) === getIdentifierMember(normalizedRight));
}

function hasSharedFileContext(row: RepositoryExplorerItem, selectedRow: RepositoryExplorerItem) {
  const rowFile = normalizeFilePath(row.filePath ?? row.sourceIdentifier);
  const selectedFile = normalizeFilePath(selectedRow.filePath ?? selectedRow.sourceIdentifier);
  if (!rowFile || !selectedFile) {
    return false;
  }

  return rowFile === selectedFile
    || rowFile.startsWith(`${selectedFile}/`)
    || selectedFile.startsWith(`${rowFile}/`);
}

function normalizeIdentifier(value: string) {
  return value.trim().replaceAll("\\", "/").toLowerCase();
}

function normalizeFilePath(value: string) {
  return normalizeIdentifier(value).split("::")[0];
}

function getIdentifierMember(value: string) {
  const qualifiedIndex = value.lastIndexOf("::");
  const member = qualifiedIndex >= 0 ? value.slice(qualifiedIndex + 2) : value;
  const dotIndex = member.lastIndexOf(".");
  return dotIndex >= 0 && dotIndex < member.length - 1 ? member.slice(dotIndex + 1) : member;
}

function getSurfaceLabel(surface: RepositoryExplorerSurface) {
  return surfaces.find((item) => item.id === surface)?.label ?? surface;
}

function getEvidenceFilterOptions(rows: RepositoryExplorerItem[]) {
  return {
    relationshipKinds: uniqueSorted(rows.map((row) => row.relationshipKind).filter(isPresent)),
    sourceKinds: uniqueSorted(rows.map((row) => row.sourceKind).filter(isPresent)),
    targetKinds: uniqueSorted(rows.map((row) => row.targetKind).filter(isPresent))
  };
}

function uniqueSorted(values: string[]) {
  return Array.from(new Set(values)).sort((left, right) => left.localeCompare(right));
}

function isPresent(value?: string | null): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function toEvidenceTrailItem(row: RepositoryExplorerItem): EvidenceTrailItem {
  return {
    id: row.id,
    surface: row.surface,
    title: getRepositoryRowTitle(row),
    subtitle: getRepositoryRowSubtitle(row)
  };
}

function getRepositoryRowTitle(row: RepositoryExplorerItem) {
  if (row.displayTitle) {
    return row.displayTitle;
  }

  if (row.surface === "azure" && row.targetIdentifier) {
    return formatReadableIdentifier(row.targetIdentifier);
  }

  return row.title;
}

function getRepositoryRowSubtitle(row: RepositoryExplorerItem) {
  if (row.displaySubtitle) {
    return row.displaySubtitle;
  }

  if (row.surface === "azure") {
    return `Used in ${formatSourceLocation(row.sourceIdentifier, row.filePath)}`;
  }

  if (row.surface === "apis" && row.sourceIdentifier) {
    return `Handled by ${formatReadableIdentifier(row.sourceIdentifier)}`;
  }

  return row.relationshipKind
    ? `${row.sourceKind} / ${formatRelationshipLabel(row.relationshipKind)} / ${row.targetKind}`
    : row.subtitle;
}

function getRepositoryRowLocator(
  row: RepositoryExplorerItem,
  occurrence?: { index: number; total: number }
) {
  const parts: string[] = [];
  if (occurrence && occurrence.total > 1) {
    parts.push(`Occurrence ${occurrence.index} of ${occurrence.total}`);
  }

  if (row.displayLocator) {
    parts.push(row.displayLocator);
    return parts.join(" - ");
  }

  if (row.lineNumber) {
    parts.push(`Line ${row.lineNumber}`);
  }

  const member = getSourceMember(row.sourceIdentifier);
  if (member) {
    parts.push(member);
  }

  parts.push(`Evidence ${row.id.replace("relationship:", "#").replace("file:", "#")}`);
  return parts.join(" - ");
}

function getEvidenceQuality(row: RepositoryExplorerItem) {
  if (isLikelyInferredRelationship(row.relationshipKind)) {
    return { id: "inferred-link", label: "Inferred link" };
  }

  if (row.filePath || row.lineNumber) {
    return { id: "with-location", label: "Has source location" };
  }

  return { id: "source-backed", label: "Source-backed" };
}

function isLikelyInferredRelationship(relationshipKind?: string | null) {
  return relationshipKind === "matches_backend_handler"
    || relationshipKind === "implemented_by"
    || relationshipKind === "depends_on"
    || relationshipKind === "navigates_to";
}

function getRowOccurrenceMap(rows: RepositoryExplorerItem[]) {
  const groups = new Map<string, RepositoryExplorerItem[]>();
  rows.forEach((row) => {
    const key = getRowDisplayKey(row);
    groups.set(key, [...(groups.get(key) ?? []), row]);
  });

  const occurrences = new Map<string, { index: number; total: number }>();
  groups.forEach((groupRows) => {
    groupRows.forEach((row, index) => {
      occurrences.set(row.id, { index: index + 1, total: groupRows.length });
    });
  });

  return occurrences;
}

function getRowDisplayKey(row: RepositoryExplorerItem) {
  if (row.occurrenceKey) {
    return row.occurrenceKey;
  }

  return [
    row.surface,
    getRepositoryRowTitle(row),
    getRepositoryRowSubtitle(row)
  ].join("|").toLowerCase();
}

function describeSelectedEvidence(row: RepositoryExplorerItem) {
  if (row.evidenceSummary) {
    return row.evidenceSummary;
  }

  if (row.relationshipKind && row.targetIdentifier) {
    return `${formatReadableIdentifier(row.sourceIdentifier)} ${formatRelationshipLabel(row.relationshipKind).toLowerCase()} ${formatReadableIdentifier(row.targetIdentifier)}.`;
  }

  if (row.filePath) {
    return `This indexed file can be used as a starting point for source review: ${row.filePath}.`;
  }

  return "This is one indexed source record from the repository.";
}

function formatEvidenceReference(row: RepositoryExplorerItem) {
  return [
    `Evidence: ${getRepositoryRowTitle(row)}`,
    `Section: ${getSurfaceLabel(row.surface)}`,
    `Group: ${getGroupLabel(row, row.surface)}`,
    row.filePath ? `File: ${row.filePath}${row.lineNumber ? `:${row.lineNumber}` : ""}` : null,
    row.relationshipKind ? `Relationship: ${formatRelationshipLabel(row.relationshipKind)}` : null,
    `Source: ${row.sourceKind}:${row.sourceIdentifier}`,
    row.targetIdentifier ? `Target: ${row.targetKind}:${row.targetIdentifier}` : null,
    row.metadata ? `Metadata: ${row.metadata}` : null,
    "",
    row.detail
  ].filter((line): line is string => line !== null).join("\n");
}

function formatRelationshipLabel(value: string) {
  return value.replace(/_/g, " ");
}

function formatKindLabel(value: string) {
  return value.replace(/[_-]/g, " ");
}

function formatReadableIdentifier(value: string) {
  const withoutKind = value.includes(":") ? value.slice(value.indexOf(":") + 1) : value;
  const afterPath = withoutKind.split(/[\\/]/).filter(Boolean).pop() ?? withoutKind;
  const afterMember = afterPath.includes("::") ? afterPath.slice(afterPath.lastIndexOf("::") + 2) : afterPath;
  return afterMember || value;
}

function formatSourceLocation(sourceIdentifier: string, filePath?: string | null) {
  const sourceWithoutKind = sourceIdentifier.includes(":")
    ? sourceIdentifier.slice(sourceIdentifier.indexOf(":") + 1)
    : sourceIdentifier;
  const [sourcePath, sourceMember] = sourceWithoutKind.split("::");
  const displayPath = filePath ?? sourcePath;
  const fileName = displayPath.split(/[\\/]/).filter(Boolean).pop() ?? displayPath;
  const readableMember = sourceMember ? ` / ${formatReadableIdentifier(sourceMember)}` : "";
  return `${fileName}${readableMember}`;
}

function getSourceMember(sourceIdentifier: string) {
  const sourceWithoutKind = sourceIdentifier.includes(":")
    ? sourceIdentifier.slice(sourceIdentifier.indexOf(":") + 1)
    : sourceIdentifier;
  const member = sourceWithoutKind.split("::")[1];
  if (!member || member === "azure-service-reference") {
    return "";
  }

  return formatReadableIdentifier(member);
}

function getSourceLocationSummary(row: RepositoryExplorerItem) {
  if (row.filePath && row.lineNumber) {
    return `${row.filePath}:${row.lineNumber}`;
  }

  if (row.filePath) {
    return row.filePath;
  }

  const sourceWithoutKind = row.sourceIdentifier.includes(":")
    ? row.sourceIdentifier.slice(row.sourceIdentifier.indexOf(":") + 1)
    : row.sourceIdentifier;
  return sourceWithoutKind || "Indexed evidence record";
}

type RepositoryEvidenceGroup = {
  label: string;
  rows: RepositoryExplorerItem[];
  summary: string;
};

const backendGroupOrder = [
  "API to backend handoff",
  "Method calls",
  "Procedures",
  "Table reads",
  "Table writes",
  "Save changes",
  "Cloud relationships",
  "Code relationships",
  "Backend relationships"
];

function groupRows(rows: RepositoryExplorerItem[], surface: RepositoryExplorerSurface): RepositoryEvidenceGroup[] {
  const groups = new Map<string, RepositoryExplorerItem[]>();
  rows.forEach((row) => {
    const label = getGroupLabel(row, surface);
    groups.set(label, [...(groups.get(label) ?? []), row]);
  });

  return Array.from(groups, ([label, groupRows]) => ({
    label,
    rows: sortGroupRows(groupRows, surface),
    summary: getGroupSummary(surface, groupRows)
  })).sort((left, right) => compareGroups(left.label, right.label, surface));
}

function getGroupLabel(row: RepositoryExplorerItem, surface: RepositoryExplorerSurface) {
  if (surface === "files") {
    return getReadableFolder(row.filePath ?? row.sourceIdentifier);
  }

  if (surface === "backend") {
    return getBackendGroupLabel(row.relationshipKind);
  }

  if (surface === "apis") {
    return getApiGroupLabel(row);
  }

  return getAzureGroupLabel(row);
}

function getReadableFolder(value: string) {
  const withoutKind = value.includes(":") ? value.slice(value.indexOf(":") + 1) : value;
  const normalized = withoutKind.replaceAll("\\", "/");
  const parts = normalized.split("/").filter(Boolean);
  if (parts.length <= 1) {
    return "Root files";
  }

  return parts.slice(0, Math.min(2, parts.length - 1)).join("/");
}

function getBackendGroupLabel(relationshipKind?: string | null) {
  switch (relationshipKind) {
    case "handles_api":
    case "matches_backend_handler":
    case "implemented_by":
      return "API to backend handoff";
    case "calls_method":
      return "Method calls";
    case "executes_procedure":
      return "Procedures";
    case "reads_table":
      return "Table reads";
    case "writes_table":
      return "Table writes";
    case "saves_changes":
      return "Save changes";
    case "uses_azure_service":
      return "Cloud relationships";
    case "contains_symbol":
    case "depends_on":
    case "navigates_to":
    case "renders_component":
      return "Code relationships";
    default:
      return relationshipKind ? titleCase(formatRelationshipLabel(relationshipKind)) : "Backend relationships";
  }
}

function getApiGroupLabel(row: RepositoryExplorerItem) {
  const routeText = [
    row.targetIdentifier,
    row.displayTitle,
    row.title,
    row.detail
  ].filter(isPresent).join(" ");
  const routeMatch = routeText.match(/\b(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+([^\s,;"')]+)/i);
  if (routeMatch) {
    return `${routeMatch[1].toUpperCase()} ${routeMatch[2]}`;
  }

  const pathMatch = routeText.match(/\/api\/[A-Za-z0-9\-_/{}:.]+/i);
  return pathMatch ? pathMatch[0] : "API routes";
}

function getAzureGroupLabel(row: RepositoryExplorerItem) {
  if (row.targetIdentifier) {
    return formatReadableIdentifier(row.targetIdentifier);
  }

  const service = row.title.match(/\b(blob storage|blob|queue|service bus|key vault|storage account|storage|sql database|sql|cosmos|function app|function|app service)\b/i)?.[0];
  return service ? titleCase(service) : "Azure dependencies";
}

function getGroupSummary(surface: RepositoryExplorerSurface, rows: RepositoryExplorerItem[]) {
  const count = rows.length;
  if (surface === "files") {
    const extensions = uniqueSorted(rows.map((row) => getFileExtension(row.filePath ?? row.sourceIdentifier)).filter(isPresent));
    const extensionLabel = extensions.length > 0 ? ` / ${extensions.slice(0, 3).join(", ")}` : "";
    return `${count} ${pluralize(count, "file")}${extensionLabel}`;
  }

  if (surface === "apis") {
    return `${count} ${pluralize(count, "handler record")}`;
  }

  if (surface === "azure") {
    const files = new Set(rows.map((row) => row.filePath ?? row.sourceIdentifier));
    return `${count} ${pluralize(count, "usage")} in ${files.size} ${pluralize(files.size, "file")}`;
  }

  const targets = new Set(rows.map((row) => row.targetIdentifier ?? row.title));
  return `${count} ${pluralize(count, "relationship")} / ${targets.size} ${pluralize(targets.size, "target")}`;
}

function sortGroupRows(rows: RepositoryExplorerItem[], surface: RepositoryExplorerSurface) {
  return [...rows].sort((left, right) => {
    if (surface === "files") {
      return (left.filePath ?? left.title).localeCompare(right.filePath ?? right.title);
    }

    return getRepositoryRowSubtitle(left).localeCompare(getRepositoryRowSubtitle(right))
      || getRepositoryRowTitle(left).localeCompare(getRepositoryRowTitle(right));
  });
}

function compareGroups(left: string, right: string, surface: RepositoryExplorerSurface) {
  if (surface === "backend") {
    const leftIndex = backendGroupOrder.indexOf(left);
    const rightIndex = backendGroupOrder.indexOf(right);
    if (leftIndex !== -1 || rightIndex !== -1) {
      return (leftIndex === -1 ? backendGroupOrder.length : leftIndex)
        - (rightIndex === -1 ? backendGroupOrder.length : rightIndex);
    }
  }

  return left.localeCompare(right);
}

function getFileExtension(value: string) {
  const normalized = value.replaceAll("\\", "/");
  const fileName = normalized.split("/").filter(Boolean).pop() ?? "";
  const extension = fileName.includes(".") ? fileName.slice(fileName.lastIndexOf(".")).toLowerCase() : "";
  return extension || "";
}

function pluralize(count: number, singular: string) {
  return count === 1 ? singular : `${singular}s`;
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
    row.metadata,
    row.displayTitle,
    row.displaySubtitle,
    row.displayLocator,
    row.evidenceSummary,
    row.occurrenceKey
  ].filter(Boolean).join(" ").toLowerCase();
}

function getFileFilterText(row: RepositoryExplorerItem) {
  return [
    row.filePath,
    row.sourceIdentifier,
    row.targetIdentifier,
    row.displayLocator
  ].filter(Boolean).join(" ").toLowerCase();
}
