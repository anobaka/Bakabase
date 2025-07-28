"use client";

import type { ValueEditorProps } from "../models";

import { useRef } from "react";

import { Input } from "@/components/bakaui";

type NumberValueEditorProps = ValueEditorProps<number | undefined> & {
  label?: string;
  placeholder?: string;
};
const NumberValueEditor = ({
  value,
  onValueChange,
  label,
  placeholder,
  ...props
}: NumberValueEditorProps) => {
  const valueRef = useRef(value);

  return (
    <Input
      autoFocus
      defaultValue={valueRef.current?.toString()}
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
        const nStr = v?.match(/[\d,]+(\.\d+)?/)?.[0];
        const n = Number(nStr);

        valueRef.current = Number.isNaN(n) ? undefined : n;
        // console.log('NumberValueEditor', r, v, nStr, v?.match(/[\d,]+(\.\d+)?/));
      }}
    />
  );
};

NumberValueEditor.displayName = "NumberValueEditor";

export default NumberValueEditor;
