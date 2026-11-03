import type { InputHTMLAttributes, Ref } from "react";

type TextFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  id: string;
  label: string;
  /** Mensagem de erro. Sem o campo, nada é exibido e o campo fica válido. */
  error?: string;
  ref?: Ref<HTMLInputElement>;
};

export function TextField({ id, label, error, className, ...input }: TextFieldProps) {
  const errorId = `${id}-error`;
  return (
    <div className="n-field">
      <label className="n-field__label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className={["n-input", className].filter(Boolean).join(" ")}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...input}
      />
      <p className="n-field__error" id={errorId}>
        {error}
      </p>
    </div>
  );
}
