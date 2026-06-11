import type { RepositoryExplorerSurface } from "./RepositoryExplorerPanel";

export type ExplorerDrillTarget = {
  surface: RepositoryExplorerSurface;
  query: string;
  selectedItemId?: string | null;
  originLabel?: string;
};

export function getExplorerTargetForPreviewRow(surface: RepositoryExplorerSurface, row: string): ExplorerDrillTarget {
  const parts = splitPreviewRow(row);
  const selectedItemId = getPreviewRowEvidenceId(parts);

  if (surface === "files") {
    return { surface, query: parts[1] ?? parts[0] ?? row, selectedItemId };
  }

  if (surface === "apis") {
    return { surface, query: parts[0] ?? row, selectedItemId };
  }

  if (surface === "azure") {
    return { surface, query: parts[0] ?? row, selectedItemId };
  }

  return {
    surface,
    query: sanitizeExplorerQuery(parts[2] ?? parts[0] ?? row),
    selectedItemId
  };
}

export function getExplorerTargetForSummaryRow(title: string, row: string): ExplorerDrillTarget {
  const [name] = splitPreviewRow(row);
  const query = name ?? row;
  const normalizedTitle = title.toLowerCase();

  if (normalizedTitle.includes("language") || normalizedTitle.includes("symbol")) {
    return { surface: "files", query };
  }

  if (normalizedTitle.includes("azure")) {
    return { surface: "azure", query };
  }

  return { surface: "backend", query };
}

export function getExplorerTargetForChainStep(kind: string, target: string, selectedItemId?: string | null): ExplorerDrillTarget {
  const surface = getExplorerSurfaceForKind(kind, target);
  return {
    surface,
    query: sanitizeExplorerQuery(target),
    selectedItemId
  };
}

export function getExplorerSurfaceForKind(kind: string, target: string): RepositoryExplorerSurface {
  const lowerKind = kind.toLowerCase();
  const lowerTarget = target.toLowerCase();

  if (lowerKind === "api" || lowerTarget.includes("controller.cs::")) {
    return "apis";
  }

  if (lowerKind === "azure-service") {
    return "azure";
  }

  if (lowerKind === "file" || lowerTarget.includes(".tsx") || lowerTarget.includes(".ts::") || lowerTarget.includes(".jsx")) {
    return "files";
  }

  return "backend";
}

export function sanitizeExplorerQuery(value: string) {
  const normalized = value
    .replace(/^(method|class|route|api|table|database-table|procedure|file|symbol|azure-service):/i, "")
    .replace(/\.(cs|ts|tsx|jsx|sql|json)$/i, "")
    .trim();
  const qualifiedIndex = normalized.lastIndexOf("::");
  const member = qualifiedIndex >= 0 ? normalized.slice(qualifiedIndex + 2) : normalized;
  return member.split(/[\\/]/).filter(Boolean).pop() ?? member;
}

function splitPreviewRow(row: string) {
  return row
    .split("\t")
    .map((part) => part.trim())
    .filter(Boolean);
}

function getPreviewRowEvidenceId(parts: string[]) {
  return parts.find((part) => /^(file|relationship):\d+$/i.test(part)) ?? null;
}
