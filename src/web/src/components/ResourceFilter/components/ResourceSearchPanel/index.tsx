"use client";

import type { SearchFilter, SearchFilterGroup } from "../../models";
import { ResourceTag } from "@/sdk/constants";

import { useMemo, useState } from "react";
import { SearchOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { useTranslation } from "react-i18next";

import FilterGroup from "../FilterGroup";
import FilterAddPopoverContent from "../FilterAddPopoverContent";
import { FilterProvider } from "../../context/FilterContext";
import { createDefaultFilterConfig } from "../../presets/DefaultFilterPreset";
import { GroupCombinator } from "../../models";

import { Button, Input, Popover } from "@/components/bakaui";
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
  const [popoverOpen, setPopoverOpen] = useState(false);
  const [newFilterIndex, setNewFilterIndex] = useState<number | null>(null);

  const addFilter = (filter: SearchFilter, autoTriggerPropertySelector = false) => {
    const newGroup: SearchFilterGroup = criteria.group ?? {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [],
      groups: [],
    };

    if (!newGroup.filters) {
      newGroup.filters = [];
    }

    // Track the index if we need to auto-trigger property selector
    if (autoTriggerPropertySelector) {
      setNewFilterIndex(newGroup.filters.length);
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
    <div className="flex gap-3 flex-wrap">
      {/* 关键词搜索 */}
      {showKeyword && (
        <Input
          isClearable
          className="grow"
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
          isOpen={popoverOpen}
          onOpenChange={setPopoverOpen}
          trigger={
            <Button isIconOnly size={compact ? "md" : "md"}>
              <TbFilterPlus className="text-lg" />
            </Button>
          }
        >
          <FilterAddPopoverContent
            showQuickFilters={showQuickFilters}
            showTags={showTags}
            selectedTags={criteria.tags}
            onTagsChange={(tags) => {
              onChange({
                ...criteria,
                tags: tags.length > 0 ? tags : undefined,
              });
            }}
            showRecentFilters={showRecentFilters}
            onAddFilter={(autoTrigger) => addFilter({ disabled: false }, autoTrigger)}
            onAddFilterGroup={addFilterGroup}
            onSelectFilter={addFilter}
            onClose={() => setPopoverOpen(false)}
          />
        </Popover>
      </div>

      {/* 筛选器分组预览 */}
      {showFilterGroupPreview && hasFilters && criteria.group && (
        <div className="border border-default-200 rounded px-1 py-1">
          <FilterGroup
            isRoot
            group={criteria.group}
            externalNewFilterIndex={newFilterIndex}
            onExternalNewFilterConsumed={() => setNewFilterIndex(null)}
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
