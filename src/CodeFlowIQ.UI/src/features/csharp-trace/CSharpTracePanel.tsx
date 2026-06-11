import { useMemo, useState } from "react";
import { AlertTriangle, ArrowRight, Code2, Database, ExternalLink, GitBranch, Search } from "lucide-react";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import type { CSharpBackendTrace, CSharpBackendTraceStep, CSharpTracePreferences } from "../../types";
import type { ExplorerDrillTarget } from "../repository-explorer";
import "./csharp-trace.css";

type CSharpTracePanelProps = {
  entry: string;
  depth: number;
  trace: CSharpBackendTrace | null;
  routeCandidates: string[];
  preferences: CSharpTracePreferences;
  disabled: boolean;
  onEntryChange: (value: string) => void;
  onDepthChange: (value: number) => void;
  onTrace: (entry?: string) => void;
  onLoadRoutes: () => void;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

export function CSharpTracePanel({
  entry,
  depth,
  trace,
  routeCandidates,
  preferences,
  disabled,
  onEntryChange,
  onDepthChange,
  onTrace,
  onLoadRoutes,
  onOpenExplorer
}: CSharpTracePanelProps) {
  const selectedSteps = trace?.steps ?? [];
  const visibleSteps = getVisibleTraceSteps(selectedSteps, preferences);
  const groupedSteps = groupTraceSteps(visibleSteps);
  const routeOptions = routeCandidates.map(getEntryCandidate).filter(Boolean).slice(0, 500);
  const [showEntrySuggestions, setShowEntrySuggestions] = useState(false);
  const visibleEntryOptions = useMemo(
    () => getVisibleEntryOptions(routeOptions, entry),
    [routeOptions, entry]
  );

  return (
    <div className="flow-layout csharp-trace">
      <FeatureIntro
        title="C# Backend Trace"
        description="Follow one backend request like a debugger: route, controller, keyed DI, base and override calls, repositories, SQL objects, and external handoffs."
        helper="Use this when you want to understand the real execution path behind a C# API or method without jumping file by file manually."
      />

      <div className="toolbar trace-toolbar">
        <label className="trace-entry-field">
          API route or method
          <input
            aria-autocomplete="list"
            aria-controls="csharp-trace-routes"
            aria-expanded={showEntrySuggestions && visibleEntryOptions.length > 0}
            placeholder="Example: POST /v4/engagements/{engagementId}/CarryForwardLevvia/FinancialTrialBalanceCarryForward"
            value={entry}
            onBlur={() => window.setTimeout(() => setShowEntrySuggestions(false), 120)}
            onChange={(event) => {
              onEntryChange(event.target.value);
              setShowEntrySuggestions(true);
            }}
            onFocus={() => setShowEntrySuggestions(true)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !disabled && entry.trim().length > 0) {
                event.preventDefault();
                setShowEntrySuggestions(false);
                onTrace(entry);
              }
            }}
          />
          {showEntrySuggestions && visibleEntryOptions.length > 0 && (
            <div className="trace-entry-results" id="csharp-trace-routes" role="listbox">
              {visibleEntryOptions.map((route) => (
                <button
                  key={route}
                  title={route}
                  type="button"
                  role="option"
                  onMouseDown={(event) => {
                    event.preventDefault();
                    onEntryChange(route);
                    setShowEntrySuggestions(false);
                  }}
                  onClick={() => {
                    onEntryChange(route);
                    setShowEntrySuggestions(false);
                  }}
                >
                  {route}
                </button>
              ))}
            </div>
          )}
        </label>
        <label className="short-field">
          Depth
          <input
            min={8}
            max={200}
            type="number"
            value={depth}
            onChange={(event) => onDepthChange(Number(event.target.value))}
          />
        </label>
        <button
          type="button"
          onClick={() => onTrace(entry)}
          disabled={disabled || entry.trim().length === 0}
          title={disabled ? "Load or refresh the repository index before tracing." : undefined}
        >
          <GitBranch size={17} /> Trace path
        </button>
        <button
          type="button"
          onClick={onLoadRoutes}
          disabled={disabled}
          title={disabled ? "Load or refresh the repository index before loading trace entries." : undefined}
        >
          <Search size={17} /> Load routes and methods
        </button>
      </div>

      {!trace && (
        <div className="trace-empty">
          <Search size={22} />
          <strong>Choose a C# entry point to trace</strong>
          <span>Paste an API route or method name. CodeFlowIQ will show the execution path and call out unresolved handoffs.</span>
        </div>
      )}

      {trace && (
        <div className="trace-workspace">
          <aside className="trace-summary-panel">
            <div>
              <span>Status</span>
              <strong>{trace.status}</strong>
            </div>
            <div>
              <span>Steps</span>
              <strong>{visibleSteps.length}</strong>
            </div>
            <div>
              <span>Hidden</span>
              <strong>{trace.steps.length - visibleSteps.length}</strong>
            </div>
            <p>{trace.query}</p>
          </aside>

          <section className="trace-main-panel">
            {(trace.hiddenStepCount > 0 || trace.hasMore) && (
              <div className="trace-notice">
                <Code2 size={17} />
                <span>
                  {trace.hiddenStepCount > 0 && `${trace.hiddenStepCount} framework/package step${trace.hiddenStepCount === 1 ? "" : "s"} hidden by default. `}
                  {trace.hasMore && trace.stopReason}
                </span>
                {trace.hasMore && trace.continuationEntry && (
                  <button type="button" onClick={() => onTrace(trace.continuationEntry ?? undefined)}>
                    Continue from last app step
                  </button>
                )}
              </div>
            )}

            {trace.warnings.length > 0 && (
              <div className="trace-warnings">
                <AlertTriangle size={17} />
                <div>
                  <strong>Needs review</strong>
                  {trace.warnings.map((warning) => <span key={warning}>{warning}</span>)}
                </div>
              </div>
            )}

            {groupedSteps.map((group) => (
              <TraceStageGroup key={group.stage} group={group} onOpenExplorer={onOpenExplorer} />
            ))}
          </section>
        </div>
      )}
    </div>
  );
}

