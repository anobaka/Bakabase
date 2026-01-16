"use client";

import type { ValueRendererProps } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import { NumberInput, Progress } from "@/components/bakaui";
import NotSet, {
  LightText,
} from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import { buildLogger } from "@/components/utils";

type NumberValueRendererProps = ValueRendererProps<number, number> & {
  precision?: number;
  as?: "number" | "progress";
  suffix?: string;
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("NumberValueRenderer");
const NumberValueRenderer = (props: NumberValueRendererProps) => {
  const {
    value: propsValue,
    precision,
    editor,
    variant,
    suffix,
    as,
    size,
    isReadonly: propsIsReadonly,
    isEditing,
    defaultEditing,
  } = props;
  const { t } = useTranslation();

  log(props);

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  // Start in editing mode if defaultEditing is true and not readonly
  const [editing, setEditing] = useState(defaultEditing && !isReadonly && !!editor);
  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  // Don't allow starting edit mode if isEditing is explicitly set to false
  const startEditing =
    !isReadonly && editor && isEditing !== false ? () => setEditing(true) : undefined;

  const completeEditing = () => {
    editor?.onValueChange?.(value, value);
    setEditing(false);
  };

  // Direct editing mode: always show input without toggle
  if (isEditing && !isReadonly && editor) {
    const handleChange = (newValue: number) => {
      const v = Number.isNaN(newValue) ? undefined : newValue;

      setValue(v);
      editor.onValueChange?.(v, v);
    };

    return (
      <NumberInput
        hideStepper
        className={"w-[120px]"}
        classNames={{ input: "text-left" }}
        endContent={as === "progress" ? "%" : undefined}
        placeholder={t("common.placeholder.typeHere")}
        size={size}
        value={value}
        onValueChange={handleChange}
      />
    );
  }

  // Light variant: show plain text with size-matched text class, or NotSet if empty
  if (variant == "light" && !editing) {
    if (value == null) {
      return <NotSet size={size} onClick={startEditing} />;
    }

    return (
      <LightText size={size} onClick={startEditing}>
        {value}
        {suffix}
      </LightText>
    );
  }

  // Progress bar mode (for percentage)
  if (as == "progress" && !editing) {
    if (value == null) {
      return <NotSet size={size} onClick={startEditing} />;
    }

    return (
      <Progress
        showValueLabel
        className={`w-[120px] ${startEditing ? "cursor-pointer" : ''}`}
        classNames={{
          base: "relative",
          labelWrapper: "absolute inset-0 flex items-center justify-center z-10",
          value: "text-foreground text-xs",
        }}
        size={size}
        value={value}
        onClick={startEditing}
      />
    );
  }

  // Editing mode: show editable input
  if (editing) {
    return (
      <NumberInput
        hideStepper
        className={"w-[120px]"}
        classNames={{ input: "text-left" }}
        endContent={as === "progress" ? "%" : undefined}
        placeholder={t("common.placeholder.typeHere")}
        size={size}
        value={value}
        onBlur={completeEditing}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            completeEditing();
          }
        }}
        onValueChange={(v) => setValue(Number.isNaN(v) ? undefined : v)}
      />
    );
  }

  // Default variant non-editing: show text or NotSet indicator
  if (value == null) {
    return <NotSet size={size} onClick={startEditing} />;
  }

  return (
    <LightText size={size} onClick={startEditing}>
      {value}
      {suffix}
    </LightText>
  );
};

NumberValueRenderer.displayName = "NumberValueRenderer";

export default NumberValueRenderer;
