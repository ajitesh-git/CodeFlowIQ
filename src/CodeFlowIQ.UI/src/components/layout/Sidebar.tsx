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
  { panel: "runtime", label: "Runtime map", icon: Play },
  { panel: "explorer", label: "Repository explorer", icon: FolderSearch },
  { panel: "summary", label: "Summary", icon: Activity },
  { panel: "chains", label: "Flow chains", icon: Network },
  { panel: "backend", label: "Backend graph", icon: Database },
  { panel: "apis", label: "API surface", icon: Braces },
  { panel: "azure", label: "Azure", icon: Cloud },
  { panel: "files", label: "Files", icon: FolderTree }
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
        <Toggle label="Frontend flows" checked />
        <Toggle label="Backend flows" checked />
        <Toggle label="SQL/T-SQL" checked />
        <Toggle label="Azure services" checked />
        <Toggle label="Git history" />
        <Toggle label="LLM assistant" />
      </div>
    </aside>
  );
}
