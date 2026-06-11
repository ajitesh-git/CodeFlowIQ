import type { OverviewSection, RepositoryOverviewItem } from "../../types";
import type { RepositoryExplorerSurface } from "../repository-explorer";

export type AppPanel =
  | "overview"
  | "runtime"
  | "explorer"
  | "summary"
  | "chains"
  | "csharpTrace"
  | "backend"
  | "apis"
  | "azure"
  | "files"
  | "settings";

export type OverviewNavigation =
  | { panel: "explorer"; surface: RepositoryExplorerSurface; query: string }
  | { panel: "files"; language: string; folder: string; take?: number }
  | { panel: "chains"; api: string; target: string; take?: number }
  | { panel: "apis"; route: string; take?: number }
  | { panel: "backend"; target: string; take?: number }
  | { panel: "azure"; service: string; take?: number };

export function getOverviewItemNavigation(section: OverviewSection, item: RepositoryOverviewItem): OverviewNavigation {
  if (section === "technology") {
    return { panel: "explorer", surface: "files", query: toLanguageId(item.title) };
  }

  if (section === "flow") {
    return { panel: "explorer", surface: "backend", query: item.title };
  }

  if (section === "api") {
    return { panel: "explorer", surface: "apis", query: item.title };
  }

  if (section === "data") {
    return { panel: "explorer", surface: "backend", query: item.title };
  }

  if (section === "azure") {
    return { panel: "explorer", surface: "azure", query: item.title };
  }

  if (section === "folder") {
    return { panel: "explorer", surface: "files", query: item.title };
  }

  if (item.title.includes("API")) {
    return { panel: "explorer", surface: "apis", query: "" };
  }

  if (item.title.includes("data")) {
    return { panel: "explorer", surface: "backend", query: "" };
  }

  if (item.title.includes("Azure")) {
    return { panel: "explorer", surface: "azure", query: "" };
  }

  if (item.title.includes("folder")) {
    return { panel: "explorer", surface: "files", query: "" };
  }

  return { panel: "explorer", surface: "backend", query: "" };
}

export function getOverviewSectionNavigation(section: OverviewSection): OverviewNavigation {
  if (section === "technology" || section === "folder") {
    return { panel: "explorer", surface: "files", query: "" };
  }

  if (section === "api") {
    return { panel: "explorer", surface: "apis", query: "" };
  }

  if (section === "data") {
    return { panel: "explorer", surface: "backend", query: "" };
  }

  if (section === "azure") {
    return { panel: "explorer", surface: "azure", query: "" };
  }

  return { panel: "explorer", surface: "backend", query: "" };
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
