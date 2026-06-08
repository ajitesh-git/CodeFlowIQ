type CodeFlowLoadingProps = {
  label: string;
};

export function CodeFlowLoading({ label }: CodeFlowLoadingProps) {
  return (
    <div className="loading-layer" role="status" aria-live="polite" aria-label={label}>
      <div className="loading-panel">
        <div className="loading-brand">
          <div className="loading-mark">CF</div>
          <div>
            <strong>CodeFlowIQ</strong>
            <span>{label}</span>
          </div>
        </div>
        <div className="loading-flow" aria-hidden="true">
          <span />
          <i />
          <span />
          <i />
          <span />
        </div>
        <p>Mapping code signals, relationships, and runtime paths locally.</p>
      </div>
    </div>
  );
}
