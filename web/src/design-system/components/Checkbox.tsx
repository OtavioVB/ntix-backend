import type { InputHTMLAttributes, ReactNode } from "react";

type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, "type"> & { children: ReactNode };

export function Checkbox({ children, ...input }: CheckboxProps) {
  return (
    <label className="n-checkbox">
      <input type="checkbox" {...input} />
      <span>{children}</span>
    </label>
  );
}
