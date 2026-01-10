"use client";

"use strict";

import type { SearchFilter } from "../../models";

import { useEffect, useState } from "react";
import { useUpdateEffect } from "react-use";
import { MdOutlineFilterAltOff } from "react-icons/md";

import { useFilterConfig } from "../../context/FilterContext";
import { getSimpleFilterOperation } from "../../utils/simpleFilterOperations";
import PropertyField from "./PropertyField";
import OperationSelector from "./OperationSelector";
import DeleteButton from "./DeleteButton";
import DisableButton from "./DisableButton";
import { isCompactValueType, isInlineValueType } from "./utils";

import { FilterDisplayMode, PropertyType, SearchOperation } from "@/sdk/constants";
import { buildLogger } from "@/components/utils";

export type FilterLayout = "horizontal" | "vertical";

interface IProps {
  filter: SearchFilter;
  onRemove?: () => any;
  onChange?: (filter: SearchFilter) => any;
  isReadonly?: boolean;
  isNew?: boolean;
  removeBackground?: boolean;
  /** Auto trigger property selector on mount (for newly added filters) */
  autoTriggerPropertySelector?: boolean;
  /** Called when user cancels property selection for a new filter */
  onCancelNewFilter?: () => void;
  /** Display mode: Simple hides operation selector and uses inline value editing */
  filterDisplayMode?: FilterDisplayMode;
  /** Layout: horizontal (default) keeps all in one row, vertical puts value on new line */
  layout?: FilterLayout;
}

const log = buildLogger("Filter");

