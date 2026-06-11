import { Cloud, Database, GitBranch, Moon, Sparkles, Sun, Workflow } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { Toggle } from "../../components/common/Toggle";
import type { CSharpTracePreferences } from "../../types";
import "./settings.css";

type SettingsPanelProps = {
  theme: "light" | "dark";
  onThemeChange: (theme: "light" | "dark") => void;
  tracePreferences: CSharpTracePreferences;
  onTracePreferencesChange: (preferences: Partial<CSharpTracePreferences>) => void;
};

export function SettingsPanel({
  theme,
  onThemeChange,
  tracePreferences,
  onTracePreferencesChange
}: SettingsPanelProps) {
  return (
    <div className="settings-page">
      <FeatureIntro
        title="Settings"
        description="Tune how CodeFlowIQ presents evidence while you explore a repository."
        helper="These settings are kept away from the sidebar so navigation stays stable on long screens."
      />

      <section className="settings-section" aria-labelledby="theme-settings-title">
        <header>
          <span>Appearance</span>
          <h2 id="theme-settings-title">Theme</h2>
        </header>
        <div className="settings-theme-switch" role="group" aria-label="Theme">
          <button
            className={theme === "light" ? "active" : ""}
            onClick={() => onThemeChange("light")}
            title="Use light theme"
            type="button"
          >
            <Sun size={17} /> Light
          </button>
          <button
            className={theme === "dark" ? "active" : ""}
            onClick={() => onThemeChange("dark")}
            title="Use dark theme"
            type="button"
          >
            <Moon size={17} /> Dark
          </button>
        </div>
      </section>

      <section className="settings-section" aria-labelledby="evidence-settings-title">
        <header>
          <span>Repository evidence</span>
          <h2 id="evidence-settings-title">Evidence layers</h2>
        </header>
        <div className="settings-toggle-grid">
          <Toggle label="Frontend evidence" checked />
          <Toggle label="Backend evidence" checked />
          <Toggle label="SQL/data evidence" checked />
          <Toggle label="Cloud evidence" checked />
          <Toggle label="Git history" />
          <Toggle label="LLM assistant" />
        </div>
      </section>

      <section className="settings-section" aria-labelledby="future-settings-title">
        <header>
          <span>C# backend trace</span>
          <h2 id="future-settings-title">Trace preferences</h2>
        </header>
        <div className="settings-trace-controls">
          <label>
            Default trace depth
            <input
              min={8}
              max={200}
              type="number"
              value={tracePreferences.defaultDepth}
              onChange={(event) => onTracePreferencesChange({ defaultDepth: Number(event.target.value) })}
            />
          </label>
          <Toggle
            label="Show framework/package calls"
            checked={tracePreferences.showFrameworkCalls}
            onChange={(checked) => onTracePreferencesChange({ showFrameworkCalls: checked })}
          />
          <Toggle
            label="Show unresolved boundary calls"
            checked={tracePreferences.showBoundaryCalls}
            onChange={(checked) => onTracePreferencesChange({ showBoundaryCalls: checked })}
          />
        </div>
        <div className="settings-preference-list">
          <div><Workflow size={18} /><span>App-code-first execution path with expandable hidden calls</span></div>
          <div><Database size={18} /><span>SQL procedures and table touchpoints stay visible by default</span></div>
          <div><Cloud size={18} /><span>Cloud and HTTP calls are marked as boundaries when source is not indexed</span></div>
          <div><GitBranch size={18} /><span>Continue tracing from the last app-code step when depth is reached</span></div>
          <div><Sparkles size={18} /><span>Source previews explain why each step appears in the path</span></div>
        </div>
      </section>
    </div>
  );
}
