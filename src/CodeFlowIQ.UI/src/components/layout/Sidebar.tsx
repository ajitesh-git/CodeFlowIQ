import { Activity, Braces, Cloud, Compass, Database, FolderSearch, FolderTree, Moon, Network, Play, Sun } from "lucide-react";
import { Toggle } from "../common/Toggle";
import type { AppPanel } from "../../features/overview";

type SidebarProps = {
  activePanel: AppPanel;
  theme: "light" | "dark";
  onOpenPanel: (panel: AppPanel) => void;
  onThemeChange: (theme: "light" | "dark") => void;
};

const navItems: Array<{ panel: AppPanel; label: string; icon: typeof Compass }> = [
  { panel: "overview", label: "Start here", icon: Compass },
  { panel: "runtime", label: "Runtime stories", icon: Play },
  { panel: "explorer", label: "Browse evidence", icon: FolderSearch },
  { panel: "summary", label: "Repo snapshot", icon: Activity },
  { panel: "chains", label: "End-to-end flows", icon: Network },
  { panel: "backend", label: "Backend & data", icon: Database },
  { panel: "apis", label: "API endpoints", icon: Braces },
  { panel: "azure", label: "Cloud services", icon: Cloud },
  { panel: "files", label: "Files indexed", icon: FolderTree }
];

export function Sidebar({ activePanel, theme, onOpenPanel, onThemeChange }: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="brand-block">
        <div className="brand-mark">CF</div>
        <div>
          <h1>CodeFlowIQ</h1>
          <span>Local intelligence workspace</span>
        </div>
      </div>

      <nav className="nav-list" aria-label="Main navigation">
        {navItems.map(({ panel, label, icon: Icon }) => (
          <button className={activePanel === panel ? "active" : ""} onClick={() => onOpenPanel(panel)} key={panel}>
            <Icon size={17} /> {label}
          </button>
        ))}
      </nav>

      <div className="feature-toggles">
        <div className="theme-switch" role="group" aria-label="Theme">
          <button
            className={theme === "light" ? "active" : ""}
            onClick={() => onThemeChange("light")}
            title="Use light theme"
            type="button"
          >
            <Sun size={15} /> Light
          </button>
          <button
            className={theme === "dark" ? "active" : ""}
            onClick={() => onThemeChange("dark")}
            title="Use dark theme"
            type="button"
          >
            <Moon size={15} /> Dark
          </button>
        </div>
        <Toggle label="Frontend evidence" checked />
        <Toggle label="Backend evidence" checked />
        <Toggle label="SQL/data evidence" checked />
        <Toggle label="Cloud evidence" checked />
        <Toggle label="Git history" />
        <Toggle label="LLM assistant" />
      </div>
    </aside>
  );
}
