"use client";

import type { SearchFilter } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { TbSwitch2, TbFilter, TbFilterStar } from "react-icons/tb";

import FilterAddPopoverContent from "../FilterAddPopoverContent";
import { useFilterConfig } from "../../context/FilterContext";
import { getSimpleFilterOperation, getRangeFilterOperations, isRangeFilterType } from "../../utils/simpleFilterOperations";

import { Button, Popover, Tooltip } from "@/components/bakaui";
import { FilterDisplayMode } from "@/sdk/constants";

export interface FilterPortalProps {
  /** Current filter display mode */
  mode: FilterDisplayMode;
  /** Callback to change mode */
  onModeChange: (mode: FilterDisplayMode) => void;
  /** Called when adding a new empty filter (Advanced mode) */
  onAddFilter: (autoTriggerPropertySelector?: boolean) => void;
  /** Called when adding a filter group */
  onAddFilterGroup: () => void;
  /** Called when selecting filters (may create multiple filters for range types like Number, Date, Rating) */
  onSelectFilters: (filters: SearchFilter[]) => void;
  /** Whether to show tags selection in Advanced mode popover */
  showTags?: boolean;
  /** Current selected tags */
  selectedTags?: ResourceTag[];
  /** Called when tags change */
  onTagsChange?: (tags: ResourceTag[]) => void;
  /** Whether to show recent filters in Advanced mode popover */
  showRecentFilters?: boolean;
  /** Current filters - used to disable already selected properties in Simple mode */
  currentFilters?: SearchFilter[];
}

/** @deprecated Use FilterPortalProps instead */
export type ResourceFilterPortalProps = FilterPortalProps;

const FilterPortal = ({
  mode,
  onModeChange,
  onAddFilter,
  onAddFilterGroup,
  onSelectFilters,
  showTags = true,
  selectedTags,
  onTagsChange,
  showRecentFilters = true,
  currentFilters = [],
}: FilterPortalProps) => {
  const { t } = useTranslation();
  const [popoverOpen, setPopoverOpen] = useState(false);
  const config = useFilterConfig();

  const handleSimpleModeAddFilter = () => {
    // In Simple mode, open property selector directly without adding empty filter
    config.renderers.openPropertySelector(
      undefined,
      async (property, availableOperations) => {
        // Check if this property type needs range filters (e.g., Number, Date, Rating)
        const rangeOperations = property.type ? getRangeFilterOperations(property.type) : undefined;

        if (rangeOperations) {
          // Create two filters for range types
          const [minOp, maxOp] = rangeOperations;

          const baseFilter = {
            propertyId: property.id,
            propertyPool: property.pool,
            property,
            availableOperations,
            disabled: false,
          };

          const minFilter: SearchFilter = { ...baseFilter, operation: minOp };
          const maxFilter: SearchFilter = { ...baseFilter, operation: maxOp };

          // Fetch value properties for both filters
          const [minValueProperty, maxValueProperty] = await Promise.all([
            config.api.getValueProperty(minFilter),
            config.api.getValueProperty(maxFilter),
          ]);

          onSelectFilters([
            { ...minFilter, valueProperty: minValueProperty },
            { ...maxFilter, valueProperty: maxValueProperty },
          ]);
        } else {
          // Single filter for non-range types
          const operation = property.type
            ? getSimpleFilterOperation(property.type)
            : availableOperations[0];

          const newFilter: SearchFilter = {
            propertyId: property.id,
            propertyPool: property.pool,
            property,
            availableOperations,
            operation,
            disabled: false,
          };

          // Fetch value property and then add filter
          const valueProperty = await config.api.getValueProperty(newFilter);
          onSelectFilters([{ ...newFilter, valueProperty }]);
        }
      }
    );
  };

  if (mode === FilterDisplayMode.Simple) {
    // Simple mode: button with tooltip for mode switch
    return (
      <Tooltip
        placement="right"
        delay={1000}
        content={
          <Button
            size="sm"
            variant="light"
            color="primary"
            onPress={() => onModeChange(FilterDisplayMode.Advanced)}
          >
            <TbSwitch2 className="text-lg" />
            {t<string>("resourceFilter.switchToAdvancedMode")}
          </Button>
        }
      >
        <Button
          isIconOnly
          size="md"
          onPress={handleSimpleModeAddFilter}
        >
          <TbFilter className="text-lg" />
        </Button>
      </Tooltip>
    );
  }

  // Advanced mode: button with popover for all options
  return (
    <Popover
      showArrow
      placement="bottom"
      isOpen={popoverOpen}
      onOpenChange={setPopoverOpen}
      trigger={
        <Button isIconOnly size="md" color="primary">
          <TbFilterStar className="text-lg" />
        </Button>
      }
    >
      <FilterAddPopoverContent
        showTags={showTags}
        selectedTags={selectedTags}
        onTagsChange={onTagsChange}
        showRecentFilters={showRecentFilters}
        onAddFilter={(autoTrigger) => {
          setPopoverOpen(false);
          onAddFilter(autoTrigger);
        }}
        onAddFilterGroup={() => {
          setPopoverOpen(false);
          onAddFilterGroup();
        }}
        onSelectFilters={(filters) => {
          setPopoverOpen(false);
          onSelectFilters(filters);
        }}
        onClose={() => setPopoverOpen(false)}
        onSwitchToSimpleMode={() => {
          setPopoverOpen(false);
          onModeChange(FilterDisplayMode.Simple);
        }}
      />
    </Popover>
  );
};

FilterPortal.displayName = "FilterPortal";

/** @deprecated Use FilterPortal instead */
export const ResourceFilterPortal = FilterPortal;

export default FilterPortal;