const Filter = ({
  filter: propsFilter,
  onRemove,
  onChange,
  isReadonly,
  isNew,
  removeBackground,
  autoTriggerPropertySelector,
  onCancelNewFilter,
  filterDisplayMode,
  layout = "horizontal",
}: IProps) => {
  const config = useFilterConfig();
  const [filter, setFilter] = useState<SearchFilter>(propsFilter);

  // In Simple mode, use inline editing and hide operation selector
  const isSimpleMode = filterDisplayMode === FilterDisplayMode.Simple;
  const isVertical = layout === "vertical";

  useEffect(() => {
    // Only open property selector if no property is selected yet
    // (filters created in Simple mode already have property selected)
    if ((isNew || autoTriggerPropertySelector) && !filter.propertyId) {
      openPropertySelector();
    } else if (filter.propertyPool && filter.propertyId && filter.operation) {
      // Restored filter needs to fetch property info and valueProperty for display
      if (!filter.property) {
        // Fetch property info
        config.api.getValueProperty(filter).then((p) => {
          if (p) {
            setFilter((prev) => ({
              ...prev,
              property: p,
              valueProperty: p,
            }));
          }
        });
      } else if (!filter.valueProperty) {
        // Only fetch valueProperty
        config.api.getValueProperty(filter).then((p) => {
          if (p) {
            setFilter((prev) => ({
              ...prev,
              valueProperty: p,
            }));
          }
        });
      }
    }
  }, []);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  const changeFilter = (newFilter: SearchFilter) => {
    setFilter(newFilter);
    onChange?.(newFilter);
    if (newFilter.propertyPool && newFilter.propertyId && newFilter.operation) {
      config.api.saveRecentFilter(newFilter);
    }
  };

  log(propsFilter, filter);

  const openPropertySelector = () => {
    config.renderers.openPropertySelector(
      filter.propertyId == undefined
        ? undefined
        : {
            id: filter.propertyId,
            pool: filter.propertyPool!,
          },
      (property, availableOperations) => {
        handlePropertySelect(property, availableOperations);
      },
      onCancelNewFilter
    );
  };

  const handlePropertySelect = (property: any, availableOperations: SearchOperation[]) => {
    // In Simple mode, use the predefined operation based on property type
    const operation = isSimpleMode && property.type
      ? getSimpleFilterOperation(property.type)
      : availableOperations[0];

    const nf: SearchFilter = {
      ...filter,
      propertyId: property.id,
      propertyPool: property.pool,
      dbValue: undefined,
      bizValue: undefined,
      property,
      availableOperations,
      operation,
    };

    refreshValue(nf);
  };

  const refreshValue = (filter: SearchFilter) => {
    if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
      filter.valueProperty = undefined;
      filter.dbValue = undefined;
      filter.bizValue = undefined;
      changeFilter({
        ...filter,
      });
    } else {
      // Clear value for operations that don't need it (IsNull, IsNotNull)
      if (filter.operation === SearchOperation.IsNull || filter.operation === SearchOperation.IsNotNull) {
        filter.dbValue = undefined;
        filter.bizValue = undefined;
      }
      config.api.getValueProperty(filter).then((p) => {
        filter.valueProperty = p;
        changeFilter({
          ...filter,
        });
      });
    }
  };

  const renderValue = () => {
    if (!filter.valueProperty) {
      return null;
    }

    if (
      filter.operation == SearchOperation.IsNull ||
      filter.operation == SearchOperation.IsNotNull
    ) {
      return null;
    }

    // In Simple mode, always show editing UI (isEditing: true)
    // For inline editing renderers (Choice, Tags, Multilevel), they automatically show options when !isReadonly
    // For text/number inputs, isEditing makes them always show the input field

    const valueElement = config.renderers.renderValueInput(
      filter.valueProperty,
      filter.dbValue,
      filter.bizValue,
      (dbValue, bizValue) => {
        changeFilter({
          ...filter,
          dbValue: dbValue,
          bizValue: bizValue,
        });
      },
      {
        size: "sm",
        variant: "light",
        isReadonly,
        operation: filter.operation,
        isEditing: isSimpleMode,
      }
    );

    // For compact value types in Simple mode (editing mode), constrain the input width
    // In display mode (Advanced mode without isEditing), no width constraint needed
    if (isSimpleMode && isCompactValueType(filter.property?.type)) {
      const propertyType = filter.property?.type;
      // Date/DateTime/Time need more width than Number/Rating/Percentage/Boolean
      const isDateTimeType = propertyType === PropertyType.Date ||
        propertyType === PropertyType.DateTime ||
        propertyType === PropertyType.Time;
      const widthClass = isDateTimeType ? "w-auto" : "w-16";
      return <div className={widthClass}>{valueElement}</div>;
    }

    return valueElement;
  };

  log("rendering filter", filter);

  const toggleDisabled = () => {
    changeFilter({
      ...filter,
      disabled: !filter.disabled,
    });
  };

  // In Simple mode, compact value types (Number, Rating, Percentage, Boolean) use single row
  const isCompact = isSimpleMode && isCompactValueType(filter.property?.type);
  const useVerticalLayout = isVertical && !isCompact;
  // Don't use full width for inline value types (Tags, Multilevel, Choice) - they have inline selectors
  const useFullWidth = useVerticalLayout && !isInlineValueType(filter.property?.type);

  return (
    <div
      className={`flex ${useVerticalLayout ? "flex-col" : ""} ${useFullWidth ? "w-full" : ""} rounded p-1 ${useVerticalLayout ? "gap-1" : "items-center"} relative`}
      style={
        removeBackground ? undefined : { backgroundColor: "var(--bakaui-overlap-background)" }
      }
    >
      {/* Disabled overlay */}
      {filter.disabled && (
        <div
          className="absolute top-0 left-0 w-full h-full flex items-center justify-center z-10 rounded pointer-events-none"
          style={{ backgroundColor: "var(--bakaui-overlap-background)" }}
        >
          <MdOutlineFilterAltOff className="text-lg text-warning" />
        </div>
      )}

      {/* First row: Actions + Property + Operation */}
      <div className={`flex items-center gap-1 ${useFullWidth ? "w-full" : ""} ${filter.disabled ? "opacity-40" : ""}`}>
        {/* Inline action buttons (delete + disable) for both modes */}
        {!isReadonly && (
          <div className="flex items-center">
            <DeleteButton onDelete={onRemove} />
            <DisableButton disabled={filter.disabled} onToggle={toggleDisabled} />
          </div>
        )}

        {/* Property field */}
        <PropertyField
          property={filter.property}
          isReadonly={isReadonly || isSimpleMode}
          onSelect={handlePropertySelect}
          onCancel={onCancelNewFilter}
        />

        {/* Operation selector */}
        {isSimpleMode ? (
          // Simple mode: readonly operation display
          filter.operation !== undefined && (
            <OperationSelector
              operation={filter.operation}
              propertyType={filter.property?.type}
              isReadonly
            />
          )
        ) : (
          // Advanced mode: operation dropdown
          <OperationSelector
            operation={filter.operation}
            propertyType={filter.property?.type}
            availableOperations={filter.availableOperations}
            hasProperty={filter.propertyId !== undefined}
            isReadonly={isReadonly}
            onSelect={(op) => refreshValue({ ...filter, operation: op })}
          />
        )}

        {/* Value in same row for horizontal layout or compact types */}
        {!useVerticalLayout && <div className="pr-2">{renderValue()}</div>}
      </div>

      {/* Second row: Value (only in vertical layout for non-compact types) */}
      {useVerticalLayout && (
        <div className={`${useFullWidth ? "w-full" : ""} ${filter.disabled ? "opacity-40" : ""}`}>
          {renderValue()}
        </div>
      )}
    </div>
  );
};

Filter.displayName = "Filter";

export default Filter;
