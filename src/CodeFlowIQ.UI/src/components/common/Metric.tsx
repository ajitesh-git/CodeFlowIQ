type MetricProps = {
  label: string;
  value?: number | string;
};

import "./common.css";

export function Metric({ label, value }: MetricProps) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value ?? "--"}</strong>
    </div>
  );
}
