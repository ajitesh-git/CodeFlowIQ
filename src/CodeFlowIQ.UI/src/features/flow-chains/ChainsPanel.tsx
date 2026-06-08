import { Check, Copy, Network, Search } from "lucide-react";
import { CSSProperties, useState } from "react";
import { EmptyState } from "../../components/common/EmptyState";
import "./flow-chains.css";

type ParsedChainStep = {
  depth: number;
  relationship: string | null;
  target: string;
  kind: string;
  displayName: string;
  stage: "frontend" | "api" | "backend" | "database" | "azure" | "unknown";
};

type ChainsPanelProps = {
  apiFilter: string;
  targetFilter: string;
  chainLimit: number;
  chains: string[];
  disabled: boolean;
  onApiFilterChange: (value: string) => void;
  onTargetFilterChange: (value: string) => void;
  onChainLimitChange: (value: number) => void;
  onLoad: (apiOverride?: string, targetOverride?: string, takeOverride?: number) => void;
};

export function ChainsPanel({
  apiFilter,
  targetFilter,
  chainLimit,
  chains,
  disabled,
  onApiFilterChange,
  onTargetFilterChange,
  onChainLimitChange,
  onLoad
}: ChainsPanelProps) {
  async function copyAllChains() {
    if (chains.length === 0) {
      return;
    }

    await navigator.clipboard.writeText(chains.join("\n\n"));
  }

  return (
    <div className="flow-layout">
      <div className="toolbar">
        <label>
          API filter
          <input value={apiFilter} onChange={(event) => onApiFilterChange(event.target.value)} />
        </label>
        <label>
          Target
          <input value={targetFilter} onChange={(event) => onTargetFilterChange(event.target.value)} />
        </label>
        <label className="short-field">
          Take
          <input
            min={1}
            max={50}
            type="number"
            value={chainLimit}
            onChange={(event) => onChainLimitChange(Number(event.target.value))}
          />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <Network size={17} /> Trace
        </button>
        <button onClick={() => {
          onApiFilterChange("");
          onTargetFilterChange("");
          onChainLimitChange(50);
          onLoad("", "", 50);
        }} disabled={disabled}>
          <Search size={17} /> Browse all
        </button>
        <button onClick={copyAllChains} disabled={chains.length === 0}>
          <Copy size={17} /> Copy
        </button>
      </div>
      <div className="chain-list">
        {chains.length === 0 ? (
          <EmptyState label="No chains loaded" />
        ) : (
          chains.map((chain, index) => (
            <ChainCard chain={chain} index={index} key={`${chain}-${index}`} />
          ))
        )}
      </div>
    </div>
  );
}

function ChainCard({ chain, index }: { chain: string; index: number }) {
  const steps = parseChain(chain);
  const [copied, setCopied] = useState(false);
  const start = steps[0]?.displayName ?? "Flow chain";
  const end = steps.at(-1)?.displayName ?? "Unknown target";
  const stageCounts = getStageCounts(steps);
  const headline = buildFlowHeadline(steps);

  async function copyChain() {
    await navigator.clipboard.writeText(chain);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1200);
  }

  return (
    <details className="chain-card" open={index === 0}>
      <summary>
        <Network size={16} />
        <span>{headline}</span>
        <strong>{Math.max(steps.length - 1, 0)} steps</strong>
      </summary>
      <div className="chain-overview">
        <div>
          <span>Entry point</span>
          <strong>{start}</strong>
        </div>
        <div>
          <span>Final dependency</span>
          <strong>{end}</strong>
        </div>
        <div className="stage-strip" aria-label="Chain stage counts">
          {stageCounts.map(([stage, count]) => (
            <span className={`stage-chip ${stage}`} key={stage}>{stage} {count}</span>
          ))}
        </div>
        <button className="copy-chain" onClick={copyChain}>
          {copied ? <Check size={16} /> : <Copy size={16} />}
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <ol className="flow-story">
        {steps.map((step, stepIndex) => (
          <li
            className={`story-step ${step.stage}`}
            key={`${step.target}-story-${stepIndex}`}
          >
            <span className={`stage-marker ${step.stage}`}>{stageLabel(step.stage)}</span>
            <div>
              <strong>{describeStepTitle(step, stepIndex)}</strong>
              <span>{describeStepDetail(step)}</span>
            </div>
          </li>
        ))}
      </ol>
      <details className="technical-trace">
        <summary>Technical trace</summary>
        <ol className="chain-steps">
          {steps.map((step, stepIndex) => (
            <li
              className={`chain-step ${step.stage}`}
              key={`${step.target}-${stepIndex}`}
              style={{ "--depth": Math.min(step.depth, 5) } as CSSProperties}
            >
              <span className={`stage-marker ${step.stage}`}>{stageLabel(step.stage)}</span>
              {step.relationship && <span className={`edge-label ${edgeClass(step.relationship)}`}>{formatRelationship(step.relationship)}</span>}
              <code title={step.target}>{step.target}</code>
            </li>
          ))}
        </ol>
      </details>
    </details>
  );
}

