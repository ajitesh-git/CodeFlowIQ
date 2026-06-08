import type { ApiHealth, RepositoryOverview, RuntimeFlowMap, WorkspaceSummary } from "../types";

type WorkspaceApiClient = {
  checkHealth: () => Promise<ApiHealth>;
  initializeWorkspace: (workspacePath: string) => Promise<unknown>;
  syncWorkspace: (workspacePath: string) => Promise<unknown>;
  loadSummary: (workspacePath: string, take?: number) => Promise<WorkspaceSummary>;
  loadOverview: (workspacePath: string, take?: number) => Promise<RepositoryOverview>;
  loadRuntimeMap: (workspacePath: string, take?: number) => Promise<RuntimeFlowMap>;
  loadChains: (workspacePath: string, api: string, target: string, take: number) => Promise<string[]>;
  loadBackendRows: (workspacePath: string, kind: string, target: string, take: number) => Promise<string[]>;
  loadApiRows: (workspacePath: string, route: string, take: number) => Promise<string[]>;
  loadAzureRows: (workspacePath: string, service: string, take: number) => Promise<string[]>;
  loadFileRows: (workspacePath: string, language: string, folder: string, take: number) => Promise<string[]>;
};

export function createWorkspaceApi(baseUrl: string): WorkspaceApiClient {
  const normalizedBaseUrl = baseUrl.trim().replace(/\/$/, "");

  async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await fetch(`${normalizedBaseUrl}${path}`, init);
    if (!response.ok) {
      const body = await response.text();
      throw new Error(body || `${response.status} ${response.statusText}`);
    }

    return (await response.json()) as T;
  }

  return {
    checkHealth: () => request<ApiHealth>("/health"),
    initializeWorkspace: (workspacePath) =>
      request("/api/workspace/init", workspaceMutation(workspacePath)),
    syncWorkspace: (workspacePath) =>
      request("/api/workspace/sync", workspaceMutation(workspacePath)),
    loadSummary: (workspacePath, take = 8) =>
      request<WorkspaceSummary>(`/api/summary?path=${encode(workspacePath)}&take=${take}`),
    loadOverview: (workspacePath, take = 8) =>
      request<RepositoryOverview>(`/api/overview?path=${encode(workspacePath)}&take=${take}`),
    loadRuntimeMap: (workspacePath, take = 10) =>
      request<RuntimeFlowMap>(`/api/runtime-flows?path=${encode(workspacePath)}&take=${take}`),
    loadChains: (workspacePath, api, target, take) =>
      request<string[]>(`/api/chains?path=${encode(workspacePath)}&api=${encode(api)}&target=${encode(target)}&format=tree&take=${take}&depth=8`),
    loadBackendRows: (workspacePath, kind, target, take) => {
      const kindQuery = kind ? `&kind=${encode(kind)}` : "";
      return request<string[]>(`/api/backend?path=${encode(workspacePath)}${kindQuery}&target=${encode(target)}&take=${take}`);
    },
    loadApiRows: (workspacePath, route, take) =>
      request<string[]>(`/api/apis?path=${encode(workspacePath)}&route=${encode(route)}&take=${take}`),
    loadAzureRows: (workspacePath, service, take) =>
      request<string[]>(`/api/azure?path=${encode(workspacePath)}&service=${encode(service)}&take=${take}`),
    loadFileRows: (workspacePath, language, folder, take) =>
      request<string[]>(`/api/files?path=${encode(workspacePath)}&language=${encode(language)}&folder=${encode(folder)}&take=${take}`)
  };
}

function workspaceMutation(workspacePath: string): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ path: workspacePath.trim() })
  };
}

function encode(value: string) {
  return encodeURIComponent(value.trim());
}
