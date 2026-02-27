"use client";

"use strict";
import type { ValueRendererProps } from "../models";
import type { MultilevelData } from "../../models";

import { useEffect, useState, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import MultilevelValueEditor from "../../ValueEditor/Editors/MultilevelValueEditor";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button } from "@/components/bakaui";
import NotSet, { LightText } from "@/components/StandardValue/ValueRenderer/Renderers/components/LightText";
import NoChoicesAvailable from "@/components/StandardValue/ValueRenderer/Renderers/components/NoChoicesAvailable";

import SelectableChip from "@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip";
import { buildLogger } from "@/components/utils";
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
  defaultEditing = false,
  valueAttributes,
  size,
  isReadonly: propsIsReadonly,
  isEditing: controlledIsEditing,
}: MultilevelValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [dataSource, setDataSource] = useState<MultilevelData<string>[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [optionsThreshold] = useFilterOptionsThreshold();

  // Internal editing state for uncontrolled mode
  const [internalIsEditing, setInternalIsEditing] = useState(defaultEditing);
  const containerRef = useRef<HTMLDivElement>(null);

  // Use controlled value if provided, otherwise use internal state
  const isEditing = controlledIsEditing ?? internalIsEditing;

  // Default isReadonly to false
  const isReadonly = propsIsReadonly ?? false;

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

  // Editing mode: show inline options with toggle (only when isEditing is explicitly true)
  if (isEditing === true && dataSource.length > 0) {
    return (
      <div ref={containerRef} className="flex flex-wrap gap-1 items-center">
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

  // Loading state for editing mode
  if (isEditing === true && isLoading) {
    return <span className="text-default-400">{t("common.state.loading")}</span>;
  }

  // No choices available in editing mode
  if (isEditing === true && dataSource.length === 0) {
    return <NoChoicesAvailable />;
  }

  // Determine if editing is allowed (for NotSet to show "click to set" vs "not set")
  const canEdit = !isReadonly && !!editor;

  // Readonly mode
  if (value == undefined || value.length == 0) {
    return (
      <div ref={containerRef} onClick={handleClick} className={canEdit ? "cursor-pointer" : undefined}>
        <NotSet size={size} onClick={canEdit ? handleClick : undefined} />
      </div>
    );
  }

  // Helper to get first available color from value attributes
  const getFirstColor = (index: number) => {
    const attrs = valueAttributes?.[index];
    if (attrs) {
      for (const attr of attrs) {
        if (attr?.color) return attr.color;
      }
    }
    return undefined;
  };

  if (variant == "light") {
    return (
      <LightText onClick={handleClick} size={size}>
        {value?.map((v, i) => (
          <span key={i}>
            {i != 0 && ", "}
            <LightText color={getFirstColor(i)} size={size}>{v.join("/")}</LightText>
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
            label={v.join("/")}
            isSelected
            color={getFirstColor(i)}
            size={size}
          />
        ))}
      </div>
    );
  }
};

MultilevelValueRenderer.displayName = "MultilevelValueRenderer";

export default MultilevelValueRenderer;
