import { FormEvent } from "react";
import { Play, RefreshCw, Search } from "lucide-react";

type WorkspaceBarProps = {
  workspacePath: string;
  disabled: boolean;
  onWorkspacePathChange: (value: string) => void;
  onInitialize: () => void;
  onSync: () => void;
  onLoad: () => void;
};

export function WorkspaceBar({
  workspacePath,
  disabled,
  onWorkspacePathChange,
  onInitialize,
  onSync,
  onLoad
}: WorkspaceBarProps) {
  function submitWorkspace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onLoad();
  }

  return (
    <section className="workspace-bar">
      <form onSubmit={submitWorkspace}>
        <label>
          Workspace
          <input
            placeholder="C:\\Users\\ajite\\Downloads\\Compressed\\deloitte-omnia-financial-be"
            value={workspacePath}
            onChange={(event) => onWorkspacePathChange(event.target.value)}
          />
        </label>
        <button type="button" onClick={onInitialize} disabled={disabled}>
          <Play size={17} /> Init
        </button>
        <button type="button" onClick={onSync} disabled={disabled}>
          <RefreshCw size={17} /> Sync
        </button>
        <button type="submit" disabled={disabled}>
          <Search size={17} /> Load
        </button>
      </form>
    </section>
  );
}
