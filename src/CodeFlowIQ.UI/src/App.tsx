import { CodeFlowLoading } from "./components/common/CodeFlowLoading";
import { MetricBand, Sidebar, StatusLine, Topbar, WorkspaceBar } from "./components/layout";
import { ApiSurfacePanel } from "./features/api-surface";
import { AzurePanel } from "./features/azure";
import { BackendPanel } from "./features/backend-graph";
import { ChainsPanel } from "./features/flow-chains";
import { FilesPanel } from "./features/files";
import { OverviewPanel } from "./features/overview";
import { RepositoryExplorerPanel } from "./features/repository-explorer";
import { RuntimeMapPanel } from "./features/runtime-map";
import { SummaryPanel } from "./features/summary";
import { useWorkspaceData } from "./hooks/useWorkspaceData";

export function App() {
  const workspace = useWorkspaceData();
  const queryDisabled = !workspace.canQueryWorkspace || workspace.isBusy;

  return (
    <div className="app-shell">
      <Sidebar
        activePanel={workspace.activePanel}
        theme={workspace.theme}
        onOpenPanel={workspace.openPanel}
        onThemeChange={workspace.setTheme}
      />

      <main className="main-panel">
        <Topbar
          apiBaseUrl={workspace.apiBaseUrl}
          apiSource={workspace.apiSource}
          health={workspace.health}
          busy={workspace.busy}
          onApiBaseUrlChange={workspace.setApiBaseUrl}
          onApiSourceChange={workspace.setApiSource}
          onCheckHealth={workspace.checkHealth}
        />

        <WorkspaceBar
          workspacePath={workspace.workspacePath}
          disabled={queryDisabled}
          onWorkspacePathChange={workspace.setWorkspacePath}
          onInitialize={workspace.initializeWorkspace}
          onSync={workspace.syncWorkspace}
          onLoad={workspace.loadWorkspacePanels}
        />

        <MetricBand summary={workspace.summary} />

        <section className="content-surface">
          {workspace.activePanel === "overview" && (
            <OverviewPanel
              overview={workspace.overview}
              disabled={queryDisabled}
              onLoad={workspace.loadOverview}
              onOpenItem={workspace.openOverviewItem}
              onBrowseSection={workspace.browseOverviewSection}
            />
          )}
          {workspace.activePanel === "runtime" && (
            <RuntimeMapPanel
              runtimeMap={workspace.runtimeMap}
              disabled={queryDisabled}
              onLoad={workspace.loadRuntimeMap}
              onOpenExplorer={(surface, query, selectedItemId) =>
                workspace.openRepositoryExplorer(surface, query, selectedItemId, "Runtime Map")
              }
            />
          )}
          {workspace.activePanel === "explorer" && (
            <RepositoryExplorerPanel
              activeSurface={workspace.repositoryExplorerSurface}
              incomingQuery={workspace.repositoryExplorerQuery}
              incomingSelectedItemId={workspace.repositoryExplorerSelectedItemId}
              incomingOriginLabel={workspace.repositoryExplorerOrigin}
              rowsBySurface={workspace.repositoryExplorerRows}
              disabled={queryDisabled}
              onSurfaceChange={workspace.setRepositoryExplorerSurface}
              onLoadSurface={workspace.loadRepositoryExplorerSurface}
              onLoadAll={workspace.loadRepositoryExplorerAll}
              onLoadRelatedEvidence={workspace.loadRepositoryExplorerRelatedEvidence}
            />
          )}
          {workspace.activePanel === "summary" && (
            <SummaryPanel
              summary={workspace.summary}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "Repo snapshot")}
            />
          )}
          {workspace.activePanel === "chains" && (
            <ChainsPanel
              apiFilter={workspace.apiFilter}
              targetFilter={workspace.targetFilter}
              chainLimit={workspace.chainLimit}
              chains={workspace.chains}
              disabled={queryDisabled}
              onApiFilterChange={workspace.setApiFilter}
              onTargetFilterChange={workspace.setTargetFilter}
              onChainLimitChange={workspace.setChainLimit}
              onLoad={workspace.loadChains}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "End-to-end flows")}
            />
          )}
          {workspace.activePanel === "backend" && (
            <BackendPanel
              rows={workspace.backendRows}
              disabled={queryDisabled}
              onLoad={workspace.loadBackendRows}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "Backend & data")}
            />
          )}
          {workspace.activePanel === "apis" && (
            <ApiSurfacePanel
              apiFilter={workspace.apiFilter}
              rows={workspace.apiRows}
              disabled={queryDisabled}
              onApiFilterChange={workspace.setApiFilter}
              onLoad={workspace.loadApiRows}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "API endpoints")}
            />
          )}
          {workspace.activePanel === "azure" && (
            <AzurePanel
              serviceFilter={workspace.azureFilter}
              rows={workspace.azureRows}
              disabled={queryDisabled}
              onServiceFilterChange={workspace.setAzureFilter}
              onLoad={workspace.loadAzureRows}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "Cloud services")}
            />
          )}
          {workspace.activePanel === "files" && (
            <FilesPanel
              languageFilter={workspace.fileLanguageFilter}
              folderFilter={workspace.fileFolderFilter}
              rows={workspace.fileRows}
              disabled={queryDisabled}
              onLanguageFilterChange={workspace.setFileLanguageFilter}
              onFolderFilterChange={workspace.setFileFolderFilter}
              onLoad={workspace.loadFileRows}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "Files indexed")}
            />
          )}
        </section>

        <StatusLine busy={workspace.busy} message={workspace.message} health={workspace.health} />
      </main>

      {workspace.busy && <CodeFlowLoading label={workspace.busy} />}
    </div>
  );
}
