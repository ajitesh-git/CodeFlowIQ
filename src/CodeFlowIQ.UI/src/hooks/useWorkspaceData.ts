import { useEffect, useMemo, useState } from "react";
import { getDefaultRuntimeConnection } from "../runtime";
import { createWorkspaceApi } from "../services/workspaceApi";
import type {
  ApiHealth,
  OverviewSection,
  RepositoryOverview,
  RepositoryOverviewItem,
  RuntimeFlowMap,
  WorkspaceSummary
} from "../types";
import type { AppPanel, OverviewNavigation } from "../features/overview";
import type { RepositoryExplorerRows, RepositoryExplorerSurface } from "../features/repository-explorer";
import { getOverviewItemNavigation, getOverviewSectionNavigation } from "../features/overview/overviewRouting";

const defaultRuntimeConnection = getDefaultRuntimeConnection();
const defaultWorkspacePath = localStorage.getItem("codeflowiq.workspacePath") ?? "";
type ThemeMode = "light" | "dark";
const defaultTheme = (localStorage.getItem("codeflowiq.theme") === "dark" ? "dark" : "light") satisfies ThemeMode;
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
  const [apiFilter, setApiFilter] = useState("accountsetup");
  const [targetFilter, setTargetFilter] = useState("TrialBalance");
  const [chains, setChains] = useState<string[]>([]);
  const [backendRows, setBackendRows] = useState<string[]>([]);
  const [apiRows, setApiRows] = useState<string[]>([]);
  const [azureRows, setAzureRows] = useState<string[]>([]);
  const [fileRows, setFileRows] = useState<string[]>([]);
  const [repositoryExplorerSurface, setRepositoryExplorerSurface] = useState<RepositoryExplorerSurface>("files");
  const [repositoryExplorerQuery, setRepositoryExplorerQuery] = useState("");
  const [repositoryExplorerSelectedItemId, setRepositoryExplorerSelectedItemId] = useState<string | null>(null);
  const [repositoryExplorerRows, setRepositoryExplorerRows] =
    useState<RepositoryExplorerRows>(emptyRepositoryExplorerRows);
  const [azureFilter, setAzureFilter] = useState("");
  const [fileLanguageFilter, setFileLanguageFilter] = useState("");
  const [fileFolderFilter, setFileFolderFilter] = useState("");
  const [chainLimit, setChainLimit] = useState(8);
  const [activePanel, setActivePanel] = useState<AppPanel>("overview");
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string>("Ready");

  const normalizedApiBaseUrl = useMemo(() => apiBaseUrl.trim().replace(/\/$/, ""), [apiBaseUrl]);
  const api = useMemo(() => createWorkspaceApi(normalizedApiBaseUrl), [normalizedApiBaseUrl]);
  const canQueryWorkspace = workspacePath.trim().length > 0 && health?.status === "healthy";
  const isBusy = busy !== null;

  useEffect(() => {
    void checkHealth();
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem("codeflowiq.theme", theme);
  }, [theme]);

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
    const result = await runTask("Indexing workspace", () => api.initializeWorkspace(path));
    if (result) {
      localStorage.setItem("codeflowiq.workspacePath", path);
      await loadSummary();
      await loadOverview();
      await loadRuntimeMap();
      await loadChains();
    }
  }

  async function syncWorkspace() {
    const path = workspacePath.trim();
    const result = await runTask("Syncing workspace", () => api.syncWorkspace(path));
    if (result) {
      localStorage.setItem("codeflowiq.workspacePath", path);
      await loadSummary();
      await loadOverview();
      await loadRuntimeMap();
    }
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

  function openRepositoryExplorer(surface: RepositoryExplorerSurface, query = "", selectedItemId: string | null = null) {
    if (!canQueryWorkspace) {
      return;
    }

    setRepositoryExplorerSurface(surface);
    setRepositoryExplorerQuery(query);
    setRepositoryExplorerSelectedItemId(selectedItemId);
    setActivePanel("explorer");
    if (repositoryExplorerRows[surface].length === 0
      || (selectedItemId && !repositoryExplorerRows[surface].some((row) => row.id === selectedItemId))) {
      void loadRepositoryExplorerSurface(surface, selectedItemId);
    }
  }

  function changeRepositoryExplorerSurface(surface: RepositoryExplorerSurface) {
    setRepositoryExplorerSurface(surface);
    setRepositoryExplorerQuery("");
    setRepositoryExplorerSelectedItemId(null);
  }

  function loadRepositoryExplorerRows(surface: RepositoryExplorerSurface, selectedItemId: string | null = null) {
    return api.loadExplorerItems(workspacePath, surface, "", 1000, selectedItemId);
  }

  async function loadWorkspacePanels() {
    await loadSummary();
    await loadOverview();
    await loadRuntimeMap();
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
      openRepositoryExplorer(navigation.surface, navigation.query);
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
    if (panel === "overview" && !overview && canQueryWorkspace) {
      void loadOverview();
    }
    if (panel === "runtime" && !runtimeMap && canQueryWorkspace) {
      void loadRuntimeMap();
    }
    if (panel === "explorer" && repositoryExplorerRows[repositoryExplorerSurface].length === 0 && canQueryWorkspace) {
      void loadRepositoryExplorerSurface(repositoryExplorerSurface);
    }
    if (panel === "backend" && backendRows.length === 0 && canQueryWorkspace) {
      void loadBackendRows();
    }
    if (panel === "apis" && apiRows.length === 0 && canQueryWorkspace) {
      void loadApiRows();
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
    backendRows,
    apiRows,
    azureRows,
    fileRows,
    repositoryExplorerSurface,
    repositoryExplorerQuery,
    repositoryExplorerSelectedItemId,
    repositoryExplorerRows,
    azureFilter,
    fileLanguageFilter,
    fileFolderFilter,
    chainLimit,
    activePanel,
    theme,
    busy,
    message,
    canQueryWorkspace,
    isBusy,
    setApiBaseUrl,
    setApiSource,
    setWorkspacePath,
    setApiFilter,
    setTargetFilter,
    setAzureFilter,
    setFileLanguageFilter,
    setFileFolderFilter,
    setChainLimit,
    setTheme,
    setRepositoryExplorerSurface: changeRepositoryExplorerSurface,
    checkHealth,
    initializeWorkspace,
    syncWorkspace,
    loadWorkspacePanels,
    loadOverview,
    loadRuntimeMap,
    loadChains,
    loadBackendRows,
    loadApiRows,
    loadAzureRows,
    loadFileRows,
    loadRepositoryExplorerSurface,
    loadRepositoryExplorerAll,
    openRepositoryExplorer,
    openOverviewItem,
    browseOverviewSection,
    openPanel
  };
}
