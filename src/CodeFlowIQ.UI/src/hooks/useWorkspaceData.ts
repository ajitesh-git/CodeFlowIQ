import { useEffect, useMemo, useState } from "react";
import { getDefaultRuntimeConnection } from "../runtime";
import { createWorkspaceApi } from "../services/workspaceApi";
import type {
  ApiHealth,
  CSharpBackendTrace,
  CSharpTracePreferences,
  IndexingJobStatus,
  OverviewSection,
  RepositoryExplorerRelatedGroup,
  RepositoryOverview,
  RepositoryOverviewItem,
  RuntimeFlowMap,
  WorkspaceSummary
} from "../types";
import type { AppPanel, OverviewNavigation } from "../features/overview";
import type { ExplorerDrillTarget, RepositoryExplorerRows, RepositoryExplorerSurface } from "../features/repository-explorer";
import { getOverviewItemNavigation, getOverviewSectionNavigation } from "../features/overview/overviewRouting";

const defaultRuntimeConnection = getDefaultRuntimeConnection();
const defaultWorkspacePath = localStorage.getItem("codeflowiq.workspacePath") ?? "";
type ThemeMode = "light" | "dark";
const defaultTheme = (localStorage.getItem("codeflowiq.theme") === "dark" ? "dark" : "light") satisfies ThemeMode;
const defaultTracePreferences: CSharpTracePreferences = {
  defaultDepth: readNumberSetting("codeflowiq.csharpTrace.defaultDepth", 80, 8, 200),
  showFrameworkCalls: localStorage.getItem("codeflowiq.csharpTrace.showFrameworkCalls") === "true",
  showBoundaryCalls: localStorage.getItem("codeflowiq.csharpTrace.showBoundaryCalls") !== "false"
};
const defaultChainLimit = 1000;
const defaultApiRouteLimit = 1000;
const indexingPollDelayMs = 1000;
const emptyRepositoryExplorerRows: RepositoryExplorerRows = {
  files: [],
  apis: [],
  backend: [],
  azure: []
};

