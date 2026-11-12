import { createContext, useContext, useId, type ReactNode } from "react";

// DOM doubles for component tests that mock the rest of bakaui.
const Selection = createContext({
  value: "",
  select: (_: string) => {},
  disabled: false,
  name: "",
});
export function RadioGroup({
  value,
  onValueChange,
  isDisabled = false,
  label,
  children,
}: {
  value: string;
  onValueChange: (value: string) => void;
  isDisabled?: boolean;
  label: string;
  children: ReactNode;
}) {
  const name = useId();
  return (
    <Selection.Provider value={{ value, select: onValueChange, disabled: isDisabled, name }}>
      <div role="radiogroup" aria-label={label}>
        {children}
      </div>
    </Selection.Provider>
  );
}
export function Radio({
  children,
  value,
  isDisabled,
}: {
  children: ReactNode;
  value: string;
  isDisabled?: boolean;
}) {
  const group = useContext(Selection);
  return (
    <label>
      <input
        type="radio"
        name={group.name}
        value={value}
        checked={value === group.value}
        disabled={isDisabled || group.disabled}
        onChange={() => group.select(value)}
      />
      {children}
    </label>
  );
}
