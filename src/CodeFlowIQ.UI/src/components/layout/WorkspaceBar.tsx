import { FormEvent } from "react";
import { CheckCircle2, Loader2, Play, RefreshCw, RotateCcw, Search, Square, XCircle } from "lucide-react";
import type { IndexingJobStatus } from "../../types";

type WorkspaceBarProps = {
  workspacePath: string;
  disabled: boolean;
  indexingStatus: IndexingJobStatus | null;
  onWorkspacePathChange: (value: string) => void;
  onInitialize: () => void;
  onSync: () => void;
  onCancelIndexing: () => void;
  onRetryIndexing: () => void;
  onLoad: () => void;
};

export function WorkspaceBar({
  workspacePath,
  disabled,
  indexingStatus,
  onWorkspacePathChange,
  onInitialize,
  onSync,
  onCancelIndexing,
  onRetryIndexing,
  onLoad
}: WorkspaceBarProps) {
  function submitWorkspace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onLoad();
  }

  const showIndexingStatus = indexingStatus
    && ["queued", "running", "cancelling", "cancelled", "completed", "failed"].includes(indexingStatus.state);

  return (
    <section className="workspace-bar">
      <form onSubmit={submitWorkspace}>
        <label>
          Repository folder
          <input
            placeholder="C:\\Users\\you\\source\\sample-enterprise-platform"
            value={workspacePath}
            onChange={(event) => onWorkspacePathChange(event.target.value)}
          />
        </label>
        <button type="button" onClick={onInitialize} disabled={disabled}>
          <Play size={17} /> Index repo
        </button>
        <button type="button" onClick={onSync} disabled={disabled}>
          <RefreshCw size={17} /> Refresh index
        </button>
        <button type="submit" disabled={disabled}>
          <Search size={17} /> Load insights
        </button>
      </form>
      {showIndexingStatus && (
        <IndexingStatusPanel
          status={indexingStatus}
          onCancel={onCancelIndexing}
          onRetry={onRetryIndexing}
        />
      )}
    </section>
  );
}

function IndexingStatusPanel({
  status,
  onCancel,
  onRetry
}: {
  status: IndexingJobStatus;
  onCancel: () => void;
  onRetry: () => void;
}) {
  const isActive = status.state === "queued" || status.state === "running";
  const isCancelling = status.state === "cancelling";
  const isFailed = status.state === "failed";
  const canRetry = status.state === "failed" || status.state === "cancelled";
  const stateLabel = getStateLabel(status);

  return (
    <div className={`indexing-panel ${status.state}`} role="status" aria-live="polite">
      <div className="indexing-status-main">
        <span className="indexing-state-icon">
          {isActive && <Loader2 className="spin" size={17} />}
          {isCancelling && <Loader2 className="spin" size={17} />}
          {status.state === "completed" && <CheckCircle2 size={17} />}
          {(isFailed || status.state === "cancelled") && <XCircle size={17} />}
        </span>
        <div>
          <strong>{stateLabel}</strong>
          <span>{status.message}</span>
        </div>
      </div>

      <div className="indexing-counts" aria-label="Indexing progress counts">
        <span><b>{status.filesScanned}</b> scanned</span>
        <span><b>{status.filesIndexed}</b> indexed</span>
        <span><b>{status.filesSkipped}</b> skipped</span>
        <span><b>{status.symbolsIndexed}</b> symbols</span>
      </div>

      <div className="indexing-current-file" title={status.currentFile ?? undefined}>
        {status.currentFile ? status.currentFile : status.error ?? status.message ?? "Waiting for the next indexing update"}
      </div>

      {(isActive || isCancelling || canRetry) && (
        <div className="indexing-actions">
          {(isActive || isCancelling) && (
            <button type="button" onClick={onCancel} disabled={isCancelling}>
              <Square size={15} /> Cancel
            </button>
          )}
          {canRetry && (
            <button type="button" onClick={onRetry}>
              <RotateCcw size={15} /> Retry
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function getStateLabel(status: IndexingJobStatus) {
  if (status.state === "completed") {
    return "Index ready";
  }

  if (status.state === "failed") {
    return "Indexing failed";
  }

  if (status.state === "cancelled") {
    return "Indexing cancelled";
  }

  if (status.state === "cancelling") {
    return "Cancelling repository";
  }

  return `${status.stage} repository`;
}
