type EmptyStateProps = {
  label: string;
};

import "./common.css";

export function EmptyState({ label }: EmptyStateProps) {
  return <div className="empty-state">{label}</div>;
}
