"use client";

import type { ValueRendererProps } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Checkbox, Switch } from "@/components/bakaui";
import NotSet, {
  LightText,
} from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import SelectableChip from "@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip";

type BooleanValueRendererProps = Omit<ValueRendererProps<boolean>, "variant"> & {
  variant: ValueRendererProps<boolean>["variant"] | "switch";
  size?: "sm" | "md" | "lg";
};

const BooleanValueRenderer = ({
  value,
  variant,
  editor,
  size,
  isReadonly: propsIsReadonly,
  isEditing,
  defaultEditing = false,
}: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  const v = variant ?? "default";
  const canEdit = !isReadonly && editor;

  // Track internal editing state for light and default variants
  const [internalEditing, setInternalEditing] = useState(
    defaultEditing && !isReadonly && !!editor && (v === "light" || v === "default"),
  );

  const startEditing = canEdit && isEditing !== false ? () => setInternalEditing(true) : undefined;

  // Shared chip click handler for editing mode
  const handleChipClick = (clickedValue: boolean) => {
    // Toggle: if already selected, clear to undefined; otherwise set the value
    const newValue = value === clickedValue ? undefined : clickedValue;

    editor?.onValueChange?.(newValue, newValue);
    if (!isEditing) {
      setInternalEditing(false);
    }
  };

  // Editing mode for light and default variants
  if ((isEditing || internalEditing) && canEdit && v !== "switch") {
    return (
      <div className="inline-flex gap-1 flex-wrap">
        <SelectableChip
          isSelected={value === true}
          itemKey="__yes__"
          label={t("common.label.yes")}
          size={size}
          onClick={() => handleChipClick(true)}
        />
        <SelectableChip
          isSelected={value === false}
          itemKey="__no__"
          label={t("common.label.no")}
          size={size}
          onClick={() => handleChipClick(false)}
        />
      </div>
    );
  }

  // Light variant display mode
  if (v === "light") {
    if (value === true) {
      return (
        <LightText size={size} onClick={startEditing}>
          {t("common.label.yes")}
        </LightText>
      );
    } else if (value === false) {
      return (
        <LightText size={size} onClick={startEditing}>
          {t("common.label.no")}
        </LightText>
      );
    } else {
      return <NotSet size={size} onClick={startEditing} />;
    }
  }

  // Switch variant: always directly interactive
  if (v === "switch") {
    return (
      <Switch
        isDisabled={!canEdit}
        isSelected={value}
        size={size}
        onValueChange={canEdit ? (v) => editor?.onValueChange?.(v, v) : undefined}
      />
    );
  }

  // Default variant display mode: show Checkbox (clickable to enter editing)
  return (
    <div
      className={startEditing ? "cursor-pointer" : undefined}
      role={startEditing ? "button" : undefined}
      tabIndex={startEditing ? 0 : undefined}
      onClick={startEditing}
      onKeyDown={
        startEditing
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                startEditing();
              }
            }
          : undefined
      }
    >
      <Checkbox isReadOnly className="pointer-events-none" isSelected={value} size={size} />
    </div>
  );
};

BooleanValueRenderer.displayName = "BooleanValueRenderer";

export default BooleanValueRenderer;
