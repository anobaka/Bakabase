"use client";

"use strict";
import type { CSSProperties } from "react";
import type { ValueRendererProps } from "../models";
import type { MultilevelData } from "../../models";

import { useEffect, useState, useMemo } from "react";
import { useTranslation } from "react-i18next";

import MultilevelValueEditor from "../../ValueEditor/Editors/MultilevelValueEditor";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, Button } from "@/components/bakaui";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import NoChoicesAvailable from "@/components/StandardValue/ValueRenderer/Renderers/components/NoChoicesAvailable";

import SelectableChip from "@/components/Chips/SelectableChip";
import { autoBackgroundColor, buildLogger } from "@/components/utils";
import { buildVisibleOptions, hasMoreOptions, getRemainingCount } from "../utils";
import { useFilterOptionsThreshold } from "@/hooks/useFilterOptionsThreshold";

type FlattenedOption = {
  path: string[];
  label: string;
  color?: string;
};

type MultilevelValueRendererProps = ValueRendererProps<string[][], string[]> & {
  multiple?: boolean;
  getDataSource?: () => Promise<MultilevelData<string>[]>;
  valueAttributes?: { color?: string }[][];
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("MultilevelValueRenderer");

const MultilevelValueRenderer = ({
  value,
  editor,
  variant,
  getDataSource,
  multiple,
  defaultEditing,
  valueAttributes,
  size,
  isReadonly: propsIsReadonly,
  ...props
}: MultilevelValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [dataSource, setDataSource] = useState<MultilevelData<string>[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [optionsThreshold] = useFilterOptionsThreshold();

  // Default isReadonly to true if no editor is provided
  const isReadonly = propsIsReadonly ?? !editor;

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

  // Note: defaultEditing is no longer used for MultilevelValueRenderer
  // because inline editing (showing first 30 options) is always enabled when !isReadonly

  const openFullEditor = editor
    ? () => {
        createPortal(MultilevelValueEditor<string>, {
          getDataSource: getDataSource,
          onValueChange: editor?.onValueChange,
          multiple,
          value: editor?.value,
        });
      }
    : undefined;

  // Flatten hierarchical data to get all leaf nodes with their paths
  const flattenedOptions = useMemo(() => {
    const result: FlattenedOption[] = [];

    const traverse = (nodes: MultilevelData<string>[], currentPath: string[], currentLabels: string[]) => {
      for (const node of nodes) {
        const newPath = [...currentPath, node.value];
        const newLabels = [...currentLabels, node.label || node.value];

        if (node.children && node.children.length > 0) {
          // Has children, recurse
          traverse(node.children, newPath, newLabels);
        } else {
          // Leaf node, add to results
          result.push({
            path: newPath,
            label: newLabels.join("/"),
            color: node.color,
          });
        }
      }
    };

    traverse(dataSource, [], []);
    return result;
  }, [dataSource]);

  // Selected values (leaf node values from dbValue)
  const selectedValues = editor?.value || [];

  // Check if a path's leaf value is selected
  const isPathSelected = (path: string[]) => {
    const leafValue = path[path.length - 1];
    return selectedValues.includes(leafValue);
  };

  const togglePath = (path: string[]) => {
    if (isReadonly || !editor?.onValueChange) return;

    const leafValue = path[path.length - 1];
    const isSelected = selectedValues.includes(leafValue);

    let newDbValues: string[];
    let newBizValues: string[][];

    if (isSelected) {
      // Remove - filter out the leaf value
      newDbValues = selectedValues.filter((v) => v !== leafValue);
      // Rebuild bizValues from remaining selections
      newBizValues = flattenedOptions
        .filter((opt) => newDbValues.includes(opt.path[opt.path.length - 1]))
        .map((opt) => opt.path);
    } else {
      if (multiple) {
        // Add to selection
        newDbValues = [...selectedValues, leafValue];
        newBizValues = flattenedOptions
          .filter((opt) => newDbValues.includes(opt.path[opt.path.length - 1]))
          .map((opt) => opt.path);
      } else {
        // Replace selection
        newDbValues = [leafValue];
        newBizValues = [path];
      }
    }

    editor.onValueChange(newDbValues, newBizValues);
  };

  // Build visible options using shared utility
  const visibleOptions = useMemo(
    () => buildVisibleOptions(flattenedOptions, (opt) => isPathSelected(opt.path), optionsThreshold),
    [flattenedOptions, selectedValues, optionsThreshold]
  );

  const hasMore = hasMoreOptions(flattenedOptions.length, optionsThreshold);
  const remainingCount = getRemainingCount(flattenedOptions.length, visibleOptions.length);

  // Editable mode: show inline options with toggle
  if (!isReadonly && dataSource.length > 0) {
    const isNotSet = selectedValues.length === 0;
    return (
      <div className="flex flex-wrap gap-1 items-center">
        {/* Fake NotSet indicator - visual only, helps user understand nothing is selected */}
        <SelectableChip
          itemKey="__not_set__"
          label={t("common.label.notSet")}
          isSelected={isNotSet}
          size={size}
        />
        {visibleOptions.map((opt) => (
          <SelectableChip
            key={opt.path.join("/")}
            itemKey={opt.path.join("/")}
            label={opt.label}
            isSelected={isPathSelected(opt.path)}
            color={opt.color}
            size={size}
            onClick={() => togglePath(opt.path)}
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

  // Readonly mode
  if (value == undefined || value.length == 0) {
    return <NotSet onClick={openFullEditor} size={size} />;
  }

  if (variant == "light") {
    const label: any[] = [];

    if (value) {
      for (let i = 0; i < value.length; i++) {
        const arr = value[i];

        if (arr) {
          if (i != 0) {
            label.push(";");
          }
          for (let j = 0; j < arr.length; j++) {
            if (j != 0) {
              label.push("/");
            }
            const style: CSSProperties = {};
            const color = valueAttributes?.[i]?.[j]?.color;

            if (color) {
              style.color = color;
              style.backgroundColor = autoBackgroundColor(color);
            }
            label.push(<span key={`${i}-${j}`} style={style}>{arr[j]}</span>);
          }
        }
      }
    }

    return (
      <span onClick={openFullEditor} className={openFullEditor ? "cursor-pointer" : undefined}>
        {label}
      </span>
    );
  } else {
    return (
      <div className={`flex flex-wrap gap-1 ${openFullEditor ? "cursor-pointer" : ""}`} onClick={openFullEditor}>
        {value?.map((v, i) => {
          const label: any[] = [];

          for (let j = 0; j < v.length; j++) {
            if (j != 0) {
              label.push("/");
            }
            const style: CSSProperties = {};
            const color = valueAttributes?.[i]?.[j]?.color;

            if (color) {
              style.color = color;
              style.backgroundColor = autoBackgroundColor(color);
            }
            label.push(<span key={`${i}-${j}`} style={style}>{v[j]}</span>);
          }

          return (
            <Chip key={i} radius={"sm"} size={size} variant={"flat"}>
              {label}
            </Chip>
          );
        })}
      </div>
    );
  }
};

MultilevelValueRenderer.displayName = "MultilevelValueRenderer";

export default MultilevelValueRenderer;
