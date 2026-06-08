import {
  Activity,
  Braces,
  Cloud,
  Compass,
  Database,
  FolderTree,
  Loader2,
  Network,
  Play,
  RefreshCw,
  Search,
  Server,
  ShieldCheck
} from "lucide-react";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { CodeFlowLoading } from "./components/common/CodeFlowLoading";
import { Metric } from "./components/common/Metric";
import { Toggle } from "./components/common/Toggle";
import { ApiSurfacePanel } from "./features/api-surface/ApiSurfacePanel";
import { AzurePanel } from "./features/azure/AzurePanel";
import { BackendPanel } from "./features/backend-graph/BackendPanel";
import { ChainsPanel } from "./features/flow-chains/ChainsPanel";
import { FilesPanel } from "./features/files/FilesPanel";
import { OverviewPanel } from "./features/overview/OverviewPanel";
import { RuntimeMapPanel as RuntimeMapPanelFeature } from "./features/runtime-map/RuntimeMapPanel";
import { SummaryPanel } from "./features/summary/SummaryPanel";
import { getDefaultRuntimeConnection } from "./runtime";
import type {
  ApiHealth,
  OverviewSection,
  RepositoryOverview,
  RepositoryOverviewItem,
  RuntimeFlowMap,
  WorkspaceSummary
} from "./types";

const apiKinds = ["overview", "runtime", "summary", "chains", "backend", "apis", "azure", "files"] as const;
const defaultRuntimeConnection = getDefaultRuntimeConnection();
const defaultWorkspacePath = localStorage.getItem("codeflowiq.workspacePath") ?? "";

