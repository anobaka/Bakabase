"use client";

import type { CSSProperties } from "react";
import type { ValueRendererProps } from "../models";
import type { MultilevelData, TagValue } from "../../models";

import { useEffect, useState, useMemo } from "react";
import { useTranslation } from "react-i18next";

import MultilevelValueEditor from "../../ValueEditor/Editors/MultilevelValueEditor";

import NotSet from "./components/NotSet";
import NoChoicesAvailable from "./components/NoChoicesAvailable";

import SelectableChip from "@/components/Chips/SelectableChip";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, Button } from "@/components/bakaui";
import { autoBackgroundColor, buildLogger, uuidv4 } from "@/components/utils";
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

  const { value, editor, variant, getDataSource, valueAttributes, size, isReadonly: propsIsReadonly } = props;
  const [dataSource, setDataSource] = useState<TagData[]>([]);
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

  const getTagLabel = (tag: TagValue) => {
    if (tag.group != undefined && tag.group.length > 0) {
      return `${tag.group}:${tag.name}`;
    }
    return tag.name;
  };

  const simpleLabels = value?.map(getTagLabel);

  const openFullEditor = editor
    ? () => {
        createPortal(MultilevelValueEditor<string>, {
          value: editor?.value,
          multiple: true,
          getDataSource: async () => {
            const ds = (await getDataSource?.()) || [];
            const data: MultilevelData<string>[] = [];

            for (const d of ds) {
              if (d.group == undefined || d.group.length == 0) {
                data.push({
                  value: d.value,
                  label: d.name,
                });
              } else {
                let group = data.find((x) => x.label == d.group);

                if (!group) {
                  group = {
                    value: uuidv4(),
                    label: d.group,
                    children: [],
                  };
                  data.push(group);
                }
                group.children!.push({
                  value: d.value,
                  label: d.name,
                });
              }
            }

            return data;
          },
          onValueChange: (dbValue, bizValue) => {
            if (dbValue) {
              const bv: TagValue[] = [];

              for (const b of bizValue!) {
                if (b.length == 1) {
                  bv.push({
                    name: b[0]!,
                  });
                } else {
                  bv.push({
                    name: b[1]!,
                    group: b[0],
                  });
                }
              }

              editor?.onValueChange?.(dbValue, bv);
            }
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

  // Editable mode: show inline options with toggle
  if (!isReadonly && dataSource.length > 0) {
    return (
      <div className="flex flex-wrap gap-1 items-center">
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

  // Loading state for editable mode
  if (!isReadonly && isLoading) {
    return <span className="text-default-400">{t("common.state.loading")}</span>;
  }

  // No choices available in editable mode
  if (!isReadonly && dataSource.length === 0) {
    return <NoChoicesAvailable />;
  }

  // Readonly mode
  if (!value || value.length == 0) {
    return <NotSet onClick={openFullEditor} size={size} />;
  }

  if (variant == "light") {
    return (
      <span onClick={openFullEditor} className={openFullEditor ? "cursor-pointer" : undefined}>
        {simpleLabels?.map((l, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <span key={i}>
              {i != 0 && ","}
              <span style={styles}>{l}</span>
            </span>
          );
        })}
      </span>
    );
  } else {
    return (
      <div className={`flex flex-wrap gap-1 ${openFullEditor ? "cursor-pointer" : ""}`} onClick={openFullEditor}>
        {simpleLabels?.map((l, i) => {
          const styles: CSSProperties = {};

          styles.color = valueAttributes?.[i]?.color;
          if (styles.color) {
            styles.backgroundColor = autoBackgroundColor(styles.color);
          }

          return (
            <Chip key={i} radius={"sm"} size={size} style={styles}>
              {l}
            </Chip>
          );
        })}
      </div>
    );
  }
};

TagsValueRenderer.displayName = "TagsValueRenderer";

export default TagsValueRenderer;
