import type { ChangeEvent } from "react";

type ToggleProps = {
  label: string;
  checked?: boolean;
  onChange?: (checked: boolean) => void;
};

import "./common.css";

export function Toggle({ label, checked = false, onChange }: ToggleProps) {
  const inputProps = onChange
    ? {
        checked,
        onChange: (event: ChangeEvent<HTMLInputElement>) => onChange(event.target.checked)
      }
    : {
        defaultChecked: checked
      };

  return (
    <label className="toggle-row">
      <span>{label}</span>
      <input type="checkbox" {...inputProps} />
    </label>
  );
}
