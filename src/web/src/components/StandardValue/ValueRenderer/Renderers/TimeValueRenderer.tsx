"use client";

import type { Duration } from "dayjs/plugin/duration";
import type { ValueRendererProps } from "../models";

import { useRef, useState } from "react";

import { TimeInput } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
type TimeValueRendererProps = ValueRendererProps<Duration, Duration> & {
  format?: string;
  size?: "sm" | "md" | "lg";
};
const TimeValueRenderer = ({
  value,
  format,
  variant,
  editor,
  size,
  isEditing,
  isReadonly: propsIsReadonly,
  ...props
}: TimeValueRendererProps) => {
  const [editing, setEditing] = useState(false);
  const editingValueRef = useRef<Duration>();

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  const f = format == undefined ? "HH:mm:ss" : format;

  const startEditing = !isReadonly && editor
    ? () => {
        editingValueRef.current = value;
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
          editingValueRef.current = x;
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
        onBlur={(x) => {
          editor?.onValueChange?.(
            editingValueRef.current,
            editingValueRef.current,
          );
          setEditing(false);
        }}
        onChange={(x) => (editingValueRef.current = x)}
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
