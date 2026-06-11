import { Activity, Braces, Cloud, Compass, Database, FolderSearch, FolderTree, GitBranch, Network, Play, Settings } from "lucide-react";
import type { AppPanel } from "../../features/overview";

type SidebarProps = {
  activePanel: AppPanel;
  onOpenPanel: (panel: AppPanel) => void;
};

const navItems: Array<{ panel: AppPanel; label: string; icon: typeof Compass }> = [
  { panel: "overview", label: "Start here", icon: Compass },
  { panel: "runtime", label: "Runtime stories", icon: Play },
  { panel: "explorer", label: "Browse evidence", icon: FolderSearch },
  { panel: "summary", label: "Repo snapshot", icon: Activity },
  { panel: "chains", label: "End-to-end flows", icon: Network },
  { panel: "csharpTrace", label: "C# backend trace", icon: GitBranch },
  { panel: "backend", label: "Backend & data", icon: Database },
  { panel: "apis", label: "API endpoints", icon: Braces },
  { panel: "azure", label: "Cloud services", icon: Cloud },
  { panel: "files", label: "Files indexed", icon: FolderTree }
];

export function Sidebar({ activePanel, onOpenPanel }: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="brand-block">
        <div className="brand-mark">CF</div>
        <div className="brand-copy">
          <h1>CodeFlowIQ</h1>
          <span>Local intelligence workspace</span>
        </div>
        <button
          className={activePanel === "settings" ? "sidebar-settings-button active" : "sidebar-settings-button"}
          type="button"
          title="Settings"
          aria-label="Settings"
          onClick={() => onOpenPanel("settings")}
        >
          <Settings size={18} />
        </button>
      </div>

      <nav className="nav-list" aria-label="Main navigation">
        {navItems.map(({ panel, label, icon: Icon }) => (
          <button className={activePanel === panel ? "active" : ""} onClick={() => onOpenPanel(panel)} key={panel}>
            <Icon size={17} /> {label}
          </button>
        ))}
      </nav>

    </aside>
  );
}
