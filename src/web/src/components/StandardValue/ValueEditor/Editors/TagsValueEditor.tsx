"use client";

import type { CSSProperties } from "react";
import type { ValueEditorProps } from "../models";
import type { TagValue } from "@/components/StandardValue/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import { useEffect, useMemo, useState } from "react";
import { SearchOutlined } from "@ant-design/icons";

import { Button, Input, Modal } from "@/components/bakaui";
import { autoBackgroundColor, buildLogger } from "@/components/utils";

type TagData = TagValue & { value: string; color?: string };

type TagsValueEditorProps = ValueEditorProps<string[], TagValue[]> &
  DestroyableProps & {
    getDataSource: () => Promise<TagData[] | undefined>;
  };

const log = buildLogger("TagsValueEditor");

const TagsValueEditor = (props: TagsValueEditorProps) => {
  const { t } = useTranslation();
  const { getDataSource, value: propsValue, onValueChange, onCancel } = props;

  const [dataSource, setDataSource] = useState<TagData[]>([]);
  const [keyword, setKeyword] = useState("");
  const [value, setValue] = useState<string[]>(propsValue ?? []);

  log(props);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    const data = (await getDataSource()) ?? [];
    if (data.length > 0) {
      setDataSource(data);
    }
  };

  // Group tags by their group property
  const groupedTags = useMemo(() => {
    const groups: Record<string, TagData[]> = {};
    const ungrouped: TagData[] = [];

    const filteredData = keyword.length === 0
      ? dataSource
      : dataSource.filter((d) => {
          const searchText = d.group ? `${d.group}:${d.name}` : d.name;
          return searchText.toLowerCase().includes(keyword.toLowerCase());
        });

    for (const tag of filteredData) {
      if (tag.group && tag.group.length > 0) {
        if (!groups[tag.group]) {
          groups[tag.group] = [];
        }
        groups[tag.group].push(tag);
      } else {
        ungrouped.push(tag);
      }
    }

    return { groups, ungrouped };
  }, [dataSource, keyword]);

  const toggleTag = (tagValue: string) => {
    if (value.includes(tagValue)) {
      setValue(value.filter((v) => v !== tagValue));
    } else {
      setValue([...value, tagValue]);
    }
  };

  const renderTagButton = (tag: TagData) => {
    const isSelected = value.includes(tag.value);
    const style: CSSProperties = {};

    if (tag.color) {
      style.color = tag.color;
      if (!isSelected) {
        style.backgroundColor = autoBackgroundColor(tag.color);
      }
    }

    return (
      <Button
        key={tag.value}
        color={isSelected ? "primary" : "default"}
        size={"sm"}
        style={style}
        onPress={() => toggleTag(tag.value)}
      >
        {tag.name}
      </Button>
    );
  };

  const groupNames = Object.keys(groupedTags.groups).sort();

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 20 ? "xl" : "lg"}
      title={t<string>("Select data")}
      onClose={onCancel}
      onOk={async () => {
        const bizValue: TagValue[] = value
          .map((v) => {
            const tag = dataSource.find((d) => d.value === v);
            if (tag) {
              return { name: tag.name, group: tag.group };
            }
            return null;
          })
          .filter((x) => x) as TagValue[];

        onValueChange?.(value, bizValue);
      }}
    >
      <div className={"flex flex-col gap-3 max-h-full min-h-0"}>
        <div>
          <Input
            size={"sm"}
            placeholder={t<string>("common.placeholder.search")}
            startContent={<SearchOutlined className={"text-small"} />}
            value={keyword}
            onValueChange={(v) => setKeyword(v)}
          />
        </div>

        <div className={"flex flex-col gap-3 min-h-0 overflow-y-auto"}>
          {dataSource.length === 0 ? (
            <div className={"text-default-400 text-center py-4"}>
              {t<string>("No choices available, please check your configurations")}
            </div>
          ) : (
            <>
              {/* Ungrouped tags */}
              {groupedTags.ungrouped.length > 0 && (
                <div className={"flex flex-wrap gap-1"}>
                  {groupedTags.ungrouped.map(renderTagButton)}
                </div>
              )}

              {/* Grouped tags */}
              {groupNames.map((groupName) => (
                <div key={groupName} className={"flex flex-col gap-1"}>
                  <div className={"text-sm font-medium text-default-500"}>
                    {groupName}
                  </div>
                  <div className={"flex flex-wrap gap-1 pl-2"}>
                    {groupedTags.groups[groupName].map(renderTagButton)}
                  </div>
                </div>
              ))}

              {/* No results after filtering */}
              {groupedTags.ungrouped.length === 0 && groupNames.length === 0 && keyword.length > 0 && (
                <div className={"text-default-400 text-center py-4"}>
                  {t<string>("No data")}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </Modal>
  );
};

TagsValueEditor.displayName = "TagsValueEditor";

export default TagsValueEditor;