function parseChain(chain: string): ParsedChainStep[] {
  return chain
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0)
    .map((line) => {
      const depth = Math.floor((line.length - line.trimStart().length) / 2);
      const trimmed = line.trim();
      const separator = " -> ";
      const separatorIndex = trimmed.indexOf(separator);
      if (separatorIndex < 0) {
        return toParsedStep(depth, null, trimmed);
      }

      return toParsedStep(
        depth,
        trimmed.slice(0, separatorIndex),
        trimmed.slice(separatorIndex + separator.length)
      );
    });
}

function toParsedStep(depth: number, relationship: string | null, target: string): ParsedChainStep {
  const kind = target.includes(":") ? target.slice(0, target.indexOf(":")) : "unknown";
  return {
    depth,
    relationship,
    target,
    kind,
    displayName: formatTargetName(target),
    stage: classifyStage(kind, target)
  };
}

function buildFlowHeadline(steps: ParsedChainStep[]) {
  const first = steps[0]?.displayName ?? "Flow chain";
  const api = steps.find((step) => step.stage === "api")?.displayName;
  const final = steps.at(-1)?.displayName;

  if (api && final && api !== final) {
    return `${first} reaches ${api}, then touches ${final}`;
  }

  if (final && first !== final) {
    return `${first} flows to ${final}`;
  }

  return first;
}

function describeStepTitle(step: ParsedChainStep, index: number) {
  if (index === 0) {
    return `Starts at ${step.displayName}`;
  }

  const action = step.relationship ? formatRelationship(step.relationship) : "continues to";
  return `${capitalize(action)} ${step.displayName}`;
}

function describeStepDetail(step: ParsedChainStep) {
  const stage = {
    frontend: "Frontend user interaction or client-side code",
    api: "Backend API endpoint or controller action",
    backend: "Application service, domain, repository, or method call",
    database: "Database table, stored procedure, DbContext, or SQL object",
    azure: "Azure service dependency",
    unknown: "Detected code relationship"
  }[step.stage];

  return `${stage} - ${step.kind}`;
}

function formatTargetName(target: string) {
  const withoutKind = target.includes(":") ? target.slice(target.indexOf(":") + 1) : target;
  const afterPath = withoutKind.split(/[\\/]/).at(-1) ?? withoutKind;
  const parts = afterPath.split("::").filter(Boolean);
  const name = parts.at(-1) ?? afterPath;

  return name
    .replace(/\.(component|controller|service|repository|module)\.(ts|tsx|js|jsx|cs)$/i, "")
    .replace(/\.(ts|tsx|js|jsx|cs|sql)$/i, "")
    .replace(/[_-]+/g, " ")
    .trim() || target;
}

function formatRelationship(relationship: string) {
  return relationship.replace(/_/g, " ");
}

function capitalize(value: string) {
  return value.length === 0 ? value : `${value[0].toUpperCase()}${value.slice(1)}`;
}

function classifyStage(kind: string, target: string): ParsedChainStep["stage"] {
  const lower = target.toLowerCase();
  if (lower.includes(".ts::") || lower.includes(".js::") || lower.includes(".component")) {
    return "frontend";
  }

  if (kind === "api" || lower.includes("controller.cs::")) {
    return "api";
  }

  if (kind === "database-table" || kind === "procedure" || lower.includes("dbcontext")) {
    return "database";
  }

  if (kind === "azure-service") {
    return "azure";
  }

  if (kind === "method" || kind === "symbol" || lower.includes("manager") || lower.includes("repo")) {
    return "backend";
  }

  return "unknown";
}

function getStageCounts(steps: ParsedChainStep[]) {
  const counts = new Map<ParsedChainStep["stage"], number>();
  for (const step of steps) {
    counts.set(step.stage, (counts.get(step.stage) ?? 0) + 1);
  }

  return [...counts.entries()].filter(([, count]) => count > 0);
}

function stageLabel(stage: ParsedChainStep["stage"]) {
  return {
    frontend: "UI",
    api: "API",
    backend: "BE",
    database: "DB",
    azure: "AZ",
    unknown: "?"
  }[stage];
}

function edgeClass(relationship: string) {
  if (relationship === "resolved_to") {
    return "resolve";
  }

  if (relationship.includes("table") || relationship.includes("procedure")) {
    return "data";
  }

  if (relationship.includes("api") || relationship.includes("handler")) {
    return "api";
  }

  return "code";
}
