type SegmentedProps<T extends string> = {
  name: string;
  legend: string;
  options: readonly { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
};

/** Escolha exclusiva entre poucas opções. Rádios nativos: teclado e leitor de tela de graça. */
export function Segmented<T extends string>({ name, legend, options, value, onChange }: SegmentedProps<T>) {
  return (
    <fieldset className="n-fieldset">
      <legend className="n-field__label">{legend}</legend>
      <div className="n-segmented">
        {options.map((option) => (
          <label className="n-segmented__option" key={option.value}>
            <input
              type="radio"
              name={name}
              value={option.value}
              checked={option.value === value}
              onChange={() => onChange(option.value)}
            />
            <span>{option.label}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
