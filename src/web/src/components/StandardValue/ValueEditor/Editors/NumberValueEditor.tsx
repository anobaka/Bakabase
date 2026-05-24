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
      hideStepper
      className="w-[120px]"
      classNames={{
        input: "text-left",
      }}
      defaultValue={valueRef.current}
      fullWidth={false}
      label={label}
      labelPlacement="outside"
      placeholder={placeholder}
      size={size}
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