export function App() {
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
  const [azureFilter, setAzureFilter] = useState("");
  const [fileLanguageFilter, setFileLanguageFilter] = useState("");
  const [fileFolderFilter, setFileFolderFilter] = useState("");
  const [chainLimit, setChainLimit] = useState(8);
  const [activePanel, setActivePanel] = useState<(typeof apiKinds)[number]>("overview");
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string>("Ready");

  const normalizedApiBaseUrl = useMemo(() => apiBaseUrl.trim().replace(/\/$/, ""), [apiBaseUrl]);
  const canQueryWorkspace = workspacePath.trim().length > 0 && health?.status === "healthy";

  useEffect(() => {
    void checkHealth();
  }, []);

  async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await fetch(`${normalizedApiBaseUrl}${path}`, init);
    if (!response.ok) {
      const body = await response.text();
      throw new Error(body || `${response.status} ${response.statusText}`);
    }

    return (await response.json()) as T;
  }

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
    const result = await runTask("Checking API", async () => request<ApiHealth>("/health"));
    if (result) {
      setHealth(result);
      setApiSource("saved");
      localStorage.setItem("codeflowiq.apiBaseUrl", normalizedApiBaseUrl);
    } else {
      setHealth(null);
    }
  }

  async function initializeWorkspace() {
    const result = await runTask("Indexing workspace", async () =>
      request("/api/workspace/init", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ path: workspacePath.trim() })
      })
    );
    if (result) {
      localStorage.setItem("codeflowiq.workspacePath", workspacePath.trim());
      await loadSummary();
      await loadOverview();
      await loadRuntimeMap();
      await loadChains();
    }
  }

  async function syncWorkspace() {
    const result = await runTask("Syncing workspace", async () =>
      request("/api/workspace/sync", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ path: workspacePath.trim() })
      })
    );
    if (result) {
      localStorage.setItem("codeflowiq.workspacePath", workspacePath.trim());
      await loadSummary();
      await loadOverview();
      await loadRuntimeMap();
    }
  }

  async function loadSummary() {
    const path = encodeURIComponent(workspacePath.trim());
    const result = await runTask("Loading summary", async () =>
      request<WorkspaceSummary>(`/api/summary?path=${path}&take=8`)
    );
    if (result) {
      setSummary(result);
    }
  }

  async function loadOverview() {
    const path = encodeURIComponent(workspacePath.trim());
    const result = await runTask("Loading overview", async () =>
      request<RepositoryOverview>(`/api/overview?path=${path}&take=8`)
    );
    if (result) {
      setOverview(result);
    }
  }

  async function loadRuntimeMap() {
    const path = encodeURIComponent(workspacePath.trim());
    const result = await runTask("Loading runtime map", async () =>
      request<RuntimeFlowMap>(`/api/runtime-flows?path=${path}&take=10`)
    );
    if (result) {
      setRuntimeMap(result);
    }
  }

  async function loadChains(apiOverride = apiFilter, targetOverride = targetFilter, takeOverride = chainLimit) {
    const path = encodeURIComponent(workspacePath.trim());
    const api = encodeURIComponent(apiOverride.trim());
    const target = encodeURIComponent(targetOverride.trim());
    const result = await runTask("Loading chains", async () =>
      request<string[]>(`/api/chains?path=${path}&api=${api}&target=${target}&format=tree&take=${takeOverride}&depth=8`)
    );
    if (result) {
      setChains(result);
    }
  }

  async function loadBackendRows(kind = "executes_procedure", targetOverride = "", take = 40) {
    const path = encodeURIComponent(workspacePath.trim());
    const target = encodeURIComponent(targetOverride.trim());
    const kindQuery = kind ? `&kind=${encodeURIComponent(kind)}` : "";
    const result = await runTask("Loading backend relationships", async () =>
      request<string[]>(`/api/backend?path=${path}${kindQuery}&target=${target}&take=${take}`)
    );
    if (result) {
      setBackendRows(result);
    }
  }

  async function loadApiRows(routeOverride = apiFilter, take = 80) {
    const path = encodeURIComponent(workspacePath.trim());
    const route = encodeURIComponent(routeOverride.trim());
    const result = await runTask("Loading APIs", async () =>
      request<string[]>(`/api/apis?path=${path}&route=${route}&take=${take}`)
    );
    if (result) {
      setApiRows(result);
    }
  }

  async function loadAzureRows(serviceOverride = azureFilter, take = 80) {
    const path = encodeURIComponent(workspacePath.trim());
    const service = encodeURIComponent(serviceOverride.trim());
    const result = await runTask("Loading Azure dependencies", async () =>
      request<string[]>(`/api/azure?path=${path}&service=${service}&take=${take}`)
    );
    if (result) {
      setAzureRows(result);
    }
  }

  async function loadFileRows(languageOverride = fileLanguageFilter, folderOverride = fileFolderFilter, take = 120) {
    const path = encodeURIComponent(workspacePath.trim());
    const language = encodeURIComponent(languageOverride.trim());
    const folder = encodeURIComponent(folderOverride.trim());
    const result = await runTask("Loading files", async () =>
      request<string[]>(`/api/files?path=${path}&language=${language}&folder=${folder}&take=${take}`)
    );
    if (result) {
      setFileRows(result);
    }
  }

  function openOverviewItem(section: OverviewSection, item: RepositoryOverviewItem) {
    if (!canQueryWorkspace) {
      return;
    }

    if (section === "technology") {
      const language = toLanguageId(item.title);
      setFileLanguageFilter(language);
      setFileFolderFilter("");
      setActivePanel("files");
      void loadFileRows(language, "");
      return;
    }

    if (section === "flow") {
      setApiFilter(item.title);
      setTargetFilter("");
      setActivePanel("chains");
      void loadChains(item.title, "");
      return;
    }

    if (section === "api") {
      setApiFilter(item.title);
      setActivePanel("apis");
      void loadApiRows(item.title);
      return;
    }

    if (section === "data") {
      setTargetFilter(item.title);
      setActivePanel("backend");
      void loadBackendRows("", item.title);
      return;
    }

    if (section === "azure") {
      setAzureFilter(item.title);
      setActivePanel("azure");
      void loadAzureRows(item.title);
      return;
    }

    if (section === "folder") {
      setFileLanguageFilter("");
      setFileFolderFilter(item.title);
      setActivePanel("files");
      void loadFileRows("", item.title);
      return;
    }

    if (item.title.includes("API")) {
      setActivePanel("apis");
      void loadApiRows("");
    } else if (item.title.includes("data")) {
      setActivePanel("backend");
      void loadBackendRows("");
    } else if (item.title.includes("Azure")) {
      setActivePanel("azure");
      void loadAzureRows("");
    } else if (item.title.includes("folder")) {
      setActivePanel("files");
      void loadFileRows("", "");
    } else {
      setActivePanel("chains");
      void loadChains("", "");
    }
  }

  function browseOverviewSection(section: OverviewSection) {
    if (!canQueryWorkspace) {
      return;
    }

    if (section === "technology" || section === "folder") {
      setFileLanguageFilter("");
      setFileFolderFilter("");
      setActivePanel("files");
      void loadFileRows("", "", 1000);
      return;
    }

    if (section === "api") {
      setApiFilter("");
      setActivePanel("apis");
      void loadApiRows("", 1000);
      return;
    }

    if (section === "data") {
      setTargetFilter("");
      setActivePanel("backend");
      void loadBackendRows("", "", 1000);
      return;
    }

    if (section === "azure") {
      setAzureFilter("");
      setActivePanel("azure");
      void loadAzureRows("", 1000);
      return;
    }

    setActivePanel("chains");
    setChainLimit(50);
    void loadChains("", "", 50);
  }

  function submitConnection(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void checkHealth();
  }

  function submitWorkspace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void (async () => {
      await loadSummary();
      await loadOverview();
      await loadRuntimeMap();
    })();
  }

  const isBusy = busy !== null;

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand-block">
          <div className="brand-mark">CF</div>
          <div>
            <h1>CodeFlowIQ</h1>
            <span>Local intelligence workspace</span>
          </div>
        </div>

        <nav className="nav-list" aria-label="Main navigation">
          <button className={activePanel === "overview" ? "active" : ""} onClick={() => {
            setActivePanel("overview");
            if (!overview && canQueryWorkspace) void loadOverview();
          }}>
            <Compass size={17} /> Start here
          </button>
          <button className={activePanel === "runtime" ? "active" : ""} onClick={() => {
            setActivePanel("runtime");
            if (!runtimeMap && canQueryWorkspace) void loadRuntimeMap();
          }}>
            <Play size={17} /> Runtime map
          </button>
          <button className={activePanel === "summary" ? "active" : ""} onClick={() => setActivePanel("summary")}>
            <Activity size={17} /> Summary
          </button>
          <button className={activePanel === "chains" ? "active" : ""} onClick={() => setActivePanel("chains")}>
            <Network size={17} /> Flow chains
          </button>
          <button className={activePanel === "backend" ? "active" : ""} onClick={() => {
            setActivePanel("backend");
            if (backendRows.length === 0 && canQueryWorkspace) void loadBackendRows();
          }}>
            <Database size={17} /> Backend graph
          </button>
          <button className={activePanel === "apis" ? "active" : ""} onClick={() => {
            setActivePanel("apis");
            if (apiRows.length === 0 && canQueryWorkspace) void loadApiRows();
          }}>
            <Braces size={17} /> API surface
          </button>
          <button className={activePanel === "azure" ? "active" : ""} onClick={() => {
            setActivePanel("azure");
            if (azureRows.length === 0 && canQueryWorkspace) void loadAzureRows();
          }}>
            <Cloud size={17} /> Azure
          </button>
          <button className={activePanel === "files" ? "active" : ""} onClick={() => {
            setActivePanel("files");
            if (fileRows.length === 0 && canQueryWorkspace) void loadFileRows();
          }}>
            <FolderTree size={17} /> Files
          </button>
        </nav>

        <div className="feature-toggles">
          <Toggle label="Frontend flows" checked />
          <Toggle label="Backend flows" checked />
          <Toggle label="SQL/T-SQL" checked />
          <Toggle label="Azure services" checked />
          <Toggle label="Git history" />
          <Toggle label="LLM assistant" />
        </div>
      </aside>

      <main className="main-panel">
        <section className="topbar">
          <form className="connection-form" onSubmit={submitConnection}>
            <label>
              API ({apiSource})
              <input value={apiBaseUrl} onChange={(event) => {
                setApiSource("saved");
                setApiBaseUrl(event.target.value);
              }} />
            </label>
            <button type="submit" className="icon-button" title="Check API">
              {busy === "Checking API" ? <Loader2 className="spin" size={18} /> : <Server size={18} />}
            </button>
          </form>

          <div className={health?.status === "healthy" ? "status-pill ok" : "status-pill"}>
            <ShieldCheck size={16} />
            {health?.status ?? "offline"}
          </div>
        </section>

        <section className="workspace-bar">
          <form onSubmit={submitWorkspace}>
            <label>
              Workspace
              <input
                placeholder="C:\\Users\\ajite\\Downloads\\Compressed\\deloitte-omnia-financial-be"
                value={workspacePath}
                onChange={(event) => setWorkspacePath(event.target.value)}
              />
            </label>
            <button type="button" onClick={initializeWorkspace} disabled={!canQueryWorkspace || isBusy}>
              <Play size={17} /> Init
            </button>
            <button type="button" onClick={syncWorkspace} disabled={!canQueryWorkspace || isBusy}>
              <RefreshCw size={17} /> Sync
            </button>
            <button type="submit" disabled={!canQueryWorkspace || isBusy}>
              <Search size={17} /> Load
            </button>
          </form>
        </section>

        <section className="metric-band">
          <Metric label="Files" value={summary?.fileCount} />
          <Metric label="Symbols" value={summary?.symbolCount} />
          <Metric label="Relationships" value={summary?.relationshipCount} />
          <Metric label="Workspace" value={summary?.kind ?? "n/a"} />
        </section>

        <section className="content-surface">
          {activePanel === "overview" && (
            <OverviewPanel
              overview={overview}
              disabled={!canQueryWorkspace || isBusy}
              onLoad={loadOverview}
              onOpenItem={openOverviewItem}
              onBrowseSection={browseOverviewSection}
            />
          )}
          {activePanel === "runtime" && (
            <RuntimeMapPanelFeature
              runtimeMap={runtimeMap}
              disabled={!canQueryWorkspace || isBusy}
              onLoad={loadRuntimeMap}
            />
          )}
          {activePanel === "summary" && <SummaryPanel summary={summary} />}
          {activePanel === "chains" && (
            <ChainsPanel
              apiFilter={apiFilter}
              targetFilter={targetFilter}
              chainLimit={chainLimit}
              chains={chains}
              disabled={!canQueryWorkspace || isBusy}
              onApiFilterChange={setApiFilter}
              onTargetFilterChange={setTargetFilter}
              onChainLimitChange={setChainLimit}
              onLoad={loadChains}
            />
          )}
          {activePanel === "backend" && (
            <BackendPanel rows={backendRows} disabled={!canQueryWorkspace || isBusy} onLoad={loadBackendRows} />
          )}
          {activePanel === "apis" && (
            <ApiSurfacePanel
              apiFilter={apiFilter}
              rows={apiRows}
              disabled={!canQueryWorkspace || isBusy}
              onApiFilterChange={setApiFilter}
              onLoad={loadApiRows}
            />
          )}
          {activePanel === "azure" && (
            <AzurePanel
              serviceFilter={azureFilter}
              rows={azureRows}
              disabled={!canQueryWorkspace || isBusy}
              onServiceFilterChange={setAzureFilter}
              onLoad={loadAzureRows}
            />
          )}
          {activePanel === "files" && (
            <FilesPanel
              languageFilter={fileLanguageFilter}
              folderFilter={fileFolderFilter}
              rows={fileRows}
              disabled={!canQueryWorkspace || isBusy}
              onLanguageFilterChange={setFileLanguageFilter}
              onFolderFilterChange={setFileFolderFilter}
              onLoad={loadFileRows}
            />
          )}
        </section>

        <footer className="status-line">
          <span className={busy ? "busy-status" : ""}>
            {busy && <Loader2 className="spin" size={14} />}
            {message}
          </span>
          {health?.runtimeFile && <span>{health.runtimeFile}</span>}
        </footer>
      </main>
      {busy && <CodeFlowLoading label={busy} />}
    </div>
  );
}

function toLanguageId(title: string) {
  const lower = title.toLowerCase();
  if (lower.includes("c#") || lower.includes("asp.net")) {
    return "csharp";
  }

  if (lower.includes("sql")) {
    return "sql";
  }

  if (lower.includes("typescript")) {
    return "typescript";
  }

  if (lower.includes("javascript")) {
    return "javascript";
  }

  if (lower.includes("html") || lower.includes("angular")) {
    return "html";
  }

  if (lower.includes("json")) {
    return "json";
  }

  return title;
}
