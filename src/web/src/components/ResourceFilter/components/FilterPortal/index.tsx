"use client";

import type { SearchFilter } from "../../models";
import type { ResourceTag } from "@/sdk/constants";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { TbFilterPlus } from "react-icons/tb";

import FilterAddPopoverContent from "../FilterAddPopoverContent";
import { useFilterConfig } from "../../context/FilterContext";
import {
  getSimpleFilterOperation,
  getRangeFilterOperations,
} from "../../utils/simpleFilterOperations";

import { Button, Popover } from "@/components/bakaui";
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
    config.renderers.openPropertySelector(undefined, async (property, availableOperations) => {
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
    });
  };

  return (
    <div className="flex max-w-full flex-wrap items-center gap-2">
      {mode === FilterDisplayMode.Simple ? (
        <Button
          color="primary"
          size="sm"
          variant="flat"
          startContent={<TbFilterPlus className="text-base" />}
          onPress={handleSimpleModeAddFilter}
        >
          {t<string>("resourceFilter.toolbar.addCondition")}
        </Button>
      ) : (
        <Popover
          isOpen={popoverOpen}
          placement="bottom-start"
          trigger={
            <Button
              color="primary"
              size="sm"
              variant="flat"
              startContent={<TbFilterPlus className="text-base" />}
            >
              {t<string>("resourceFilter.toolbar.addCondition")}
            </Button>
          }
          onOpenChange={setPopoverOpen}
        >
          <FilterAddPopoverContent
            selectedTags={selectedTags}
            showRecentFilters={showRecentFilters}
            showTags={showTags}
            onAddFilter={onAddFilter}
            onAddFilterGroup={onAddFilterGroup}
            onClose={() => setPopoverOpen(false)}
            onSelectFilters={onSelectFilters}
            onTagsChange={onTagsChange}
          />
        </Popover>
      )}
      <div
        role="group"
        aria-label={t<string>("resourceFilter.toolbar.mode")}
        className="inline-flex items-center rounded-lg bg-default-100/70 p-0.5"
      >
        {[FilterDisplayMode.Simple, FilterDisplayMode.Advanced].map((value) => (
          <Button
            key={value}
            aria-pressed={mode === value}
            size="sm"
            variant="light"
            className={`h-7 min-w-0 px-2.5 text-xs ${mode === value ? "bg-content1 font-medium text-foreground shadow-sm" : "text-default-500"}`}
            title={t<string>(
              value === FilterDisplayMode.Simple
                ? "resourceFilter.toolbar.simpleHint"
                : "resourceFilter.toolbar.advancedHint",
            )}
            onPress={() => {
              if (mode !== value) {
                setPopoverOpen(false);
                onModeChange(value);
              }
            }}
          >
            {t<string>(
              value === FilterDisplayMode.Simple
                ? "resourceFilter.toolbar.simple"
                : "resourceFilter.toolbar.advanced",
            )}
          </Button>
        ))}
      </div>
    </div>
  );
};

FilterPortal.displayName = "FilterPortal";

/** @deprecated Use FilterPortal instead */
export const ResourceFilterPortal = FilterPortal;

export default FilterPortal;
