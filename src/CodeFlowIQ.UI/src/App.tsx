import { CodeFlowLoading } from "./components/common/CodeFlowLoading";
import { Sidebar, StatusLine, Topbar, WorkspaceBar } from "./components/layout";
import { ApiSurfacePanel } from "./features/api-surface";
import { AzurePanel } from "./features/azure";
import { BackendPanel } from "./features/backend-graph";
import { ChainsPanel } from "./features/flow-chains";
import { CSharpTracePanel } from "./features/csharp-trace";
import { FilesPanel } from "./features/files";
import { OverviewPanel } from "./features/overview";
import { RepositoryExplorerPanel } from "./features/repository-explorer";
import { RuntimeMapPanel } from "./features/runtime-map";
import { SettingsPanel } from "./features/settings";
import { SummaryPanel } from "./features/summary";
import { useWorkspaceData } from "./hooks/useWorkspaceData";

export function App() {
  const workspace = useWorkspaceData();
  const blockingBusy = workspace.isBusy && !workspace.isIndexingActive;
  const queryDisabled = !workspace.canQueryWorkspace || blockingBusy;
  const workspaceActionsDisabled = !workspace.canQueryWorkspace || workspace.isBusy;

  return (
    <div className="app-shell">
      <Sidebar
        activePanel={workspace.activePanel}
        onOpenPanel={workspace.openPanel}
      />

      <main className="main-panel">
        <Topbar
          apiBaseUrl={workspace.apiBaseUrl}
          apiSource={workspace.apiSource}
          health={workspace.health}
          summary={workspace.summary}
          busy={workspace.busy}
          onApiBaseUrlChange={workspace.setApiBaseUrl}
          onApiSourceChange={workspace.setApiSource}
          onCheckHealth={workspace.checkHealth}
        />

        <WorkspaceBar
          workspacePath={workspace.workspacePath}
          disabled={workspaceActionsDisabled}
          indexingStatus={workspace.indexingStatus}
          onWorkspacePathChange={workspace.setWorkspacePath}
          onInitialize={workspace.initializeWorkspace}
          onSync={workspace.syncWorkspace}
          onCancelIndexing={workspace.cancelIndexing}
          onRetryIndexing={workspace.retryIndexing}
          onLoad={workspace.loadWorkspacePanels}
        />

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
          {workspace.activePanel === "csharpTrace" && (
            <CSharpTracePanel
              entry={workspace.csharpTraceEntry}
              depth={workspace.csharpTraceDepth}
              trace={workspace.csharpTrace}
              routeCandidates={workspace.csharpTraceEntries}
              preferences={workspace.csharpTracePreferences}
              disabled={queryDisabled}
              onEntryChange={workspace.setCSharpTraceEntry}
              onDepthChange={workspace.setCSharpTraceDepth}
              onTrace={workspace.loadCSharpTrace}
              onLoadRoutes={() => workspace.loadCSharpTraceEntries()}
              onOpenExplorer={(target) => workspace.openExplorerTarget(target, "C# backend trace")}
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
          {workspace.activePanel === "settings" && (
            <SettingsPanel
              theme={workspace.theme}
              onThemeChange={workspace.setTheme}
              tracePreferences={workspace.csharpTracePreferences}
              onTracePreferencesChange={workspace.setCSharpTracePreferences}
            />
          )}
        </section>

        <StatusLine busy={workspace.busy} message={workspace.message} health={workspace.health} />
      </main>

      {blockingBusy && workspace.busy && <CodeFlowLoading label={workspace.busy} />}
    </div>
  );
}
