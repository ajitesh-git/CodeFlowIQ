export type ApiHealth = {
  status: string;
  name: string;
  processId: number;
  runtimeFile?: string | null;
};

export type IndexingJobStatus = {
  jobId: string;
  workspacePath: string;
  operation: string;
  state: "queued" | "running" | "cancelling" | "cancelled" | "completed" | "failed" | string;
  stage: string;
  filesScanned: number;
  filesIndexed: number;
  filesSkipped: number;
  symbolsIndexed: number;
  currentFile?: string | null;
  message: string;
  startedAt: string;
  updatedAt: string;
  completedAt?: string | null;
  error?: string | null;
};

export type WorkspaceSummary = {
  name: string;
  kind: string;
  fileCount: number;
  symbolCount: number;
  relationshipCount: number;
  languageCounts: string[];
  symbolKindCounts: string[];
  relationshipKindCounts: string[];
  azureServiceCounts: string[];
};

export type RepositoryOverviewItem = {
  title: string;
  detail: string;
  kind: string;
  score: number;
};

export type RepositoryOverview = {
  name: string;
  kind: string;
  summary: string;
  technologySignals: RepositoryOverviewItem[];
  suggestedStartingPoints: RepositoryOverviewItem[];
  detectedFlows: RepositoryOverviewItem[];
  importantApis: RepositoryOverviewItem[];
  dataTouchpoints: RepositoryOverviewItem[];
  azureDependencies: RepositoryOverviewItem[];
  importantFolders: RepositoryOverviewItem[];
};

export type RuntimeEntryPoint = {
  title: string;
  detail: string;
  category: string;
  confidence: number;
};

export type RuntimeFlowStep = {
  stage: string;
  title: string;
  detail: string;
  kind: string;
  evidenceType?: string | null;
  explorerSurface?: "files" | "apis" | "backend" | "azure" | null;
  explorerQuery?: string | null;
  repositoryExplorerItemId?: string | null;
};

export type RuntimeFlow = {
  title: string;
  summary: string;
  category: string;
  confidence: number;
  steps: RuntimeFlowStep[];
};

export type RuntimeExecutionPath = {
  entryPointTitle: string;
  entryPointDetail: string;
  category: string;
  summary: string;
  flows: RuntimeFlow[];
};

export type RuntimeFlowMap = {
  name: string;
  kind: string;
  summary: string;
  entryPoints: RuntimeEntryPoint[];
  executionPaths: RuntimeExecutionPath[];
  flows: RuntimeFlow[];
};

export type RepositoryExplorerItem = {
  id: string;
  surface: "files" | "apis" | "backend" | "azure";
  title: string;
  subtitle: string;
  detail: string;
  sourceKind: string;
  sourceIdentifier: string;
  relationshipKind?: string | null;
  targetKind?: string | null;
  targetIdentifier?: string | null;
  filePath?: string | null;
  lineNumber?: number | null;
  metadata?: string | null;
  displayTitle?: string | null;
  displaySubtitle?: string | null;
  displayLocator?: string | null;
  evidenceSummary?: string | null;
  occurrenceKey?: string | null;
};

export type RepositoryExplorerRelatedGroup = {
  label: string;
  rows: RepositoryExplorerItem[];
};

export type CSharpBackendTraceStep = {
  number: number;
  stage: string;
  title: string;
  detail: string;
  confidence: string;
  reason: string;
  evidenceItemId?: string | null;
  sourceKind?: string | null;
  sourceIdentifier?: string | null;
  targetKind?: string | null;
  targetIdentifier?: string | null;
  metadata?: string | null;
  category: string;
  isFrameworkCall: boolean;
  isBoundary: boolean;
  isHiddenByDefault: boolean;
  hiddenReason?: string | null;
  sourceFilePath?: string | null;
  sourceLineNumber?: number | null;
  sourcePreview?: string | null;
  continuationEntry?: string | null;
};

export type CSharpBackendTrace = {
  query: string;
  status: string;
  steps: CSharpBackendTraceStep[];
  warnings: string[];
  hiddenStepCount: number;
  hasMore: boolean;
  continuationEntry?: string | null;
  stopReason?: string | null;
};

export type CSharpTracePreferences = {
  defaultDepth: number;
  showFrameworkCalls: boolean;
  showBoundaryCalls: boolean;
};

export type OverviewSection = "guide" | "technology" | "flow" | "api" | "data" | "azure" | "folder";
