type ToggleProps = {
  label: string;
  checked?: boolean;
};

export function Toggle({ label, checked = false }: ToggleProps) {
  return (
    <label className="toggle-row">
      <span>{label}</span>
      <input type="checkbox" defaultChecked={checked} />
    </label>
  );
}
