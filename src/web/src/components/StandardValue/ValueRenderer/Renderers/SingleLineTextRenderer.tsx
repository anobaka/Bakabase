"use client";

import { useTranslation } from "react-i18next";

import NotSet, { LightText } from "./components/LightText";
import {
  useStringValueState,
  type StringValueRendererBaseProps,
} from "./hooks/useStringValueState";

import { Input } from "@/components/bakaui";

const SingleLineTextRenderer = (props: StringValueRendererBaseProps) => {
  const { t } = useTranslation();
  const {
    value,
    setValue,
    editing,
    isReadonly,
    variant,
    editor,
    size,
    isEditing,
    startEditing,
    completeEditing,
  } = useStringValueState(props);

  // Direct editing mode: always show input without toggle
  if (isEditing && !isReadonly && editor) {
    const handleChange = (newValue: string) => {
      setValue(newValue);
      editor.onValueChange?.(newValue, newValue);
    };

    return (
      <Input
        placeholder={t("common.placeholder.typeHere")}
        size={size}
        value={value ?? ""}
        onValueChange={handleChange}
      />
    );
  }

  // Light variant: show plain text with size-matched text class, or NotSet if empty
  if (variant == "light" && !editing) {
    // Show NotSet indicator when value is empty
    if (value == null || value === "") {
      return <NotSet size={size} onClick={startEditing} />;
    }

    return (
      <LightText size={size} onClick={startEditing}>
        {value}
      </LightText>
    );
  }

  // Editing mode: show editable input
  if (editing) {
    return (
      <Input
        placeholder={t("common.placeholder.typeHere")}
        size={size}
        value={value}
        onBlur={completeEditing}
        onValueChange={setValue}
      />
    );
  }

  // Default variant non-editing: show text or NotSet indicator
  // If value is empty, show NotSet; otherwise show text with click-to-edit
  if (value == null || value === "") {
    return <NotSet size={size} onClick={startEditing} />;
  }

  return (
    <LightText size={size} onClick={startEditing}>
      {value}
    </LightText>
  );
};

SingleLineTextRenderer.displayName = "SingleLineTextRenderer";

export default SingleLineTextRenderer;
