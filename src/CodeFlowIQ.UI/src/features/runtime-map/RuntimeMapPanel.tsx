import { BookOpen, Compass, FolderTree, Network, Play, RefreshCw, Search } from "lucide-react";
import { useEffect, useState } from "react";
import type { RepositoryExplorerSurface } from "../repository-explorer";
import type { RuntimeExecutionPath, RuntimeFlow, RuntimeFlowMap } from "../../types";
import "./runtime-map.css";

type RuntimeMapView = "recommended" | "starts" | "flows" | "evidence";
type RuntimeConnectionStatus = "connected" | "partial" | "detected-only";
type RuntimeEvidenceQuality = "exact" | "inferred" | "missing";

type RuntimeMapPanelProps = {
  runtimeMap: RuntimeFlowMap | null;
  disabled: boolean;
  onLoad: () => void;
  onOpenExplorer: (surface: RepositoryExplorerSurface, query?: string, selectedItemId?: string | null) => void;
};

export function RuntimeMapPanel({ runtimeMap, disabled, onLoad, onOpenExplorer }: RuntimeMapPanelProps) {
  const [selectedPathIndex, setSelectedPathIndex] = useState(0);
  const [selectedFlowIndex, setSelectedFlowIndex] = useState(0);
  const [activeRuntimeView, setActiveRuntimeView] = useState<RuntimeMapView>("recommended");
  const [startSearch, setStartSearch] = useState("");
  const [flowSearch, setFlowSearch] = useState("");
  const [flowCategoryFilter, setFlowCategoryFilter] = useState("all");

  useEffect(() => {
    if (!runtimeMap) {
      if (selectedPathIndex !== 0) {
        setSelectedPathIndex(0);
      }
      if (selectedFlowIndex !== 0) {
        setSelectedFlowIndex(0);
      }
      return;
    }

    if (selectedPathIndex >= runtimeMap.executionPaths.length) {
      setSelectedPathIndex(0);
    }

    const nextPath = runtimeMap.executionPaths[selectedPathIndex] ?? runtimeMap.executionPaths[0];
    if (nextPath && selectedFlowIndex >= nextPath.flows.length) {
      setSelectedFlowIndex(0);
    }
  }, [runtimeMap, selectedFlowIndex, selectedPathIndex]);

  if (!runtimeMap) {
    return (
      <div className="overview-empty">
        <div>
          <Play size={24} />
          <h2>Map how this repository runs</h2>
          <p>Build a runtime map to identify likely startup files, user/API entry points, backend handlers, data access, and cloud dependencies from the local index.</p>
        </div>
        <button onClick={onLoad} disabled={disabled}>
          <Network size={17} /> Build runtime map
        </button>
      </div>
    );
  }

  const selectedResolvedIndex = runtimeMap.executionPaths[selectedPathIndex] ? selectedPathIndex : 0;
  const selectedPath = runtimeMap.executionPaths[selectedResolvedIndex] ?? null;
  const selectedResolvedFlowIndex = selectedPath?.flows[selectedFlowIndex] ? selectedFlowIndex : 0;
  const selectedFlow = selectedPath?.flows[selectedResolvedFlowIndex] ?? null;
  const selectedStepCount = selectedPath?.flows.reduce((sum, flow) => sum + flow.steps.length, 0) ?? 0;
  const totalEntryCount = runtimeMap.executionPaths.length || runtimeMap.entryPoints.length;
  const indexedPaths = runtimeMap.executionPaths.map((path, index) => ({ path, index }));
  const coverage = getRuntimeCoverage(runtimeMap);
  const runnablePathIndexes = indexedPaths.filter((item) => !isDetectedOnlyExecutionPath(item.path));
  const detectedOnlyPathIndexes = indexedPaths.filter((item) => isDetectedOnlyExecutionPath(item.path));
  const normalizedStartQuery = startSearch.trim().toLowerCase();
  const filteredRunnablePathIndexes = runnablePathIndexes.filter(({ path }) => runtimePathMatchesQuery(path, normalizedStartQuery));
  const filteredDetectedOnlyPathIndexes = detectedOnlyPathIndexes.filter(({ path }) => runtimePathMatchesQuery(path, normalizedStartQuery));
  const indexedFlows = runtimeMap.executionPaths
    .map((path, index) => ({ path, index }))
    .flatMap(({ path, index: pathIndex }) => path.flows.map((flow, flowIndex) => ({ flow, flowIndex, path, pathIndex })))
    .filter(({ path }) => !isDetectedOnlyExecutionPath(path));
  const flowCategories = Array.from(new Set(indexedFlows.map(({ flow }) => flow.category))).sort();
  const normalizedFlowQuery = flowSearch.trim().toLowerCase();
  const filteredFlows = indexedFlows.filter(({ flow, path }) => (
    (flowCategoryFilter === "all" || flow.category === flowCategoryFilter)
    && runtimeFlowMatchesQuery(flow, path, normalizedFlowQuery)
  ));
  const recommendedStories = runnablePathIndexes.slice(0, 4);

  function selectRuntimePath(index: number, nextView: RuntimeMapView = "recommended") {
    setSelectedPathIndex(index);
    setSelectedFlowIndex(0);
    setActiveRuntimeView(nextView);
  }

  function selectRuntimeFlow(pathIndex: number, flowIndex: number, nextView: RuntimeMapView = "evidence") {
    setSelectedPathIndex(pathIndex);
    setSelectedFlowIndex(flowIndex);
    setActiveRuntimeView(nextView);
  }

  function openSelectedFlowInExplorer() {
    if (selectedFlow === null) {
      return;
    }

    const target = getExplorerTargetForRuntimeFlow(selectedFlow, selectedPath);
    onOpenExplorer(target.surface, target.query, target.selectedItemId);
  }

  return (
    <div className="runtime-layout">
      <section className="runtime-hero">
        <div>
          <span>{runtimeMap.kind}</span>
          <h2>Runtime Map</h2>
          <p>{runtimeMap.summary}</p>
        </div>
        <div className="runtime-hero-metrics">
          <Metric label="Start Points" value={totalEntryCount} />
          <Metric label="Flow Paths" value={runtimeMap.flows.length} />
          <Metric label="Selected Steps" value={selectedStepCount} />
        </div>
        <button onClick={onLoad} disabled={disabled}>
          <RefreshCw size={17} /> Refresh
        </button>
      </section>

      <nav className="runtime-view-nav" aria-label="Runtime Map sections">
        <button className={activeRuntimeView === "recommended" ? "active" : ""} onClick={() => setActiveRuntimeView("recommended")}>
          <Compass size={16} /> Recommended
        </button>
        <button className={activeRuntimeView === "starts" ? "active" : ""} onClick={() => setActiveRuntimeView("starts")}>
          <FolderTree size={16} /> All Start Points
        </button>
        <button className={activeRuntimeView === "flows" ? "active" : ""} disabled={indexedFlows.length === 0} onClick={() => setActiveRuntimeView("flows")}>
          <Network size={16} /> All Flows
        </button>
        <button className={activeRuntimeView === "evidence" ? "active" : ""} disabled={selectedFlow === null} onClick={() => setActiveRuntimeView("evidence")}>
          <BookOpen size={16} /> Evidence
        </button>
      </nav>

      <RuntimeCoveragePanel coverage={coverage} />

      {activeRuntimeView === "recommended" && (
        <section className="runtime-recommended-view">
          <div className="runtime-page-heading">
            <div>
              <h3>Recommended Runtime Stories</h3>
              <p>Curated connected paths first. Use All Start Points or All Flows when you need complete repository coverage.</p>
            </div>
            <button onClick={() => onOpenExplorer("files", "")}>
              <FolderTree size={16} /> Browse all evidence
            </button>
          </div>

          {recommendedStories.length === 0 ? (
            <EmptyState label="No connected runtime stories detected yet" />
          ) : (
            <div className="runtime-recommended-grid">
              <div className="runtime-story-list">
                {recommendedStories.map(({ path, index }) => (
                  <RuntimeEntryButton
                    active={index === selectedResolvedIndex}
                    index={index}
                    key={`${index}-${path.category}-${path.entryPointTitle}-${path.entryPointDetail}`}
                    path={path}
                    onSelect={(nextIndex) => selectRuntimePath(nextIndex, "recommended")}
                  />
                ))}
              </div>

              <div className="runtime-story-preview">
                {selectedPath === null || isDetectedOnlyExecutionPath(selectedPath) ? (
                  <EmptyState label="Choose a recommended story to preview its execution flow" />
                ) : (
                  <SelectedExecutionPath
                    key={`recommended-path-${selectedResolvedIndex}`}
                    path={selectedPath}
                    pathIndex={selectedResolvedIndex}
                    selectedFlowIndex={selectedResolvedFlowIndex}
                    onSelectFlow={setSelectedFlowIndex}
                    onOpenEvidence={() => setActiveRuntimeView("evidence")}
                    onOpenExplorer={onOpenExplorer}
                  />
                )}
              </div>
            </div>
          )}
        </section>
      )}

      {activeRuntimeView === "starts" && (
        <section className="runtime-starts-view">
          <div className="runtime-page-heading">
            <div>
              <h3>All Start Points</h3>
              <p>Search and group every detected runtime start point without expanding all flows at once.</p>
            </div>
            <span>{filteredRunnablePathIndexes.length + filteredDetectedOnlyPathIndexes.length} shown</span>
          </div>

          <div className="runtime-browser-toolbar">
            <label>
              Search start points
              <input value={startSearch} onChange={(event) => setStartSearch(event.target.value)} placeholder="Search title, path, category" />
            </label>
          </div>

          <div className="runtime-starts-grid">
            {runtimeMap.executionPaths.length === 0 ? (
              <EmptyState label="No runtime entry points detected yet" />
            ) : (
              <>
                <div className="runtime-path-group">
                  <div className="runtime-path-group-heading">
                    <span>Runnable Stories</span>
                    <strong>{filteredRunnablePathIndexes.length}</strong>
                  </div>
                  {filteredRunnablePathIndexes.length === 0 ? (
                    <EmptyState label="No connected execution stories yet" />
                  ) : (
                    filteredRunnablePathIndexes.map(({ path, index }) => (
                      <RuntimeEntryButton
                        active={index === selectedResolvedIndex}
                        index={index}
                        key={`${index}-${path.category}-${path.entryPointTitle}-${path.entryPointDetail}`}
                        path={path}
                        onSelect={(nextIndex) => selectRuntimePath(nextIndex, "starts")}
                      />
                    ))
                  )}
                </div>

                {detectedOnlyPathIndexes.length > 0 && (
                  <section className="runtime-path-group muted">
                    <div className="runtime-path-group-heading">
                      <span>Detected Starts</span>
                      <strong>{filteredDetectedOnlyPathIndexes.length}</strong>
                    </div>
                    <div>
                      {filteredDetectedOnlyPathIndexes.length === 0 ? (
                        <EmptyState label="No detected starts match the current search" />
                      ) : (
                        filteredDetectedOnlyPathIndexes.map(({ path, index }) => (
                          <RuntimeEntryButton
                            active={index === selectedResolvedIndex}
                            index={index}
                            key={`${index}-${path.category}-${path.entryPointTitle}-${path.entryPointDetail}`}
                            path={path}
                            onSelect={(nextIndex) => selectRuntimePath(nextIndex, "starts")}
                          />
                        ))
                      )}
                    </div>
                  </section>
                )}
              </>
            )}
          </div>

          <div className="runtime-selected-start">
            <div className="runtime-page-heading compact">
              <div>
                <h3>Selected Start Point</h3>
                <p>Inspect one start point at a time, then open its source evidence when needed.</p>
              </div>
              {selectedFlow !== null && (
                <div className="runtime-heading-actions">
                  <button onClick={() => setActiveRuntimeView("evidence")}>
                    <BookOpen size={16} /> Evidence
                  </button>
                  <button onClick={openSelectedFlowInExplorer}>
                    <Search size={16} /> Drill down
                  </button>
                </div>
              )}
            </div>
            {selectedPath === null ? (
              <EmptyState label="Select a start point to inspect its runtime story" />
            ) : (
              <SelectedExecutionPath
                key={`start-path-${selectedResolvedIndex}`}
                path={selectedPath}
                pathIndex={selectedResolvedIndex}
                selectedFlowIndex={selectedResolvedFlowIndex}
                onSelectFlow={setSelectedFlowIndex}
                onOpenEvidence={() => setActiveRuntimeView("evidence")}
                onOpenExplorer={onOpenExplorer}
              />
            )}
          </div>
        </section>
      )}

      {activeRuntimeView === "flows" && (
        <section className="runtime-all-flows-view">
          <div className="runtime-page-heading">
            <div>
              <h3>All Flows</h3>
              <p>Search and filter flow paths, then open one story or its evidence.</p>
            </div>
            <span>{filteredFlows.length} shown</span>
          </div>

          <div className="runtime-browser-toolbar two-column">
            <label>
              Search flows
              <input value={flowSearch} onChange={(event) => setFlowSearch(event.target.value)} placeholder="Search flow, start point, step, evidence" />
            </label>
            <label>
              Category
              <select value={flowCategoryFilter} onChange={(event) => setFlowCategoryFilter(event.target.value)}>
                <option value="all">All categories</option>
                {flowCategories.map((category) => (
                  <option value={category} key={category}>{formatRuntimeCategory(category)}</option>
                ))}
              </select>
            </label>
          </div>

          {filteredFlows.length === 0 ? (
            <EmptyState label="No runtime flows match the current filters" />
          ) : (
            <div className="runtime-flow-browser-list">
              {filteredFlows.map(({ flow, flowIndex, path, pathIndex }) => (
                <article className="runtime-flow-browser-card" key={`${pathIndex}-${flowIndex}-${flow.title}`}>
                  <div>
                    <span>{formatRuntimeCategory(flow.category)}</span>
                    <strong>{flow.title}</strong>
                    <small className={`runtime-status-badge ${getRuntimeFlowEvidenceQuality(flow)}`}>
                      {formatRuntimeEvidenceQuality(getRuntimeFlowEvidenceQuality(flow))}
                    </small>
                    <small>{path.entryPointTitle}</small>
                    <p>{flow.summary}</p>
                  </div>
                  <div className="runtime-flow-browser-meta">
                    <code>{path.entryPointDetail}</code>
                    <small>{flow.confidence}% confidence - {flow.steps.length} steps</small>
                    <div>
                      <button onClick={() => selectRuntimeFlow(pathIndex, flowIndex, "recommended")}>
                        <Play size={15} /> Story
                      </button>
                      <button onClick={() => selectRuntimeFlow(pathIndex, flowIndex, "evidence")}>
                        <BookOpen size={15} /> Evidence
                      </button>
                      <button onClick={() => {
                        const target = getExplorerTargetForRuntimeFlow(flow, path);
                        onOpenExplorer(target.surface, target.query, target.selectedItemId);
                      }}>
                        <Search size={15} /> Drill down
                      </button>
                    </div>
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>
      )}

      {activeRuntimeView === "evidence" && (
        <section className="runtime-evidence-view">
          <div className="runtime-page-heading">
            <div>
              <h3>Evidence</h3>
              <p>Review source-backed details for one selected flow.</p>
            </div>
            <button onClick={() => setActiveRuntimeView("recommended")} disabled={selectedPath === null}>
              <Play size={16} /> Back to story
            </button>
            <button onClick={openSelectedFlowInExplorer} disabled={selectedFlow === null}>
              <Search size={16} /> Open in Explorer
            </button>
          </div>
          <EvidenceDrawer key={`evidence-${selectedResolvedIndex}-${selectedResolvedFlowIndex}`} title={selectedFlow?.title ?? "Flow Evidence"} flows={selectedFlow ? [selectedFlow] : []} />
        </section>
      )}
    </div>
  );
}

function Metric({ label, value }: { label: string; value?: number | string }) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value ?? "--"}</strong>
    </div>
  );
}

function EmptyState({ label }: { label: string }) {
  return <div className="empty-state">{label}</div>;
}

function RuntimeCoveragePanel({ coverage }: { coverage: ReturnType<typeof getRuntimeCoverage> }) {
  return (
    <section className="runtime-coverage-panel" aria-label="Runtime Map quality and coverage">
      <div className="runtime-coverage-heading">
        <div>
          <span>Quality + Coverage</span>
          <p>{coverage.summary}</p>
        </div>
        <strong>{coverage.connectedStartPoints}/{coverage.startPoints} connected</strong>
      </div>
      <div className="runtime-coverage-grid">
        <RuntimeCoverageMetric label="Connected starts" value={coverage.connectedStartPoints} total={coverage.startPoints} tone="good" />
        <RuntimeCoverageMetric label="Partial starts" value={coverage.partialStartPoints} total={coverage.startPoints} tone="warn" />
        <RuntimeCoverageMetric label="Detected only" value={coverage.detectedOnlyStartPoints} total={coverage.startPoints} tone="muted" />
        <RuntimeCoverageMetric label="Reach API" value={coverage.flowsReachingApi} total={coverage.flowCount} tone="api" />
        <RuntimeCoverageMetric label="Reach backend" value={coverage.flowsReachingBackend} total={coverage.flowCount} tone="backend" />
        <RuntimeCoverageMetric label="Reach SQL/data" value={coverage.flowsReachingData} total={coverage.flowCount} tone="database" />
        <RuntimeCoverageMetric label="Reach cloud" value={coverage.flowsReachingCloud} total={coverage.flowCount} tone="cloud" />
      </div>
    </section>
  );
}

function RuntimeCoverageMetric({ label, value, total, tone }: { label: string; value: number; total: number; tone: string }) {
  const percent = total <= 0 ? 0 : Math.round((value / total) * 100);
  return (
    <article className={`runtime-coverage-metric ${tone}`}>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
      </div>
      <small>{percent}% of {total}</small>
    </article>
  );
}

function RuntimeEntryButton({ active, index, path, onSelect }: { active: boolean; index: number; path: RuntimeExecutionPath; onSelect: (index: number) => void }) {
  const detectedOnly = isDetectedOnlyExecutionPath(path);
  const status = getRuntimePathStatus(path);
  return (
    <button className={`${active ? "runtime-entry-button active" : "runtime-entry-button"}${detectedOnly ? " detected-only" : ""}`} onClick={() => onSelect(index)}>
      <span>{formatRuntimeCategory(path.category)}</span>
      <strong>{path.entryPointTitle}</strong>
      <code title={path.entryPointDetail}>{path.entryPointDetail}</code>
      <small className={`runtime-status-badge ${status}`}>{formatRuntimeStatus(status)}</small>
      <small>{detectedOnly ? "Needs connections" : `${path.flows.length} flows`}</small>
    </button>
  );
}

function SelectedExecutionPath({ path, pathIndex, selectedFlowIndex, onSelectFlow, onOpenEvidence, onOpenExplorer }: {
  path: RuntimeExecutionPath;
  pathIndex: number;
  selectedFlowIndex: number;
  onSelectFlow: (index: number) => void;
  onOpenEvidence: () => void;
  onOpenExplorer: (surface: RepositoryExplorerSurface, query?: string, selectedItemId?: string | null) => void;
}) {
  const selectedResolvedFlowIndex = path.flows[selectedFlowIndex] ? selectedFlowIndex : 0;
  const selectedFlow = path.flows[selectedResolvedFlowIndex] ?? null;
  const detectedOnly = isDetectedOnlyExecutionPath(path);
  const pathStatus = getRuntimePathStatus(path);

  return (
    <div className="selected-execution">
      <div className="selected-execution-header">
        <div>
          <span>{formatRuntimeCategory(path.category)}</span>
          <h3>{path.entryPointTitle}</h3>
          <p>{path.summary}</p>
        </div>
        <code>{path.entryPointDetail}</code>
      </div>

      <RuntimeQualityNote path={path} flow={selectedFlow} status={pathStatus} />

      {detectedOnly && (
        <div className="runtime-unconnected-state">
          <strong>Detected start point</strong>
          <p>CodeFlowIQ found this runtime start file, but the current index does not connect it to an executable frontend, API, database, or cloud flow yet.</p>
        </div>
      )}

      {!detectedOnly && path.flows.length > 1 && (
        <div className="runtime-flow-picker" aria-label="Runtime flows for selected entry point">
          <div className="runtime-flow-picker-heading">
            <span>Flows under this start point</span>
            <strong>{path.flows.length}</strong>
          </div>
          <div className="runtime-flow-tabs">
            {path.flows.map((flow, index) => (
              <button className={index === selectedResolvedFlowIndex ? "active" : ""} key={`${flow.title}-${flow.category}-${index}`} onClick={() => onSelectFlow(index)}>
                <span>{formatRuntimeCategory(flow.category)}</span>
                <strong>{flow.title}</strong>
                <small className={`runtime-status-badge ${getRuntimeFlowEvidenceQuality(flow)}`}>{formatRuntimeEvidenceQuality(getRuntimeFlowEvidenceQuality(flow))}</small>
                <small>{flow.confidence}% confidence - {flow.steps.length} steps</small>
              </button>
            ))}
          </div>
        </div>
      )}

      {selectedFlow === null ? (
        <EmptyState label="No flows connected to this entry point yet" />
      ) : (
        <>
          <div className="runtime-story-actions">
            <button onClick={onOpenEvidence}>
              <BookOpen size={16} /> Open evidence
            </button>
            <button onClick={() => {
              const target = getExplorerTargetForRuntimeFlow(selectedFlow, path);
              onOpenExplorer(target.surface, target.query, target.selectedItemId);
            }}>
              <Search size={16} /> Drill down
            </button>
          </div>
          <RuntimeFlowDetail key={`flow-${pathIndex}-${selectedResolvedFlowIndex}`} flow={selectedFlow} />
        </>
      )}
    </div>
  );
}

function RuntimeQualityNote({ path, flow, status }: { path: RuntimeExecutionPath; flow: RuntimeFlow | null; status: RuntimeConnectionStatus }) {
  const quality = flow === null ? "missing" : getRuntimeFlowEvidenceQuality(flow);
  return (
    <section className="runtime-quality-note">
      <div>
        <span className={`runtime-status-badge ${status}`}>{formatRuntimeStatus(status)}</span>
        <span className={`runtime-status-badge ${quality}`}>{formatRuntimeEvidenceQuality(quality)}</span>
      </div>
      <p>{getRuntimeQualityMessage(path, flow, status, quality)}</p>
    </section>
  );
}

function RuntimeFlowDetail({ flow }: { flow: RuntimeFlow }) {
  const stages = getRuntimeStages(flow);

  return (
    <section className="runtime-flow-detail">
      <div className="execution-flow-heading">
        <div>
          <strong>{flow.title}</strong>
          <span>{formatRuntimeCategory(flow.category)} - {flow.confidence}% confidence</span>
        </div>
        <small>{flow.steps.length} steps</small>
      </div>

      <div className="flow-explanation">
        <div className="runtime-stage-strip" aria-label="Runtime stages in this flow">
          {stages.map((stage) => (
            <span className={`stage-chip ${stageClass(stage)}`} key={stage}>{formatRuntimeCategory(stage)}</span>
          ))}
        </div>
        <strong>Execution Story</strong>
        <p>{explainRuntimeFlow(flow)}</p>
      </div>

      <ol className="runtime-timeline">
        {flow.steps.map((step, stepIndex) => (
          <li className={`runtime-step ${stageClass(step.stage)}`} key={`${flow.title}-${step.title}-${stepIndex}`}>
            <div className="timeline-marker">
              <span>{stepIndex + 1}</span>
            </div>
            <article>
              <div className="runtime-step-heading">
                <span>{formatRuntimeCategory(step.stage)}</span>
                <small>{step.kind}</small>
              </div>
              <strong>{step.title}</strong>
              <details>
                <summary>Evidence</summary>
                <code>{step.detail}</code>
              </details>
            </article>
          </li>
        ))}
      </ol>
    </section>
  );
}

function EvidenceDrawer({ title, flows }: { title: string; flows: RuntimeFlow[] }) {
  return (
    <section className="runtime-evidence-drawer">
      <div className="runtime-evidence-header">
        <div>
          <span>{title}</span>
          <small>Source-backed flow details</small>
        </div>
        <strong>{flows.length}</strong>
      </div>
      <div className="runtime-flow-list">
        {flows.length === 0 ? (
          <EmptyState label="No runtime flows detected yet" />
        ) : (
          flows.map((flow, index) => (
            <details className="runtime-flow-card" open={index === 0} key={`${flow.category}-${flow.title}-${index}`}>
              <summary>
                <span>{flow.title}</span>
                <strong>{formatRuntimeCategory(flow.category)} - {flow.confidence}%</strong>
              </summary>
              <p>{flow.summary}</p>
              <ol>
                {flow.steps.map((step, stepIndex) => (
                  <li key={`${flow.title}-${step.title}-${stepIndex}`}>
                    <span>{formatRuntimeCategory(step.stage)}</span>
                    <div>
                      <strong>{step.title}</strong>
                      <small>{step.kind}</small>
                      <code>{step.detail}</code>
                    </div>
                  </li>
                ))}
              </ol>
            </details>
          ))
        )}
      </div>
    </section>
  );
}

function isDetectedOnlyExecutionPath(path: RuntimeExecutionPath) {
  return path.flows.length === 1
    && path.flows[0].steps.length === 1
    && path.flows[0].category === path.category;
}

function getRuntimeCoverage(runtimeMap: RuntimeFlowMap) {
  const paths = runtimeMap.executionPaths;
  const allPathFlows = paths.flatMap((path) => path.flows);
  const uniqueFlows = allPathFlows.length > 0 ? allPathFlows : runtimeMap.flows;
  const connectedStartPoints = paths.filter((path) => getRuntimePathStatus(path) === "connected").length;
  const partialStartPoints = paths.filter((path) => getRuntimePathStatus(path) === "partial").length;
  const detectedOnlyStartPoints = paths.filter((path) => getRuntimePathStatus(path) === "detected-only").length;
  const flowsReachingApi = uniqueFlows.filter((flow) => runtimeFlowTouchesLayer(flow, ["api", "http"])).length;
  const flowsReachingBackend = uniqueFlows.filter((flow) => runtimeFlowTouchesLayer(flow, ["backend", "handler"])).length;
  const flowsReachingData = uniqueFlows.filter((flow) => runtimeFlowTouchesLayer(flow, ["database", "procedure", "sql", "persistence", "table"])).length;
  const flowsReachingCloud = uniqueFlows.filter((flow) => runtimeFlowTouchesLayer(flow, ["cloud", "azure", "queue", "blob", "service bus", "function"])).length;
  const startPoints = paths.length || runtimeMap.entryPoints.length;
  const flowCount = uniqueFlows.length;

  return {
    startPoints,
    flowCount,
    connectedStartPoints,
    partialStartPoints,
    detectedOnlyStartPoints,
    flowsReachingApi,
    flowsReachingBackend,
    flowsReachingData,
    flowsReachingCloud,
    summary: buildRuntimeCoverageSummary(startPoints, connectedStartPoints, partialStartPoints, detectedOnlyStartPoints, flowsReachingData, flowCount)
  };
}

function getRuntimePathStatus(path: RuntimeExecutionPath): RuntimeConnectionStatus {
  if (isDetectedOnlyExecutionPath(path)) {
    return "detected-only";
  }

  const hasData = path.flows.some((flow) => runtimeFlowTouchesLayer(flow, ["database", "procedure", "sql", "persistence", "table"]));
  const hasApiOrBackend = path.flows.some((flow) => runtimeFlowTouchesLayer(flow, ["api", "http", "backend", "handler"]));
  return hasData || hasApiOrBackend ? "connected" : "partial";
}

function getRuntimeFlowEvidenceQuality(flow: RuntimeFlow): RuntimeEvidenceQuality {
  if (flow.steps.length <= 1 || flow.category === "backend-startup") {
    return "missing";
  }

  if (flow.category === "backend-data") {
    return "inferred";
  }

  const hasDirectEvidence = flow.steps.some((step) => (
    step.stage.toLowerCase().includes("http")
    || step.stage.toLowerCase().includes("backend")
    || step.stage.toLowerCase().includes("database")
    || step.stage.toLowerCase().includes("procedure")
    || step.stage.toLowerCase().includes("cloud")
  ));
  return hasDirectEvidence ? "exact" : "inferred";
}

function runtimeFlowTouchesLayer(flow: RuntimeFlow, terms: string[]) {
  return flow.steps.some((step) => {
    const searchable = `${step.stage} ${step.kind} ${step.title} ${step.detail}`.toLowerCase();
    return terms.some((term) => searchable.includes(term));
  });
}

function getExplorerTargetForRuntimeFlow(flow: RuntimeFlow, path: RuntimeExecutionPath | null): {
  surface: RepositoryExplorerSurface;
  query: string;
  selectedItemId: string | null;
} {
  const metadataStep = [...flow.steps].reverse().find((step) => step.explorerSurface && (step.repositoryExplorerItemId || step.explorerQuery));
  if (metadataStep?.explorerSurface && (metadataStep.explorerQuery || metadataStep.repositoryExplorerItemId)) {
    return {
      surface: metadataStep.explorerSurface,
      query: metadataStep.explorerQuery ?? getRuntimeExplorerQuery(metadataStep, flow, path),
      selectedItemId: metadataStep.repositoryExplorerItemId ?? null
    };
  }

  const targetStep = [...flow.steps].reverse().find((step) => {
    const searchable = `${step.stage} ${step.kind} ${step.title} ${step.detail}`.toLowerCase();
    return searchable.includes("azure")
      || searchable.includes("cloud")
      || searchable.includes("api")
      || searchable.includes("http")
      || searchable.includes("database")
      || searchable.includes("procedure")
      || searchable.includes("table")
      || searchable.includes("sql");
  }) ?? flow.steps[flow.steps.length - 1];
  const searchable = targetStep ? `${targetStep.stage} ${targetStep.kind} ${targetStep.title} ${targetStep.detail}`.toLowerCase() : "";
  const surface: RepositoryExplorerSurface = searchable.includes("azure") || searchable.includes("cloud")
    ? "azure"
    : "backend";

  return {
    surface,
    query: getRuntimeExplorerQuery(targetStep, flow, path),
    selectedItemId: targetStep?.repositoryExplorerItemId ?? null
  };
}

function getRuntimeExplorerQuery(step: RuntimeFlow["steps"][number] | undefined, flow: RuntimeFlow, path: RuntimeExecutionPath | null) {
  const candidates = [
    step?.detail,
    step?.title,
    flow.title,
    path?.entryPointDetail,
    path?.entryPointTitle
  ];

  return candidates
    .map((value) => sanitizeRuntimeExplorerQuery(value ?? ""))
    .find((value) => value.length > 0) ?? "";
}

function sanitizeRuntimeExplorerQuery(value: string) {
  const normalized = value
    .replace(/^(method|class|route|api|table|procedure|file|symbol):/i, "")
    .replace(/\.(cs|ts|tsx|jsx|sql|json)$/i, "")
    .trim();
  const qualifiedIndex = normalized.lastIndexOf("::");
  const member = qualifiedIndex >= 0 ? normalized.slice(qualifiedIndex + 2) : normalized;
  return member.split(/[\\/]/).filter(Boolean).pop() ?? member;
}

function buildRuntimeCoverageSummary(startPoints: number, connected: number, partial: number, detectedOnly: number, dataFlows: number, flowCount: number) {
  if (startPoints === 0) {
    return "No runtime start points have been detected yet.";
  }

  if (detectedOnly > 0) {
    return `${connected} start points are connected, ${partial} are partial, and ${detectedOnly} still need downstream evidence. ${dataFlows} of ${flowCount} flows reach SQL/data.`;
  }

  return `${connected} start points are connected. ${dataFlows} of ${flowCount} flows reach SQL/data.`;
}

function formatRuntimeStatus(status: RuntimeConnectionStatus) {
  return status === "connected" ? "Connected" : status === "partial" ? "Partially connected" : "Detected only";
}

function formatRuntimeEvidenceQuality(quality: RuntimeEvidenceQuality) {
  return quality === "exact" ? "Exact evidence" : quality === "inferred" ? "Inferred link" : "Missing link";
}

function getRuntimeQualityMessage(path: RuntimeExecutionPath, flow: RuntimeFlow | null, status: RuntimeConnectionStatus, quality: RuntimeEvidenceQuality) {
  if (status === "detected-only") {
    return "CodeFlowIQ found the start file, but the current index has not found a downstream runtime relationship from it yet.";
  }

  if (flow === null) {
    return "Select a flow to inspect its evidence quality.";
  }

  if (quality === "inferred") {
    return "This flow is connected by a project/domain fallback. Treat it as a useful lead, then inspect the evidence before relying on it as an exact call chain.";
  }

  if (runtimeFlowTouchesLayer(flow, ["database", "procedure", "sql", "table"])) {
    return "This flow has source-backed runtime steps and reaches SQL, procedures, or database tables.";
  }

  return `${path.entryPointTitle} is connected through source-backed runtime steps, but this selected flow does not yet reach SQL/data evidence.`;
}

function runtimePathMatchesQuery(path: RuntimeExecutionPath, query: string) {
  if (!query) {
    return true;
  }

  return [
    path.category,
    path.entryPointTitle,
    path.entryPointDetail,
    path.summary,
    ...path.flows.map((flow) => flow.title)
  ].join(" ").toLowerCase().includes(query);
}

function runtimeFlowMatchesQuery(flow: RuntimeFlow, path: RuntimeExecutionPath, query: string) {
  if (!query) {
    return true;
  }

  return [
    flow.category,
    flow.title,
    flow.summary,
    path.entryPointTitle,
    path.entryPointDetail,
    ...flow.steps.flatMap((step) => [step.stage, step.kind, step.title, step.detail])
  ].join(" ").toLowerCase().includes(query);
}

function explainRuntimeFlow(flow: RuntimeFlow) {
  const steps = flow.steps;
  if (steps.length === 0) {
    return `${flow.title} was detected, but no ordered runtime steps are available yet.`;
  }

  const phrases = steps.map((step, index) => {
    const prefix = index === 0 ? "starts at" : stageVerb(step.stage);
    return `${prefix} ${step.title}`;
  });

  return `${flow.title} ${phrases.join(", then ")}.`;
}

function stageVerb(stage: string) {
  const lower = stage.toLowerCase();
  if (lower.includes("route")) return "routes to";
  if (lower.includes("ui")) return "invokes";
  if (lower.includes("frontend")) return "runs";
  if (lower.includes("http") || lower.includes("api")) return "calls";
  if (lower.includes("backend")) return "enters";
  if (lower.includes("database") || lower.includes("procedure") || lower.includes("persistence")) return "touches";
  if (lower.includes("cloud")) return "uses";
  if (lower.includes("navigation")) return "navigates to";
  return "continues to";
}

function formatRuntimeCategory(value: string) {
  return value
    .replace(/[-_]+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function getRuntimeStages(flow: RuntimeFlow) {
  const stages: string[] = [];
  for (const step of flow.steps) {
    const stage = step.stage || "unknown";
    if (!stages.some((item) => stageClass(item) === stageClass(stage))) {
      stages.push(stage);
    }
  }

  return stages;
}

function stageClass(stage: string) {
  const lower = stage.toLowerCase();
  if (lower.includes("route") || lower.includes("ui") || lower.includes("frontend") || lower.includes("navigation")) {
    return "frontend";
  }

  if (lower.includes("http") || lower.includes("api")) {
    return "api";
  }

  if (lower.includes("database") || lower.includes("procedure") || lower.includes("persistence")) {
    return "database";
  }

  if (lower.includes("cloud")) {
    return "azure";
  }

  return "backend";
}
