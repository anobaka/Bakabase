"use client";

import type { IProperty } from "@/components/Property/models";
import type { SearchOperation } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AiOutlineInfoCircle } from "react-icons/ai";

import { useFilterConfig } from "../../context/FilterContext";

import { Button, Tooltip } from "@/components/bakaui";
import { PropertyPool, ResourceProperty } from "@/sdk/constants";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";

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
const PropertyField = ({ property, isReadonly, onSelect, onCancel }: PropertyFieldProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();

  const displayText = property
    ? (property.name ?? t<string>("resourceFilter.condition.unknownProperty"))
    : t<string>("resourceFilter.condition.selectProperty");

  const handlePress = () => {
    config.renderers.openPropertySelector(
      property ? { id: property.id, pool: property.pool } : undefined,
      (selectedProperty, availableOperations) => {
        onSelect?.(selectedProperty, availableOperations);
      },
      onCancel,
    );
  };

  const label = (
    <>
      {property && (
        <span className="inline-flex shrink-0 text-default-400">
          <PropertyTypeIcon textVariant="none" type={property.type} />
        </span>
      )}
      <span className="min-w-0 break-words">{displayText}</span>
    </>
  );

  return (
    <span className="inline-flex min-w-0 max-w-full items-center gap-0.5">
      {isReadonly ? (
        <span className="inline-flex min-h-8 min-w-0 items-center gap-1.5 py-1 text-sm font-medium text-foreground">
          {label}
        </span>
      ) : (
        <Button
          className="h-auto min-h-8 min-w-0 max-w-full justify-start gap-1.5 px-1.5 py-1 text-left text-sm font-medium whitespace-normal"
          size="sm"
          variant="light"
          onPress={handlePress}
        >
          {label}
        </Button>
      )}
      {property?.pool === PropertyPool.Internal && property.id === ResourceProperty.Source && (
        <Tooltip content={t<string>("resourceFilter.source.help")}>
          <Button
            isIconOnly
            aria-label={t<string>("resourceFilter.source.about")}
            className="h-8 w-8 min-w-8 shrink-0 text-default-400"
            size="sm"
            variant="light"
          >
            <AiOutlineInfoCircle aria-hidden className="text-base" />
          </Button>
        </Tooltip>
      )}
    </span>
  );
};

PropertyField.displayName = "PropertyField";

export default PropertyField;
