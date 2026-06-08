export type ApiHealth = {
  status: string;
  name: string;
  processId: number;
  runtimeFile?: string | null;
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

export type OverviewSection = "guide" | "technology" | "flow" | "api" | "data" | "azure" | "folder";
