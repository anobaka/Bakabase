"use client";

"use strict";
import type { ValueRendererProps } from "../models";

import { type CSSProperties, useEffect, useState, useMemo } from "react";
import { useTranslation } from "react-i18next";

import ChoiceValueEditor from "../../ValueEditor/Editors/ChoiceValueEditor";

import NotSet from "./components/NotSet";
import NoChoicesAvailable from "./components/NoChoicesAvailable";

import SelectableChip from "@/components/Chips/SelectableChip";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, Button } from "@/components/bakaui";
import { autoBackgroundColor, buildLogger } from "@/components/utils";
import { buildVisibleOptions, hasMoreOptions, getRemainingCount } from "../utils";

type Data = { label: string; value: string; color?: string };

type ChoiceValueRendererProps = ValueRendererProps<string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<Data[]>;
  valueAttributes?: { color?: string }[];
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("ChoiceValueRenderer");
const ChoiceValueRenderer = (props: ChoiceValueRendererProps) => {
  const { value, editor, variant, getDataSource, multiple, valueAttributes, size, isReadonly: propsIsReadonly } =
    props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [dataSource, setDataSource] = useState<Data[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

  log(props);

  // Load data source on mount if not readonly
  useEffect(() => {
    if (!isReadonly && getDataSource) {
      setIsLoading(true);
      getDataSource().then((data) => {
        setDataSource(data);
        setIsLoading(false);
      });
    }
  }, [isReadonly, getDataSource]);

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
    () => buildVisibleOptions(dataSource, (item) => selectedValues.includes(item.value)),
    [dataSource, selectedValues]
  );

  const hasMore = hasMoreOptions(dataSource.length);
  const remainingCount = getRemainingCount(dataSource.length, visibleOptions.length);

  // Editable mode: show inline options with toggle
  if (!isReadonly && dataSource.length > 0) {
    return (
      <div className="flex flex-wrap gap-1 items-center">
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

  // Loading state for editable mode
  if (!isReadonly && isLoading) {
    return <span className="text-default-400">{t("common.state.loading")}</span>;
  }

  // No choices available in editable mode
  if (!isReadonly && dataSource.length === 0) {
    return <NoChoicesAvailable />;
  }

  // Readonly mode: display current values
  const validValues = value?.filter((v) => v != undefined) || [];

  if (validValues.length == 0) {
    return <NotSet onClick={openFullEditor} />;
  }

  if (variant == "light") {
    return (
      <span onClick={openFullEditor} className={openFullEditor ? "cursor-pointer" : undefined}>
        {value?.map((v, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <span key={i}>
              {i != 0 && ","}
              <span style={styles}>{v}</span>
            </span>
          );
        })}
      </span>
    );
  } else {
    return (
      <div className={`flex flex-wrap gap-1 ${openFullEditor ? "cursor-pointer" : ""}`} onClick={openFullEditor}>
        {value?.map((v, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <Chip
              key={i}
              className={"h-auto whitespace-break-spaces py-1"}
              radius={"sm"}
              size={size}
              style={styles}
            >
              {v}
            </Chip>
          );
        })}
      </div>
    );
  }
};

ChoiceValueRenderer.displayName = "ChoiceValueRenderer";

export default ChoiceValueRenderer;
