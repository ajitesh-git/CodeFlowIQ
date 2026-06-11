import { Check, ChevronLeft, ChevronRight, Copy, Network, Search } from "lucide-react";
import { CSSProperties, useEffect, useMemo, useState } from "react";
import { EmptyState } from "../../components/common/EmptyState";
import { FeatureIntro } from "../../components/common/FeatureIntro";
import { getExplorerTargetForChainStep, type ExplorerDrillTarget } from "../repository-explorer";
import "./flow-chains.css";

type ParsedChainStep = {
  depth: number;
  relationship: string | null;
  target: string;
  evidenceItemId: string | null;
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
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
};

const chainsPerPage = 25;

export function ChainsPanel({
  apiFilter,
  targetFilter,
  chainLimit,
  chains,
  disabled,
  onApiFilterChange,
  onTargetFilterChange,
  onChainLimitChange,
  onLoad,
  onOpenExplorer
}: ChainsPanelProps) {
  const [currentPage, setCurrentPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(chains.length / chainsPerPage));
  const safeCurrentPage = Math.min(currentPage, pageCount);
  const pageStartIndex = (safeCurrentPage - 1) * chainsPerPage;
  const visibleChains = useMemo(
    () => chains.slice(pageStartIndex, pageStartIndex + chainsPerPage),
    [chains, pageStartIndex]
  );

  useEffect(() => {
    setCurrentPage(1);
  }, [chains]);

  useEffect(() => {
    if (currentPage > pageCount) {
      setCurrentPage(pageCount);
    }
  }, [currentPage, pageCount]);

  async function copyAllChains() {
    if (chains.length === 0) {
      return;
    }

    await navigator.clipboard.writeText(chains.map(cleanChainEvidenceMetadata).join("\n\n"));
  }

  return (
    <div className="flow-layout">
      <FeatureIntro
        title="End-to-End Flow Chains"
        description="Trace one business path across UI, API, backend code, database objects, and cloud services. Each chain is shown first as a readable story, with the technical trace kept underneath."
        helper="Use this page when you want to answer: if this API or feature runs, what does it eventually touch?"
      />
      <div className="toolbar">
        <label>
          Starting API or feature
          <input placeholder="Example: register, login, account" value={apiFilter} onChange={(event) => onApiFilterChange(event.target.value)} />
        </label>
        <label>
          Data or dependency target
          <input placeholder="Example: Users, invoice, Service Bus" value={targetFilter} onChange={(event) => onTargetFilterChange(event.target.value)} />
        </label>
        <label className="short-field">
          Limit
          <input
            min={1}
            max={1000}
            type="number"
            value={chainLimit}
            onChange={(event) => onChainLimitChange(Number(event.target.value))}
          />
        </label>
        <button onClick={() => onLoad()} disabled={disabled}>
          <Network size={17} /> Trace flow
        </button>
        <button onClick={() => {
          onApiFilterChange("");
          onTargetFilterChange("");
          onChainLimitChange(1000);
          onLoad("", "", 1000);
        }} disabled={disabled}>
          <Search size={17} /> Browse all chains
        </button>
        <button onClick={copyAllChains} disabled={chains.length === 0}>
          <Copy size={17} /> Copy all
        </button>
      </div>
      {chains.length > 0 && (
        <ChainPagination
          currentPage={safeCurrentPage}
          end={Math.min(pageStartIndex + visibleChains.length, chains.length)}
          onPageChange={setCurrentPage}
          pageCount={pageCount}
          pageSize={chainsPerPage}
          start={pageStartIndex + 1}
          total={chains.length}
        />
      )}
      <div className="chain-list">
        {chains.length === 0 ? (
          <EmptyState label="No flow chains loaded yet. Add a filter or browse all chains." />
        ) : (
          visibleChains.map((chain, index) => (
            <ChainCard
              chain={chain}
              index={pageStartIndex + index}
              isFirstVisible={index === 0}
              key={`${chain}-${pageStartIndex + index}`}
              onOpenExplorer={onOpenExplorer}
            />
          ))
        )}
      </div>
      {chains.length > chainsPerPage && (
        <ChainPagination
          currentPage={safeCurrentPage}
          end={Math.min(pageStartIndex + visibleChains.length, chains.length)}
          onPageChange={setCurrentPage}
          pageCount={pageCount}
          pageSize={chainsPerPage}
          start={pageStartIndex + 1}
          total={chains.length}
        />
      )}
    </div>
  );
}

