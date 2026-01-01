"use client";

import type { SearchFilter } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AppstoreOutlined, FilterOutlined } from "@ant-design/icons";

import QuickFilters from "../ResourceSearchPanel/QuickFilters";
import RecentFilters from "../RecentFilters";

import { Button, Checkbox, CheckboxGroup, Divider } from "@/components/bakaui";
import { resourceTags, ResourceTag as ResourceTagEnum } from "@/sdk/constants";

export interface FilterAddPopoverContentProps {
  /** Called when adding a new empty filter */
  onAddFilter: (autoTriggerPropertySelector?: boolean) => void;
  /** Called when adding a filter group */
  onAddFilterGroup: () => void;
  /** Called when selecting a quick filter or recent filter */
  onSelectFilter: (filter: SearchFilter) => void;
  /** Whether to show quick filters */
  showQuickFilters?: boolean;
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
}

const FilterAddPopoverContent = ({
  onAddFilter,
  onAddFilterGroup,
  onSelectFilter,
  showQuickFilters = true,
  showTags = false,
  selectedTags,
  onTagsChange,
  showRecentFilters = true,
  onClose,
}: FilterAddPopoverContentProps) => {
  const { t } = useTranslation();

  return (
    <div
      className="grid items-center gap-2 my-3 mx-1 max-w-[480px]"
      style={{ gridTemplateColumns: "auto auto" }}
    >
      {showQuickFilters && (
        <>
          <QuickFilters
            onAdded={(filter) => {
              onClose?.();
              onSelectFilter(filter);
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
            onClose?.();
            onAddFilter(true);
          }}
        >
          <FilterOutlined className="text-base" />
          {t<string>("Filter")}
        </Button>
        <Button
          size="sm"
          onPress={() => {
            onClose?.();
            onAddFilterGroup();
          }}
        >
          <AppstoreOutlined className="text-base" />
          {t<string>("Filter group")}
        </Button>
      </div>

      {showTags && onTagsChange && (
        <>
          <div />
          <Divider orientation="horizontal" />
          <div>{t<string>("Special filters")}</div>
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
                onClose?.();
                onSelectFilter(filter);
              }}
            />
          </div>
        </>
      )}
    </div>
  );
};

FilterAddPopoverContent.displayName = "FilterAddPopoverContent";

export default FilterAddPopoverContent;
