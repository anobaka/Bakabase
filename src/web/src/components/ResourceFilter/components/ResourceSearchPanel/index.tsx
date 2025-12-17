"use client";

import type { SearchFilter, SearchFilterGroup } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useMemo } from "react";
import { AppstoreOutlined, FilterOutlined, SearchOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { useTranslation } from "react-i18next";

import FilterModal from "../FilterModal";
import RecentFilters from "../RecentFilters";
import FilterGroup from "../FilterGroup";
import QuickFilters from "./QuickFilters";
import { FilterProvider, useFilterConfig } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";
import { GroupCombinator } from "../../models";

import { Button, Checkbox, CheckboxGroup, Divider, Input, Popover } from "@/components/bakaui";
import { resourceTags } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/**
 * 搜索条件数据结构，与后端 SearchCriteria 对应
 */
export interface SearchCriteria {
  group?: SearchFilterGroup;
  keyword?: string;
  tags?: ResourceTag[];
}

interface ResourceSearchPanelProps {
  /** 当前搜索条件 */
  criteria: SearchCriteria;
  /** 搜索条件变更回调 */
  onChange: (criteria: SearchCriteria) => void;
  /** 是否显示关键词搜索框 */
  showKeyword?: boolean;
  /** 是否显示特殊标签筛选 */
  showTags?: boolean;
  /** 是否显示快捷筛选 */
  showQuickFilters?: boolean;
  /** 是否显示最近使用的筛选器 */
  showRecentFilters?: boolean;
  /** 是否显示筛选器分组预览 */
  showFilterGroupPreview?: boolean;
  /** 是否紧凑模式（用于 Modal 内） */
  compact?: boolean;
}

/**
 * 统一的资源搜索面板组件
 * 用于 ResourceProfile、BulkModification、Resource 页面等场景
 */
const ResourceSearchPanelInner = ({
  criteria,
  onChange,
  showKeyword = false,
  showTags = true,
  showQuickFilters = true,
  showRecentFilters = true,
  showFilterGroupPreview = true,
  compact = false,
}: ResourceSearchPanelProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const addFilter = (filter: SearchFilter) => {
    const newGroup: SearchFilterGroup = criteria.group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.filters) {
      newGroup.filters = [];
    }
    newGroup.filters.push(filter);

    onChange({ ...criteria, group: newGroup });
  };

  const addFilterGroup = () => {
    const newSubGroup: SearchFilterGroup = {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    const newGroup: SearchFilterGroup = criteria.group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.groups) {
      newGroup.groups = [];
    }
    newGroup.groups.push(newSubGroup);

    onChange({ ...criteria, group: newGroup });
  };

  const hasFilters =
    criteria.group &&
    ((criteria.group.filters && criteria.group.filters.length > 0) ||
      (criteria.group.groups && criteria.group.groups.length > 0));

  return (
    <div className="flex flex-col gap-3">
      {/* 关键词搜索 */}
      {showKeyword && (
        <Input
          isClearable
          className="w-full"
          placeholder={t<string>("Search everything")}
          startContent={<SearchOutlined className="text-lg" />}
          value={criteria.keyword || ""}
          onValueChange={(v) => {
            onChange({ ...criteria, keyword: v || undefined });
          }}
        />
      )}

      {/* 筛选器添加按钮 */}
      <div className="flex items-center gap-2">
        <Popover
          showArrow
          placement="bottom"
          trigger={
            <Button isIconOnly size={compact ? "sm" : "md"}>
              <TbFilterPlus className="text-lg" />
            </Button>
          }
        >
          <div
            className="grid items-center gap-2 my-3 mx-1"
            style={{ gridTemplateColumns: "auto auto" }}
          >
            {showQuickFilters && (
              <>
                <QuickFilters
                  onAdded={(filter) => {
                    createPortal(FilterModal, {
                      filter,
                      onSubmit: (f: SearchFilter) => {
                        addFilter(f);
                      },
                    });
                  }}
                />
                <div />
                <Divider orientation="horizontal" />
              </>
            )}

            <div>{t<string>("Advance filter")}</div>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                onPress={() => {
                  createPortal(FilterModal, {
                    isNew: true,
                    filter: { disabled: false },
                    onSubmit: (filter: SearchFilter) => {
                      addFilter(filter);
                    },
                  });
                }}
              >
                <FilterOutlined className="text-base" />
                {t<string>("Filter")}
              </Button>
              <Button
                size="sm"
                onPress={() => {
                  addFilterGroup();
                }}
              >
                <AppstoreOutlined className="text-base" />
                {t<string>("Filter group")}
              </Button>
            </div>

            {showTags && (
              <>
                <div />
                <Divider orientation="horizontal" />
                <div>{t<string>("Special filters")}</div>
                <div>
                  <CheckboxGroup
                    size="sm"
                    value={criteria.tags?.map((tag) => tag.toString())}
                    onChange={(ts) => {
                      const newTags = ts.map((tag) => parseInt(tag, 10) as ResourceTag);
                      onChange({
                        ...criteria,
                        tags: newTags.length > 0 ? newTags : undefined,
                      });
                    }}
                  >
                    {resourceTags.map((rt) => (
                      <Checkbox key={rt.value} value={rt.value.toString()}>
                        {t<string>(`ResourceTag.${rt.label}`)}
                      </Checkbox>
                    ))}
                  </CheckboxGroup>
                </div>
              </>
            )}

            {showRecentFilters && (
              <>
                <div />
                <Divider />
                <div>{t<string>("Recent filters")}</div>
                <div>
                  <RecentFilters
                    onSelectFilter={(filter) => {
                      addFilter(filter);
                    }}
                  />
                </div>
              </>
            )}
          </div>
        </Popover>

        <span className="text-sm text-default-500">
          {hasFilters
            ? t<string>("{{count}} filters configured", {
                count:
                  (criteria.group?.filters?.length || 0) + (criteria.group?.groups?.length || 0),
              })
            : t<string>("Click to add filters")}
        </span>
      </div>

      {/* 筛选器分组预览 */}
      {showFilterGroupPreview && hasFilters && criteria.group && (
        <div className="border rounded p-2">
          <FilterGroup
            isRoot
            group={criteria.group}
            onChange={(group) => {
              onChange({ ...criteria, group });
            }}
          />
        </div>
      )}

      {/* 标签显示 */}
      {criteria.tags && criteria.tags.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {criteria.tags.map((tag) => (
            <span
              key={tag}
              className="px-2 py-1 text-xs bg-default-100 rounded cursor-pointer hover:bg-default-200"
              onClick={() => {
                onChange({
                  ...criteria,
                  tags: criteria.tags?.filter((t) => t !== tag),
                });
              }}
            >
              {t<string>(`ResourceTag.${resourceTags.find((rt) => rt.value === tag)?.label}`)} &times;
            </span>
          ))}
        </div>
      )}
    </div>
  );
};

/**
 * 带有 FilterProvider 的 ResourceSearchPanel
 */
const ResourceSearchPanel = (props: ResourceSearchPanelProps) => {
  const { createPortal } = useBakabaseContext();
  const filterConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

  return (
    <FilterProvider config={filterConfig}>
      <ResourceSearchPanelInner {...props} />
    </FilterProvider>
  );
};

ResourceSearchPanel.displayName = "ResourceSearchPanel";

export default ResourceSearchPanel;