type TraceGroup = {
  stage: string;
  steps: CSharpBackendTraceStep[];
};

function TraceStageGroup({
  group,
  onOpenExplorer
}: {
  group: TraceGroup;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
}) {
  return (
    <section className="trace-stage-group">
      <header>
        <strong>{group.stage}</strong>
        <span>{group.steps.length} step{group.steps.length === 1 ? "" : "s"}</span>
      </header>
      <div className="trace-step-list">
        {group.steps.map((step) => (
          <TraceStep key={`${step.number}-${step.title}-${step.targetIdentifier}`} step={step} onOpenExplorer={onOpenExplorer} />
        ))}
      </div>
    </section>
  );
}

function TraceStep({
  step,
  onOpenExplorer
}: {
  step: CSharpBackendTraceStep;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
}) {
  const explorerTarget = getTraceExplorerTarget(step);

  return (
    <article className={`trace-step ${step.confidence}`}>
      <div className="trace-step-number">{step.number}</div>
      <div className="trace-step-body">
        <div className="trace-step-heading">
          <strong>{step.title}</strong>
          <span>{step.confidence}</span>
        </div>
        <p>{step.detail}</p>
        {(step.sourceFilePath || step.sourcePreview) && (
          <div className="trace-source-preview">
            <span>
              {step.sourceFilePath}
              {step.sourceLineNumber ? `:${step.sourceLineNumber}` : ""}
            </span>
            {step.sourcePreview && <code>{step.sourcePreview}</code>}
          </div>
        )}
        <div className="trace-identifiers">
          {step.sourceIdentifier && <code>{compactIdentifier(step.sourceIdentifier)}</code>}
          {step.sourceIdentifier && step.targetIdentifier && <ArrowRight size={14} />}
          {step.targetIdentifier && <code>{compactIdentifier(step.targetIdentifier)}</code>}
        </div>
        <details>
          <summary>Why CodeFlowIQ believes this</summary>
          <span>{step.reason}</span>
          {step.metadata && <code>{step.metadata}</code>}
        </details>
      </div>
      <div className="trace-step-actions">
        {isSqlStep(step) && <Database size={16} />}
        {explorerTarget && (
          <button type="button" onClick={() => onOpenExplorer(explorerTarget)}>
            <ExternalLink size={15} /> Evidence
          </button>
        )}
      </div>
    </article>
  );
}

function groupTraceSteps(steps: CSharpBackendTraceStep[]) {
  const groups: TraceGroup[] = [];
  for (const step of steps) {
    const stage = getDisplayStage(step);
    const current = groups[groups.length - 1];
    if (current?.stage === stage) {
      current.steps.push(step);
    } else {
      groups.push({ stage, steps: [step] });
    }
  }

  return groups;
}

function getVisibleTraceSteps(steps: CSharpBackendTraceStep[], preferences: CSharpTracePreferences) {
  return steps.filter((step) => {
    if (!preferences.showFrameworkCalls && step.isFrameworkCall) {
      return false;
    }

    if (!preferences.showBoundaryCalls && step.isBoundary) {
      return false;
    }

    return true;
  });
}

function getDisplayStage(step: CSharpBackendTraceStep) {
  switch (step.category) {
    case "entry":
      return "Entry point";
    case "handoff":
      return "Dependency handoffs";
    case "data":
      return "SQL and data access";
    case "boundary":
      return "External or unresolved boundaries";
    default:
      return "Application code";
  }
}

function getTraceExplorerTarget(step: CSharpBackendTraceStep): ExplorerDrillTarget | null {
  if (!step.evidenceItemId) {
    return null;
  }

  return {
    surface: isApiStep(step) ? "apis" : isCloudStep(step) ? "azure" : "backend",
    query: step.targetIdentifier ?? step.sourceIdentifier ?? step.title,
    selectedItemId: step.evidenceItemId
  };
}

function isApiStep(step: CSharpBackendTraceStep) {
  return step.stage.toLowerCase().includes("api") || step.targetKind === "api";
}

function isCloudStep(step: CSharpBackendTraceStep) {
  return step.title.toLowerCase().includes("blob") || step.detail.toLowerCase().includes("blob");
}

function isSqlStep(step: CSharpBackendTraceStep) {
  const text = `${step.stage} ${step.title} ${step.detail}`.toLowerCase();
  return text.includes("sql") || text.includes("table") || text.includes("procedure");
}

function compactIdentifier(value: string) {
  return value.length > 120 ? `...${value.slice(-117)}` : value;
}

function getEntryCandidate(row: string) {
  return row.trim();
}

function getVisibleEntryOptions(options: string[], query: string) {
  const normalizedQuery = query.trim().toLowerCase();
  const matches = normalizedQuery
    ? options.filter((option) => option.toLowerCase().includes(normalizedQuery))
    : options;

  return matches.slice(0, 14);
}
