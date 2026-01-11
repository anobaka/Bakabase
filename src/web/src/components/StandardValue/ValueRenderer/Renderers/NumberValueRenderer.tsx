"use client";

import type { ValueRendererProps } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import NumberValueEditor from "../../ValueEditor/Editors/NumberValueEditor";

import { Progress } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import { buildLogger } from "@/components/utils";
type NumberValueRendererProps = ValueRendererProps<number, number> & {
  precision?: number;
  as?: "number" | "progress";
  suffix?: string;
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("NumberValueRenderer");
const NumberValueRenderer = (props: NumberValueRendererProps) => {
  const { value, precision, editor, variant, suffix, as, size, isReadonly: propsIsReadonly, isEditing, ...otherProps } =
    props;
  const { t } = useTranslation();

  log(props);

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  const [editing, setEditing] = useState(false);

  const startEditing = !isReadonly && editor ? () => setEditing(true) : undefined;

  // Direct editing mode: always show input without toggle
  if (isEditing && !isReadonly && editor) {
    return (
      <NumberValueEditor
        value={value}
        size={size}
        placeholder={t("Number")}
        onValueChange={(dbValue, bizValue) => {
          editor.onValueChange?.(dbValue, bizValue);
        }}
      />
    );
  }

  if (editing) {
    return (
      <NumberValueEditor
        value={value}
        size={size}
        placeholder={t("Number")}
        onValueChange={(dbValue, bizValue) => {
          editor?.onValueChange?.(dbValue, bizValue);
          setEditing(false);
        }}
      />
    );
  }

  if (value == undefined) {
    return <NotSet onClick={startEditing} />;
  }

  if (variant == "light" || as == "number") {
    return (
      <span onClick={startEditing} className={startEditing ? "cursor-pointer" : undefined}>
        {value}
        {suffix}
      </span>
    );
  } else {
    return <Progress size={size} value={value} onClick={startEditing} />;
  }
};

NumberValueRenderer.displayName = "NumberValueRenderer";

export default NumberValueRenderer;
