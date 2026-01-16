"use client";

import type { ValueEditorProps } from "../models";

import { useRef } from "react";

import { NumberInput } from "@/components/bakaui";

type NumberValueEditorProps = ValueEditorProps<number | undefined> & {
  label?: string;
  placeholder?: string;
  size?: "sm" | "md" | "lg";
};
const NumberValueEditor = ({
  value,
  onValueChange,
  label,
  placeholder,
  size,
  ...props
}: NumberValueEditorProps) => {
  const valueRef = useRef(value);

  return (
    <NumberInput
      fullWidth={false}
      size={size}
      hideStepper
      labelPlacement="outside"
      classNames={{
        input: "text-left",
      }}
      className="w-[120px]"
      defaultValue={valueRef.current}
      label={label}
      placeholder={placeholder}
      onBlur={() => {
        onValueChange?.(valueRef.current, valueRef.current);
      }}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          onValueChange?.(valueRef.current, valueRef.current);
        }
      }}
      onValueChange={(v) => {
        valueRef.current = Number.isNaN(v) ? undefined : v;
      }}
    />
  );
};

NumberValueEditor.displayName = "NumberValueEditor";

export default NumberValueEditor;
