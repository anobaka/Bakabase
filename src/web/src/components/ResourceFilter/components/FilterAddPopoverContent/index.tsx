"use client";

import type { SearchFilter } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AppstoreOutlined, FilterOutlined } from "@ant-design/icons";

import RecentFilters from "../RecentFilters";

import { Button, Checkbox, CheckboxGroup, Divider } from "@/components/bakaui";
import { resourceTags, ResourceTag as ResourceTagEnum } from "@/sdk/constants";
import { TbSwitch2 } from "react-icons/tb";
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
    <div
      className="grid items-center gap-2 my-3 mx-1 max-w-[480px]"
      style={{ gridTemplateColumns: "auto auto" }}
    >
      <div>{t<string>("resourceFilter.advanceFilter")}</div>
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          onPress={() => {
            onClose?.();
            onAddFilter(true);
          }}
        >
          <FilterOutlined className="text-base" />
          {t<string>("resourceFilter.filter")}
        </Button>
        <Button
          size="sm"
          onPress={() => {
            onClose?.();
            onAddFilterGroup();
          }}
        >
          <AppstoreOutlined className="text-base" />
          {t<string>("resourceFilter.filterGroup")}
        </Button>
      </div>

      {showTags && onTagsChange && (
        <>
          <div />
          <Divider orientation="horizontal" />
          <div>{t<string>("resourceFilter.specialFilters")}</div>
          <div>
            <CheckboxGroup
              size="sm"
              value={selectedTags?.map((tag) => tag.toString())}
              onChange={(ts) => {
                const newTags = ts.map((tag) => parseInt(tag, 10) as ResourceTag);
                onTagsChange(newTags);
              }}
            >
              {resourceTags
                .filter(
                  (rt) =>
                    rt.value != ResourceTagEnum.PathDoesNotExist &&
                    rt.value != ResourceTagEnum.UnknownMediaLibrary,
                )
                .map((rt) => (
                  <Checkbox key={rt.value} value={rt.value.toString()}>
                    {t<string>(getEnumKey('ResourceTag', rt.label))}
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
          <div>{t<string>("resourceFilter.recentFilters")}</div>
          <div>
            <RecentFilters
              onSelectFilter={(filter) => {
                onClose?.();
                onSelectFilters([filter]);
              }}
            />
          </div>
        </>
      )}

      {onSwitchToSimpleMode && (
        <>
          <div />
          <Divider />
          <div />
          <div className="flex justify-end">
            <Button
              size="sm"
              variant="light"
              color="primary"
              onPress={() => {
                onClose?.();
                onSwitchToSimpleMode();
              }}
            >
              <TbSwitch2 className="text-lg" />
              {t<string>("resourceFilter.switchToSimpleMode")}
            </Button>
          </div>
        </>
      )}
    </div>
  );
};

FilterAddPopoverContent.displayName = "FilterAddPopoverContent";

export default FilterAddPopoverContent;