export function useWorkspaceData() {
  const [theme, setTheme] = useState<ThemeMode>(defaultTheme);
  const [apiBaseUrl, setApiBaseUrl] = useState(defaultRuntimeConnection.baseUrl);
  const [apiSource, setApiSource] = useState(defaultRuntimeConnection.source);
  const [workspacePath, setWorkspacePath] = useState(defaultWorkspacePath);
  const [health, setHealth] = useState<ApiHealth | null>(null);
  const [overview, setOverview] = useState<RepositoryOverview | null>(null);
  const [runtimeMap, setRuntimeMap] = useState<RuntimeFlowMap | null>(null);
  const [summary, setSummary] = useState<WorkspaceSummary | null>(null);
  const [apiFilter, setApiFilter] = useState("");
  const [targetFilter, setTargetFilter] = useState("");
  const [chains, setChains] = useState<string[]>([]);
  const [csharpTraceEntry, setCSharpTraceEntry] = useState("");
  const [csharpTraceDepth, setCSharpTraceDepth] = useState(defaultTracePreferences.defaultDepth);
  const [csharpTrace, setCSharpTrace] = useState<CSharpBackendTrace | null>(null);
  const [csharpTraceEntries, setCSharpTraceEntries] = useState<string[]>([]);
  const [csharpTracePreferences, setCSharpTracePreferences] =
    useState<CSharpTracePreferences>(defaultTracePreferences);
  const [backendRows, setBackendRows] = useState<string[]>([]);
  const [apiRows, setApiRows] = useState<string[]>([]);
  const [azureRows, setAzureRows] = useState<string[]>([]);
  const [fileRows, setFileRows] = useState<string[]>([]);
  const [repositoryExplorerSurface, setRepositoryExplorerSurface] = useState<RepositoryExplorerSurface>("files");
  const [repositoryExplorerQuery, setRepositoryExplorerQuery] = useState("");
  const [repositoryExplorerSelectedItemId, setRepositoryExplorerSelectedItemId] = useState<string | null>(null);
  const [repositoryExplorerOrigin, setRepositoryExplorerOrigin] = useState("Browse evidence");
  const [repositoryExplorerRows, setRepositoryExplorerRows] =
    useState<RepositoryExplorerRows>(emptyRepositoryExplorerRows);
  const [azureFilter, setAzureFilter] = useState("");
  const [fileLanguageFilter, setFileLanguageFilter] = useState("");
  const [fileFolderFilter, setFileFolderFilter] = useState("");
  const [chainLimit, setChainLimit] = useState(defaultChainLimit);
  const [activePanel, setActivePanel] = useState<AppPanel>("overview");
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string>("Ready");
  const [indexingStatus, setIndexingStatus] = useState<IndexingJobStatus | null>(null);
  const [indexingRequestActive, setIndexingRequestActive] = useState(false);

  const normalizedApiBaseUrl = useMemo(() => apiBaseUrl.trim().replace(/\/$/, ""), [apiBaseUrl]);
  const api = useMemo(() => createWorkspaceApi(normalizedApiBaseUrl), [normalizedApiBaseUrl]);
  const canQueryWorkspace = workspacePath.trim().length > 0 && health?.status === "healthy";
  const isIndexingActive = indexingRequestActive
    || indexingStatus?.state === "queued"
    || indexingStatus?.state === "running"
    || indexingStatus?.state === "cancelling";
  const isBusy = busy !== null;

  useEffect(() => {
    void checkHealth();
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem("codeflowiq.theme", theme);
  }, [theme]);

  useEffect(() => {
    localStorage.setItem("codeflowiq.csharpTrace.defaultDepth", String(csharpTracePreferences.defaultDepth));
    localStorage.setItem("codeflowiq.csharpTrace.showFrameworkCalls", String(csharpTracePreferences.showFrameworkCalls));
    localStorage.setItem("codeflowiq.csharpTrace.showBoundaryCalls", String(csharpTracePreferences.showBoundaryCalls));
  }, [csharpTracePreferences]);

  async function runTask<T>(label: string, task: () => Promise<T>): Promise<T | null> {
    setBusy(label);
    setMessage(label);
    try {
      const result = await task();
      setMessage("Ready");
      return result;
    } catch (error) {
      const text = error instanceof Error ? error.message : "Unexpected error";
      setMessage(text);
      return null;
    } finally {
      setBusy(null);
    }
  }

  async function checkHealth() {
    const result = await runTask("Checking API", api.checkHealth);
    if (result) {
      setHealth(result);
      setApiSource("saved");
      localStorage.setItem("codeflowiq.apiBaseUrl", normalizedApiBaseUrl);
    } else {
      setHealth(null);
    }
  }

  async function initializeWorkspace() {
    const path = workspacePath.trim();
    await startWorkspaceIndexing("Indexing workspace", path, () => api.initializeWorkspace(path));
  }

  async function syncWorkspace() {
    const path = workspacePath.trim();
    await startWorkspaceIndexing("Refreshing index", path, () => api.syncWorkspace(path));
  }

  async function startWorkspaceIndexing(label: string, path: string, start: () => Promise<unknown>) {
    setIndexingRequestActive(true);
    setBusy(label);
    setMessage(label);
    try {
      const result = await start();
      localStorage.setItem("codeflowiq.workspacePath", path);

      if (!isIndexingJobStatus(result)) {
        setIndexingStatus(null);
        setMessage("Loading insights from the existing index");
        await loadWorkspacePanels();
        setMessage("Ready");
        return;
      }

      const completedStatus = await pollIndexingUntilComplete(path, result);
      if (completedStatus.state === "failed" || completedStatus.state === "cancelled") {
        setMessage(completedStatus.error || completedStatus.message || "Indexing failed");
        return;
      }

      setMessage("Index ready. Loading insights");
      await loadWorkspacePanels();
      setMessage("Ready");
    } catch (error) {
      const text = error instanceof Error ? error.message : "Unexpected error";
      setMessage(text);
    } finally {
      setIndexingRequestActive(false);
      setBusy(null);
    }
  }

  async function pollIndexingUntilComplete(path: string, firstStatus: IndexingJobStatus) {
    let status = firstStatus;
    setIndexingStatus(status);
    setMessage(formatIndexingMessage(status));

    while (status.state === "queued" || status.state === "running" || status.state === "cancelling") {
      await delay(indexingPollDelayMs);
      status = await api.loadIndexingStatus(path);
      setIndexingStatus(status);
      setMessage(formatIndexingMessage(status));
    }

    return status;
  }

  async function cancelIndexing() {
    const path = workspacePath.trim();
    setMessage("Cancelling indexing");
    try {
      const status = await api.cancelIndexing(path);
      setIndexingStatus(status);
      setMessage(formatIndexingMessage(status));
    } catch (error) {
      const text = error instanceof Error ? error.message : "Unable to cancel indexing";
      setMessage(text);
    }
  }

  async function retryIndexing() {
    await syncWorkspace();
  }

  async function loadSummary() {
    const result = await runTask("Loading summary", () => api.loadSummary(workspacePath));
    if (result) {
      setSummary(result);
    }
  }

  async function loadOverview() {
    const result = await runTask("Loading overview", () => api.loadOverview(workspacePath));
    if (result) {
      setOverview(result);
    }
  }

  async function loadRuntimeMap() {
    const result = await runTask("Loading runtime map", () => api.loadRuntimeMap(workspacePath));
    if (result) {
      setRuntimeMap(result);
    }
  }

  async function loadChains(apiOverride = apiFilter, targetOverride = targetFilter, takeOverride = chainLimit) {
    const result = await runTask("Loading chains", () =>
      api.loadChains(workspacePath, apiOverride, targetOverride, takeOverride)
    );
    if (result) {
      setChains(result);
    }
  }

  async function loadCSharpTrace(entryOverride?: string, depthOverride = csharpTraceDepth) {
    const normalizedEntry = (entryOverride ?? csharpTraceEntry).trim();
    if (!normalizedEntry) {
      setMessage("Enter an API route or method to trace.");
      return;
    }

    const result = await runTask("Tracing C# backend path", () =>
      api.loadCSharpBackendTrace(workspacePath, normalizedEntry, depthOverride)
    );
    if (result) {
      setCSharpTrace(result);
    }
  }

  function updateCSharpTracePreferences(next: Partial<CSharpTracePreferences>) {
    setCSharpTracePreferences((current) => {
      const merged = {
        ...current,
        ...next,
        defaultDepth: Math.min(200, Math.max(8, Math.round(next.defaultDepth ?? current.defaultDepth)))
      };
      setCSharpTraceDepth(merged.defaultDepth);
      return merged;
    });
  }

  async function loadCSharpTraceEntries(take = 5000) {
    const result = await runTask("Loading C# routes and methods", () =>
      api.loadCSharpTraceEntries(workspacePath, take)
    );
    if (result) {
      setCSharpTraceEntries(result);
    }
  }

  async function loadBackendRows(kind = "executes_procedure", targetOverride = "", take = 40) {
    const result = await runTask("Loading backend relationships", () =>
      api.loadBackendRows(workspacePath, kind, targetOverride, take)
    );
    if (result) {
      setBackendRows(result);
    }
  }

  async function loadApiRows(routeOverride = apiFilter, take = 80) {
    const result = await runTask("Loading APIs", () => api.loadApiRows(workspacePath, routeOverride, take));
    if (result) {
      setApiRows(result);
    }
  }

  async function loadAzureRows(serviceOverride = azureFilter, take = 80) {
    const result = await runTask("Loading Azure dependencies", () => api.loadAzureRows(workspacePath, serviceOverride, take));
    if (result) {
      setAzureRows(result);
    }
  }

  async function loadFileRows(languageOverride = fileLanguageFilter, folderOverride = fileFolderFilter, take = 120) {
    const result = await runTask("Loading files", () =>
      api.loadFileRows(workspacePath, languageOverride, folderOverride, take)
    );
    if (result) {
      setFileRows(result);
    }
  }

  async function loadRepositoryExplorerSurface(surface = repositoryExplorerSurface, selectedItemId = repositoryExplorerSelectedItemId) {
    const result = await runTask(`Loading ${surface} explorer`, () => loadRepositoryExplorerRows(surface, selectedItemId));
    if (result) {
      setRepositoryExplorerRows((current) => ({ ...current, [surface]: result }));
    }
  }

  async function loadRepositoryExplorerAll() {
    const result = await runTask("Loading repository explorer", async () => {
      const [files, apis, backend, azure] = await Promise.all([
        loadRepositoryExplorerRows("files"),
        loadRepositoryExplorerRows("apis"),
        loadRepositoryExplorerRows("backend"),
        loadRepositoryExplorerRows("azure")
      ]);

      return { files, apis, backend, azure } satisfies RepositoryExplorerRows;
    });
    if (result) {
      setRepositoryExplorerRows(result);
    }
  }

  function openRepositoryExplorer(
    surface: RepositoryExplorerSurface,
    query = "",
    selectedItemId: string | null = null,
    originLabel = "Browse evidence"
  ) {
    if (!canQueryWorkspace) {
      return;
    }

    setRepositoryExplorerSurface(surface);
    setRepositoryExplorerQuery(query);
    setRepositoryExplorerSelectedItemId(selectedItemId);
    setRepositoryExplorerOrigin(originLabel);
    setActivePanel("explorer");
    if (repositoryExplorerRows[surface].length === 0
      || (selectedItemId && !repositoryExplorerRows[surface].some((row) => row.id === selectedItemId))) {
      void loadRepositoryExplorerSurface(surface, selectedItemId);
    }
  }

  function openExplorerTarget(target: ExplorerDrillTarget, fallbackOriginLabel = "Preview") {
    openRepositoryExplorer(
      target.surface,
      target.query,
      target.selectedItemId ?? null,
      target.originLabel ?? fallbackOriginLabel
    );
  }

  function changeRepositoryExplorerSurface(surface: RepositoryExplorerSurface) {
    setRepositoryExplorerSurface(surface);
    setRepositoryExplorerQuery("");
    setRepositoryExplorerSelectedItemId(null);
  }

  function loadRepositoryExplorerRows(surface: RepositoryExplorerSurface, selectedItemId: string | null = null) {
    return api.loadExplorerItems(workspacePath, surface, "", 1000, selectedItemId);
  }

  function loadRepositoryExplorerRelatedEvidence(surface: RepositoryExplorerSurface, itemId: string): Promise<RepositoryExplorerRelatedGroup[]> {
    if (!canQueryWorkspace) {
      return Promise.resolve([]);
    }

    return api.loadExplorerRelatedItems(workspacePath, surface, itemId, 6);
  }

  async function loadWorkspacePanels() {
    setApiFilter("");
    setTargetFilter("");
    setChainLimit(defaultChainLimit);
    await loadSummary();
    await loadOverview();
    await loadRuntimeMap();
    await loadChains("", "", defaultChainLimit);
    await loadApiRows("", defaultApiRouteLimit);
  }

  function openOverviewItem(section: OverviewSection, item: RepositoryOverviewItem) {
    if (!canQueryWorkspace) {
      return;
    }

    applyOverviewNavigation(getOverviewItemNavigation(section, item));
  }

  function browseOverviewSection(section: OverviewSection) {
    if (!canQueryWorkspace) {
      return;
    }

    applyOverviewNavigation(getOverviewSectionNavigation(section));
  }

  function applyOverviewNavigation(navigation: OverviewNavigation) {
    if (navigation.panel === "explorer") {
      openRepositoryExplorer(navigation.surface, navigation.query, null, "Start here");
      return;
    }

    if (navigation.panel === "files") {
      setFileLanguageFilter(navigation.language);
      setFileFolderFilter(navigation.folder);
      setActivePanel("files");
      void loadFileRows(navigation.language, navigation.folder, navigation.take);
      return;
    }

    if (navigation.panel === "chains") {
      setApiFilter(navigation.api);
      setTargetFilter(navigation.target);
      if (navigation.take) {
        setChainLimit(navigation.take);
      }
      setActivePanel("chains");
      void loadChains(navigation.api, navigation.target, navigation.take);
      return;
    }

    if (navigation.panel === "apis") {
      setApiFilter(navigation.route);
      setActivePanel("apis");
      void loadApiRows(navigation.route, navigation.take);
      return;
    }

    if (navigation.panel === "backend") {
      setTargetFilter(navigation.target);
      setActivePanel("backend");
      void loadBackendRows("", navigation.target, navigation.take);
      return;
    }

    setAzureFilter(navigation.service);
    setActivePanel("azure");
    void loadAzureRows(navigation.service, navigation.take);
  }

  function openPanel(panel: AppPanel) {
    setActivePanel(panel);
    if (panel === "explorer") {
      setRepositoryExplorerOrigin("Browse evidence");
    }
    if (panel === "overview" && !overview && canQueryWorkspace) {
      void loadOverview();
    }
    if (panel === "runtime" && !runtimeMap && canQueryWorkspace) {
      void loadRuntimeMap();
    }
    if (panel === "chains" && chains.length === 0 && canQueryWorkspace) {
      setApiFilter("");
      setTargetFilter("");
      setChainLimit(defaultChainLimit);
      void loadChains("", "", defaultChainLimit);
    }
    if (panel === "csharpTrace" && !csharpTrace && canQueryWorkspace && csharpTraceEntry.trim().length > 0) {
      void loadCSharpTrace();
    }
    if (panel === "csharpTrace" && csharpTraceEntries.length === 0 && canQueryWorkspace) {
      void loadCSharpTraceEntries();
    }
    if (panel === "explorer" && repositoryExplorerRows[repositoryExplorerSurface].length === 0 && canQueryWorkspace) {
      void loadRepositoryExplorerSurface(repositoryExplorerSurface);
    }
    if (panel === "backend" && backendRows.length === 0 && canQueryWorkspace) {
      void loadBackendRows();
    }
    if (panel === "apis" && apiRows.length === 0 && canQueryWorkspace) {
      void loadApiRows("", defaultApiRouteLimit);
    }
    if (panel === "azure" && azureRows.length === 0 && canQueryWorkspace) {
      void loadAzureRows();
    }
    if (panel === "files" && fileRows.length === 0 && canQueryWorkspace) {
      void loadFileRows();
    }
  }

  return {
    apiBaseUrl,
    apiSource,
    workspacePath,
    health,
    overview,
    runtimeMap,
    summary,
    apiFilter,
    targetFilter,
    chains,
    csharpTraceEntry,
    csharpTraceDepth,
    csharpTrace,
    csharpTraceEntries,
    csharpTracePreferences,
    backendRows,
    apiRows,
    azureRows,
    fileRows,
    repositoryExplorerSurface,
    repositoryExplorerQuery,
    repositoryExplorerSelectedItemId,
    repositoryExplorerOrigin,
    repositoryExplorerRows,
    azureFilter,
    fileLanguageFilter,
    fileFolderFilter,
    chainLimit,
    activePanel,
    theme,
    busy,
    message,
    indexingStatus,
    canQueryWorkspace,
    isIndexingActive,
    isBusy,
    setApiBaseUrl,
    setApiSource,
    setWorkspacePath,
    setApiFilter,
    setTargetFilter,
    setCSharpTraceEntry,
    setCSharpTraceDepth,
    setCSharpTracePreferences: updateCSharpTracePreferences,
    setAzureFilter,
    setFileLanguageFilter,
    setFileFolderFilter,
    setChainLimit,
    setTheme,
    setRepositoryExplorerSurface: changeRepositoryExplorerSurface,
    checkHealth,
    initializeWorkspace,
    syncWorkspace,
    cancelIndexing,
    retryIndexing,
    loadWorkspacePanels,
    loadOverview,
    loadRuntimeMap,
    loadChains,
    loadCSharpTrace,
    loadCSharpTraceEntries,
    loadBackendRows,
    loadApiRows,
    loadAzureRows,
    loadFileRows,
    loadRepositoryExplorerSurface,
    loadRepositoryExplorerAll,
    loadRepositoryExplorerRelatedEvidence,
    openRepositoryExplorer,
    openExplorerTarget,
    openOverviewItem,
    browseOverviewSection,
    openPanel
  };
}

function readNumberSetting(key: string, fallback: number, min: number, max: number) {
  const value = Number(localStorage.getItem(key));
  if (!Number.isFinite(value)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, Math.round(value)));
}

function isIndexingJobStatus(value: unknown): value is IndexingJobStatus {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Partial<IndexingJobStatus>;
  return typeof candidate.jobId === "string"
    && typeof candidate.state === "string"
    && typeof candidate.stage === "string"
    && typeof candidate.filesScanned === "number";
}

function formatIndexingMessage(status: IndexingJobStatus) {
  const currentFile = status.currentFile ? ` • ${compactPath(status.currentFile)}` : "";
  if (status.state === "failed") {
    return status.error || status.message || "Indexing failed";
  }

  if (status.state === "cancelled") {
    return "Indexing cancelled";
  }

  if (status.state === "completed") {
    return `Index ready • scanned ${status.filesScanned}, indexed ${status.filesIndexed}`;
  }

  return `${status.stage} • scanned ${status.filesScanned}, indexed ${status.filesIndexed}, skipped ${status.filesSkipped}${currentFile}`;
}

function compactPath(path: string) {
  return path.length > 80 ? `...${path.slice(-77)}` : path;
}

function delay(milliseconds: number) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
}
