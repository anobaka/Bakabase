"use client";

import type { Duration } from "dayjs/plugin/duration";
import type { ValueRendererProps } from "../models";

import { useEffect, useState } from "react";

import { TimeInput } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
type TimeValueRendererProps = ValueRendererProps<Duration, Duration> & {
  format?: string;
  size?: "sm" | "md" | "lg";
};
const TimeValueRenderer = ({
  value: propsValue,
  format,
  variant,
  editor,
  size,
  isEditing,
  isReadonly: propsIsReadonly,
  ...props
}: TimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState<Duration | undefined>(propsValue);

  useEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  const f = format == undefined ? "HH:mm:ss" : format;

  // Don't allow starting edit mode if isEditing is explicitly set to false
  const startEditing = !isReadonly && editor && isEditing !== false
    ? () => {
        setEditing(true);
      }
    : undefined;

  // Direct editing mode: always show time picker without toggle
  if (isEditing && !isReadonly && editor) {
    return (
      <TimeInput
        isReadOnly={false}
        value={value}
        size={size}
        onChange={(x) => {
          setValue(x);
          editor.onValueChange?.(x, x);
        }}
      />
    );
  }

  if (editing) {
    return (
      <TimeInput
        isReadOnly={!editor}
        value={value}
        size={size}
        onBlur={() => {
          editor?.onValueChange?.(value, value);
          setEditing(false);
        }}
        onChange={(x) => setValue(x)}
      />
    );
  }

  if (value == undefined) {
    return <NotSet onClick={startEditing} size={size} />;
  } else {
    return <span onClick={startEditing} className={startEditing ? "cursor-pointer" : undefined}>{value?.format(f)}</span>;
  }
};

TimeValueRenderer.displayName = "TimeValueRenderer";

export default TimeValueRenderer;
