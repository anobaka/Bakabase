"use client";

import type { ValueRendererProps } from "../../models";

import { useState } from "react";
import { useUpdateEffect } from "react-use";

import { buildLogger } from "@/components/utils";

export type StringValueRendererBaseProps = ValueRendererProps<string> & {
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("useStringValueState");

export function useStringValueState(props: StringValueRendererBaseProps) {
  const {
    value: propsValue,
    variant,
    editor,
    size,
    isReadonly: propsIsReadonly,
    defaultEditing,
    isEditing,
  } = props;

  // Start in editing mode if defaultEditing is true and not readonly
  const [editing, setEditing] = useState(defaultEditing && !propsIsReadonly && !!editor);
  const [value, setValue] = useState(propsValue);

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  // Don't allow starting edit mode if isEditing is explicitly set to false
  const startEditing = !isReadonly && editor && isEditing !== false
    ? () => {
      log("Start editing");
      setEditing(true);
    }
    : undefined;

  const completeEditing = () => {
    editor?.onValueChange?.(value, value);
    setEditing(false);
  };

  return {
    value,
    setValue,
    editing,
    setEditing,
    isReadonly,
    variant,
    editor,
    size,
    isEditing,
    startEditing,
    completeEditing,
    log,
  };
}
