"use client";

"use strict";
import type { ValueRendererProps } from "../models";

import { useEffect, useState, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import ChoiceValueEditor from "../../ValueEditor/Editors/ChoiceValueEditor";

import NotSet, { LightText } from "./components/LightText";
import NoChoicesAvailable from "./components/NoChoicesAvailable";

import SelectableChip from "@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { buildVisibleOptions, hasMoreOptions, getRemainingCount } from "../utils";
import { useFilterOptionsThreshold } from "@/hooks/useFilterOptionsThreshold";

type Data = { label: string; value: string; color?: string };

type ChoiceValueRendererProps = ValueRendererProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<Data[]>;
  valueAttributes?: { color?: string }[];
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("ChoiceValueRenderer");
const ChoiceValueRenderer = (props: ChoiceValueRendererProps) => {
  const { value, editor, variant, getDataSource, multiple, valueAttributes, size, isReadonly: propsIsReadonly, isEditing: controlledIsEditing, defaultEditing = false } =
    props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [dataSource, setDataSource] = useState<Data[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [optionsThreshold] = useFilterOptionsThreshold();

  // Internal editing state for uncontrolled mode
  const [internalIsEditing, setInternalIsEditing] = useState(defaultEditing);
  const containerRef = useRef<HTMLDivElement>(null);

  // Use controlled value if provided, otherwise use internal state
  const isEditing = controlledIsEditing ?? internalIsEditing;

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

  log(props);

  // Click outside to close editing mode (only for uncontrolled mode)
  useEffect(() => {
    if (controlledIsEditing !== undefined || !isEditing) return;

    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setInternalIsEditing(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [controlledIsEditing, isEditing]);

  // Load data source when entering editing mode
  useEffect(() => {
    if (isEditing && getDataSource && dataSource.length === 0) {
      setIsLoading(true);
      getDataSource().then((data) => {
        setDataSource(data);
        setIsLoading(false);
      });
    }
  }, [isEditing, getDataSource]);

  const handleClick = () => {
    if (controlledIsEditing === undefined && !isEditing && !isReadonly && editor) {
      setInternalIsEditing(true);
    }
  };

  const openFullEditor = editor
    ? () => {
        createPortal(ChoiceValueEditor, {
          value: editor?.value,
          getDataSource: getDataSource ?? (async () => []),
          onValueChange: editor?.onValueChange,
          multiple: multiple ?? false,
        });
      }
    : undefined;

  // Note: defaultEditing is no longer used for ChoiceValueRenderer
  // because inline editing (showing first 30 options) is always enabled when !isReadonly

  const selectedValues = editor?.value || value || [];

  const toggleValue = (itemValue: string) => {
    if (isReadonly || !editor?.onValueChange) return;

    if (multiple) {
      // Multiple selection: toggle the item
      const newDbValues = selectedValues.includes(itemValue)
        ? selectedValues.filter((v) => v !== itemValue)
        : [...selectedValues, itemValue];
      // Convert dbValues (UUIDs) to bizValues (labels)
      const newBizValues = newDbValues
        .map((v) => dataSource.find((d) => d.value === v)?.label)
        .filter((l): l is string => l !== undefined);
      editor.onValueChange(newDbValues, newBizValues);
    } else {
      // Single selection: select or deselect
      const newDbValues = selectedValues.includes(itemValue) ? [] : [itemValue];
      // Convert dbValues (UUIDs) to bizValues (labels)
      const newBizValues = newDbValues
        .map((v) => dataSource.find((d) => d.value === v)?.label)
        .filter((l): l is string => l !== undefined);
      editor.onValueChange(newDbValues, newBizValues);
    }
  };

  // Build visible options using shared utility
  const visibleOptions = useMemo(
    () => buildVisibleOptions(dataSource, (item) => selectedValues.includes(item.value), optionsThreshold),
    [dataSource, selectedValues, optionsThreshold]
  );

  const hasMore = hasMoreOptions(dataSource.length, optionsThreshold);
  const remainingCount = getRemainingCount(dataSource.length, visibleOptions.length);

  // Editing mode: show inline options with toggle (only when isEditing is explicitly true)
  if (isEditing === true && dataSource.length > 0) {
    const isNotSet = selectedValues.length === 0;
    return (
      <div ref={containerRef} className="flex flex-wrap gap-1 items-center">
        {/* Fake NotSet indicator - visual only, helps user understand nothing is selected */}
        <SelectableChip
          itemKey="__not_set__"
          label={t("common.label.notSet")}
          isSelected={isNotSet}
          size={size}
        />
        {visibleOptions.map((item) => (
          <SelectableChip
            key={item.value}
            itemKey={item.value}
            label={item.label}
            isSelected={selectedValues.includes(item.value)}
            color={item.color}
            size={size}
            onClick={() => toggleValue(item.value)}
          />
        ))}
        {hasMore && (
          <Button
            size="sm"
            variant="light"
            color="primary"
            onClick={openFullEditor}
          >
            {t("common.action.more")} (+{remainingCount})
          </Button>
        )}
      </div>
    );
  }

  // Loading state for editing mode
  if (isEditing === true && isLoading) {
    return <span className="text-default-400">{t("common.state.loading")}</span>;
  }

  // No choices available in editing mode
  if (isEditing === true && dataSource.length === 0) {
    return <NoChoicesAvailable />;
  }

  // Readonly mode: display current values
  const validValues = value?.filter((v) => v != undefined) || [];

  // Determine if editing is allowed (for NotSet to show "click to set" vs "not set")
  const canEdit = !isReadonly && !!editor;

  if (validValues.length == 0) {
    return (
      <div ref={containerRef} onClick={handleClick} className={canEdit ? "cursor-pointer" : undefined}>
        <NotSet size={size} onClick={canEdit ? handleClick : undefined} />
      </div>
    );
  }

  if (variant == "light") {
    return (
      <LightText onClick={handleClick} size={size}>
        {value?.map((v, i) => (
          <span key={i}>
            {i != 0 && ", "}
            <LightText color={valueAttributes?.[i]?.color} size={size}>{v}</LightText>
          </span>
        ))}
      </LightText>
    );
  } else {
    return (
      <div ref={containerRef} onClick={handleClick} className="flex flex-wrap gap-1 cursor-pointer">
        {value?.map((v, i) => (
          <SelectableChip
            key={i}
            itemKey={`display-${i}`}
            label={v}
            isSelected
            color={valueAttributes?.[i]?.color}
            size={size}
          />
        ))}
      </div>
    );
  }
};

ChoiceValueRenderer.displayName = "ChoiceValueRenderer";

export default ChoiceValueRenderer;
