import type { OverviewSection, RepositoryOverviewItem } from "../../types";

export type AppPanel = "overview" | "runtime" | "summary" | "chains" | "backend" | "apis" | "azure" | "files";

export type OverviewNavigation =
  | { panel: "files"; language: string; folder: string; take?: number }
  | { panel: "chains"; api: string; target: string; take?: number }
  | { panel: "apis"; route: string; take?: number }
  | { panel: "backend"; target: string; take?: number }
  | { panel: "azure"; service: string; take?: number };

export function getOverviewItemNavigation(section: OverviewSection, item: RepositoryOverviewItem): OverviewNavigation {
  if (section === "technology") {
    return { panel: "files", language: toLanguageId(item.title), folder: "" };
  }

  if (section === "flow") {
    return { panel: "chains", api: item.title, target: "" };
  }

  if (section === "api") {
    return { panel: "apis", route: item.title };
  }

  if (section === "data") {
    return { panel: "backend", target: item.title };
  }

  if (section === "azure") {
    return { panel: "azure", service: item.title };
  }

  if (section === "folder") {
    return { panel: "files", language: "", folder: item.title };
  }

  if (item.title.includes("API")) {
    return { panel: "apis", route: "" };
  }

  if (item.title.includes("data")) {
    return { panel: "backend", target: "" };
  }

  if (item.title.includes("Azure")) {
    return { panel: "azure", service: "" };
  }

  if (item.title.includes("folder")) {
    return { panel: "files", language: "", folder: "" };
  }

  return { panel: "chains", api: "", target: "" };
}

export function getOverviewSectionNavigation(section: OverviewSection): OverviewNavigation {
  if (section === "technology" || section === "folder") {
    return { panel: "files", language: "", folder: "", take: 1000 };
  }

  if (section === "api") {
    return { panel: "apis", route: "", take: 1000 };
  }

  if (section === "data") {
    return { panel: "backend", target: "", take: 1000 };
  }

  if (section === "azure") {
    return { panel: "azure", service: "", take: 1000 };
  }

  return { panel: "chains", api: "", target: "", take: 50 };
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
