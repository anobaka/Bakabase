"use client";

import type { ValueRendererProps } from "../models";
import type { TagValue } from "../../models";

import { useEffect, useState, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import TagsValueEditor from "../../ValueEditor/Editors/TagsValueEditor";

import NotSet, { LightText } from "./components/LightText";
import NoChoicesAvailable from "./components/NoChoicesAvailable";

import SelectableChip from "@/components/StandardValue/ValueRenderer/Renderers/components/SelectableChip";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { buildVisibleOptions, hasMoreOptions, getRemainingCount } from "../utils";
import { useFilterOptionsThreshold } from "@/hooks/useFilterOptionsThreshold";

type TagData = TagValue & { value: string; color?: string };

type TagsValueRendererProps = ValueRendererProps<TagValue[], string[]> & {
  getDataSource?: () => Promise<TagData[]>;
  valueAttributes?: { color?: string }[];
  size?: "sm" | "md" | "lg";
};

const log = buildLogger("TagsValueRenderer");
const TagsValueRenderer = (props: TagsValueRendererProps) => {
  const { createPortal } = useBakabaseContext();
  const { t } = useTranslation();

  const { value, editor, variant, getDataSource, valueAttributes, size, isReadonly: propsIsReadonly, isEditing: controlledIsEditing, defaultEditing = false } = props;
  const [dataSource, setDataSource] = useState<TagData[]>([]);
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

  const getTagLabel = (tag: TagValue) => {
    if (tag.group != undefined && tag.group.length > 0) {
      return `${tag.group}:${tag.name}`;
    }
    return tag.name;
  };

  const simpleLabels = value?.map(getTagLabel);

  const openFullEditor = editor
    ? () => {
        createPortal(TagsValueEditor, {
          value: editor?.value,
          getDataSource: async () => {
            return (await getDataSource?.()) || [];
          },
          onValueChange: (dbValue, bizValue) => {
            editor?.onValueChange?.(dbValue, bizValue);
          },
        });
      }
    : undefined;

  const selectedValues = editor?.value || [];

  const toggleValue = (tagValue: string) => {
    if (isReadonly || !editor?.onValueChange) return;

    const tag = dataSource.find((t) => t.value === tagValue);
    if (!tag) return;

    const newDbValues = selectedValues.includes(tagValue)
      ? selectedValues.filter((v) => v !== tagValue)
      : [...selectedValues, tagValue];

    // Convert to bizValue format
    const newBizValues: TagValue[] = newDbValues.map((v) => {
      const t = dataSource.find((d) => d.value === v);
      return t ? { name: t.name, group: t.group } : { name: v };
    });

    editor.onValueChange(newDbValues, newBizValues);
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
            label={getTagLabel(item)}
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

  // Determine if editing is allowed (for NotSet to show "click to set" vs "not set")
  const canEdit = !isReadonly && !!editor;

  // Readonly mode
  if (!value || value.length == 0) {
    return (
      <div ref={containerRef} onClick={handleClick} className={canEdit ? "cursor-pointer" : undefined}>
        <NotSet size={size} onClick={canEdit ? handleClick : undefined} />
      </div>
    );
  }

  if (variant == "light") {
    return (
      <LightText onClick={handleClick} size={size}>
        {simpleLabels?.map((l, i) => (
          <span key={i}>
            {i != 0 && ", "}
            <LightText color={valueAttributes?.[i]?.color} size={size}>{l}</LightText>
          </span>
        ))}
      </LightText>
    );
  } else {
    return (
      <div ref={containerRef} onClick={handleClick} className="flex flex-wrap gap-1 cursor-pointer">
        {simpleLabels?.map((l, i) => (
          <SelectableChip
            key={i}
            itemKey={`display-${i}`}
            label={l}
            isSelected
            color={valueAttributes?.[i]?.color}
            size={size}
          />
        ))}
      </div>
    );
  }
};

TagsValueRenderer.displayName = "TagsValueRenderer";

export default TagsValueRenderer;
