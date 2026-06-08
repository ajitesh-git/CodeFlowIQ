type MetricProps = {
  label: string;
  value?: number | string;
};

export function Metric({ label, value }: MetricProps) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value ?? "--"}</strong>
    </div>
  );
}
