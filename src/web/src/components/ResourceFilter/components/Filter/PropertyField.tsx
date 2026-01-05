"use client";

import type { IProperty } from "@/components/Property/models";
import type { SearchOperation } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import { useFilterConfig } from "../../context/FilterContext";

export interface PropertyFieldProps {
  /** Current property. Used for display and as current selection in selector. */
  property?: IProperty;
  /** If true, renders as non-clickable Chip */
  isReadonly?: boolean;
  /** Called when a new property is selected */
  onSelect?: (property: IProperty, availableOperations: SearchOperation[]) => void;
  /** Called when user cancels property selection */
  onCancel?: () => void;
}

/**
 * Displays the property name and handles property selection.
 * - Readonly mode: non-clickable Chip
 * - Editable mode: clickable Button that opens PropertySelector
 */
const PropertyField = ({
  property,
  isReadonly,
  onSelect,
  onCancel,
}: PropertyFieldProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();

  const displayText = property
    ? (property.name ?? t<string>("Unknown property"))
    : t<string>("Property");

  const handlePress = () => {
    config.renderers.openPropertySelector(
      property
        ? { id: property.id, pool: property.pool }
        : undefined,
      (selectedProperty, availableOperations) => {
        onSelect?.(selectedProperty, availableOperations);
      },
      onCancel
    );
  };

  if (isReadonly) {
    return (
      <span className="text-sm font-medium text-foreground">
        {displayText}
      </span>
    );
  }

  return (
    <button
      type="button"
      className="text-sm font-medium text-foreground hover:text-primary hover:underline cursor-pointer bg-transparent border-none p-0"
      onClick={handlePress}
    >
      {displayText}
    </button>
  );
};

PropertyField.displayName = "PropertyField";

export default PropertyField;
