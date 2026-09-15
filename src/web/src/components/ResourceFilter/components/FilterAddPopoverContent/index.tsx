"use client";

import type { SearchFilter } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AppstoreOutlined, FilterOutlined } from "@ant-design/icons";
import { TbSwitch2 } from "react-icons/tb";

import RecentFilters from "../RecentFilters";

import { Button, Checkbox, CheckboxGroup } from "@/components/bakaui";
import { resourceTags } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";

export interface FilterAddPopoverContentProps {
  /** Called when adding a new empty filter */
  onAddFilter: (autoTriggerPropertySelector?: boolean) => void;
  /** Called when adding a filter group */
  onAddFilterGroup: () => void;
  /** Called when selecting filters (may be multiple for range types) */
  onSelectFilters: (filters: SearchFilter[]) => void;
  /** Whether to show tags selection */
  showTags?: boolean;
  /** Current selected tags (only used when showTags is true) */
  selectedTags?: ResourceTag[];
  /** Called when tags change (only used when showTags is true) */
  onTagsChange?: (tags: ResourceTag[]) => void;
  /** Whether to show recent filters */
  showRecentFilters?: boolean;
  /** Called to close the popover */
  onClose?: () => void;
  /** Called to switch to Simple mode (only shown when provided) */
  onSwitchToSimpleMode?: () => void;
}

const FilterAddPopoverContent = ({
  onAddFilter,
  onAddFilterGroup,
  onSelectFilters,
  showTags = false,
  selectedTags,
  onTagsChange,
  showRecentFilters = true,
  onClose,
  onSwitchToSimpleMode,
}: FilterAddPopoverContentProps) => {
  const { t } = useTranslation();

  return (
    <div className="w-80 max-w-[calc(100vw-2rem)] max-h-[75vh] space-y-4 overflow-y-auto p-2">
      <div>
        <h3 className="text-sm font-semibold">
          {t<string>("resourceFilter.toolbar.addCondition")}
        </h3>
        <p className="mt-1 text-xs leading-relaxed text-default-500">
          {t<string>("resourceFilter.add.description")}
        </p>
      </div>
      <div className="grid grid-cols-2 gap-2">
        <Button
          className="h-auto min-h-16 items-start justify-start whitespace-normal px-3 py-2.5 text-left"
          size="sm"
          variant="flat"
          onPress={() => {
            onClose?.();
            onAddFilter(true);
          }}
        >
          <FilterOutlined className="mt-0.5 shrink-0 text-base text-primary" />
          <span className="min-w-0">
            <span className="block font-medium">{t<string>("resourceFilter.filter")}</span>
            <span className="mt-1 block text-xs leading-relaxed text-default-500">
              {t<string>("resourceFilter.add.conditionHint")}
            </span>
          </span>
        </Button>
        <Button
          className="h-auto min-h-16 items-start justify-start whitespace-normal px-3 py-2.5 text-left"
          size="sm"
          variant="flat"
          onPress={() => {
            onClose?.();
            onAddFilterGroup();
          }}
        >
          <AppstoreOutlined className="mt-0.5 shrink-0 text-base text-primary" />
          <span className="min-w-0">
            <span className="block font-medium">{t<string>("resourceFilter.filterGroup")}</span>
            <span className="mt-1 block text-xs leading-relaxed text-default-500">
              {t<string>("resourceFilter.add.groupHint")}
            </span>
          </span>
        </Button>
      </div>
      {showTags && onTagsChange && (
        <section>
          <h4 className="mb-2 text-xs font-medium text-default-500">
            {t<string>("resourceFilter.specialFilters")}
          </h4>
          <CheckboxGroup
            aria-label={t<string>("resourceFilter.specialFilters")}
            classNames={{ wrapper: "gap-2" }}
            size="sm"
            value={selectedTags?.map((tag) => tag.toString()) ?? []}
            onChange={(tags) => onTagsChange(tags.map((tag) => parseInt(tag, 10) as ResourceTag))}
          >
            {resourceTags.map((tag) => (
              <Checkbox key={tag.value} value={tag.value.toString()}>
                {t<string>(getEnumKey("ResourceTag", tag.label))}
              </Checkbox>
            ))}
          </CheckboxGroup>
        </section>
      )}
      {showRecentFilters && (
        <section>
          <h4 className="mb-2 text-xs font-medium text-default-500">
            {t<string>("resourceFilter.recentFilters")}
          </h4>
          <RecentFilters
            onSelectFilter={(filter) => {
              onClose?.();
              onSelectFilters([filter]);
            }}
          />
        </section>
      )}
      {onSwitchToSimpleMode && (
        <div className="flex justify-end">
          <Button
            color="primary"
            size="sm"
            variant="light"
            onPress={() => {
              onClose?.();
              onSwitchToSimpleMode();
            }}
          >
            <TbSwitch2 className="text-base" />
            {t<string>("resourceFilter.switchToSimpleMode")}
          </Button>
        </div>
      )}
    </div>
  );
};

FilterAddPopoverContent.displayName = "FilterAddPopoverContent";

export default FilterAddPopoverContent;
