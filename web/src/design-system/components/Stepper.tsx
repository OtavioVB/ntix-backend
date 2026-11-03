type StepperProps = {
  label: string;
  value: number;
  max: number;
  onChange: (value: number) => void;
};

/** Quantidade com botões grandes: é o controle mais tocado de uma compra no celular. */
export function Stepper({ label, value, max, onChange }: StepperProps) {
  return (
    <div className="n-stepper" role="group" aria-label={label}>
      <button type="button" aria-label={`Remover um: ${label}`} disabled={value <= 0} onClick={() => onChange(value - 1)}>−</button>
      <output aria-live="polite">{value}</output>
      <button type="button" aria-label={`Adicionar um: ${label}`} disabled={value >= max} onClick={() => onChange(value + 1)}>+</button>
    </div>
  );
}