function ChainPagination({
  currentPage,
  end,
  onPageChange,
  pageCount,
  pageSize,
  start,
  total
}: {
  currentPage: number;
  end: number;
  onPageChange: (page: number) => void;
  pageCount: number;
  pageSize: number;
  start: number;
  total: number;
}) {
  return (
    <div className="chain-pagination" aria-label="Flow chain pagination">
      <div>
        <strong>{total}</strong>
        <span>loaded chains</span>
        <small>Showing {start}-{end} in pages of {pageSize}</small>
      </div>
      <div className="chain-pagination-actions">
        <button onClick={() => onPageChange(currentPage - 1)} disabled={currentPage <= 1}>
          <ChevronLeft size={16} /> Previous
        </button>
        <span>Page {currentPage} of {pageCount}</span>
        <button onClick={() => onPageChange(currentPage + 1)} disabled={currentPage >= pageCount}>
          Next <ChevronRight size={16} />
        </button>
      </div>
    </div>
  );
}

function ChainCard({
  chain,
  index,
  isFirstVisible,
  onOpenExplorer
}: {
  chain: string;
  index: number;
  isFirstVisible: boolean;
  onOpenExplorer: (target: ExplorerDrillTarget) => void;
}) {
  const steps = parseChain(chain);
  const [copied, setCopied] = useState(false);
  const start = steps[0]?.displayName ?? "Flow chain";
  const end = steps.at(-1)?.displayName ?? "Unknown target";
  const stageCounts = getStageCounts(steps);
  const headline = buildFlowHeadline(steps);

  async function copyChain() {
    await navigator.clipboard.writeText(cleanChainEvidenceMetadata(chain));
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1200);
  }

  return (
    <details className="chain-card" open={isFirstVisible}>
      <summary>
        <Network size={16} />
        <span>{headline}</span>
        <strong>{Math.max(steps.length - 1, 0)} steps</strong>
      </summary>
      <div className="chain-overview">
        <div>
          <span>Where it starts</span>
          <strong>{start}</strong>
        </div>
        <div>
          <span>Where it ends</span>
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
              <button
                className="chain-step-explorer"
                onClick={() => onOpenExplorer(getExplorerTargetForChainStep(step.kind, step.target, step.evidenceItemId))}
                type="button"
              >
                <Search size={15} /> View evidence
              </button>
            </div>
          </li>
        ))}
      </ol>
      <details className="technical-trace">
        <summary>Show technical trace</summary>
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
              <button
                className="chain-step-explorer compact"
                onClick={() => onOpenExplorer(getExplorerTargetForChainStep(step.kind, step.target, step.evidenceItemId))}
                type="button"
              >
                <Search size={14} />
              </button>
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
  const { evidenceItemId, targetText } = parseStepTargetMetadata(target);
  const kind = targetText.includes(":") ? targetText.slice(0, targetText.indexOf(":")) : "unknown";
  return {
    depth,
    relationship,
    target: targetText,
    evidenceItemId,
    kind,
    displayName: formatTargetName(targetText),
    stage: classifyStage(kind, targetText)
  };
}

function parseStepTargetMetadata(target: string) {
  const parts = target.split("\t").map((part) => part.trim()).filter(Boolean);
  const evidenceItemId = parts.find((part) => /^relationship:\d+$/i.test(part)) ?? null;
  const targetText = parts.filter((part) => !/^relationship:\d+$/i.test(part)).join("\t") || target;
  return { evidenceItemId, targetText };
}

function cleanChainEvidenceMetadata(chain: string) {
  return chain
    .split(/\r?\n/)
    .map((line) => line
      .split("\t")
      .filter((part) => !/^relationship:\d+$/i.test(part.trim()))
      .join("\t"))
    .join("\n");
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
    backend: "Code",
    database: "Data",
    azure: "Cloud",
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
