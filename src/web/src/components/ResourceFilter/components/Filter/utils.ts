"use strict";

import { PropertyType, SearchOperation } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";

// Property types that should use symbol display for operations
export const numericPropertyTypes: PropertyType[] = [
  PropertyType.Number,
  PropertyType.Percentage,
  PropertyType.Rating,
  PropertyType.Date,
  PropertyType.DateTime,
  PropertyType.Time,
];

// Property types that have compact value inputs (can fit in single row)
export const compactValuePropertyTypes: PropertyType[] = [
  PropertyType.Number,
  PropertyType.Percentage,
  PropertyType.Rating,
  PropertyType.Boolean,
  PropertyType.Date,
  PropertyType.DateTime,
  PropertyType.Time,
];

// Property types that have inline value selectors (don't need full width in vertical layout)
export const inlineValuePropertyTypes: PropertyType[] = [
  PropertyType.SingleChoice,
  PropertyType.MultipleChoice,
  PropertyType.Tags,
  PropertyType.Multilevel,
];

/**
 * Check if a property type has a compact value input that can fit in a single row.
 * Used in Simple mode to decide whether to use single-row or two-row layout.
 */
export const isCompactValueType = (propertyType?: PropertyType): boolean => {
  return propertyType !== undefined && compactValuePropertyTypes.includes(propertyType);
};

/**
 * Check if a property type has an inline value selector that doesn't need full width.
 * Used to avoid w-full in vertical layout for types like Tags, Multilevel, Choice.
 */
export const isInlineValueType = (propertyType?: PropertyType): boolean => {
  return propertyType !== undefined && inlineValuePropertyTypes.includes(propertyType);
};

// Check if a property type should use symbol display
export const shouldUseSymbol = (propertyType?: PropertyType): boolean => {
  return propertyType !== undefined && numericPropertyTypes.includes(propertyType);
};

// Get display text for operation (symbol if property type is numeric, otherwise translation)
export const getOperationDisplay = (
  operation: SearchOperation,
  propertyType: PropertyType | undefined,
  t: (key: string) => string
): string => {
  const baseKey = getEnumKey('SearchOperation', SearchOperation[operation]);

  if (shouldUseSymbol(propertyType)) {
    const symbolKey = `${baseKey}.symbol`;
    const symbol = t(symbolKey);
    // If symbol key exists (not equal to key itself), use symbol
    if (symbol !== symbolKey) {
      return symbol;
    }
  }

  return t(baseKey);
};

// Get operation display with optional symbol prefix for dropdown items
export const getOperationDropdownDisplay = (
  operation: SearchOperation,
  propertyType: PropertyType | undefined,
  t: (key: string) => string
): { displayText: string; description?: string } => {
  const baseKey = getEnumKey('SearchOperation', SearchOperation[operation]);
  const descriptionKey = `${baseKey}.description`;
  const description = t(descriptionKey);
  const label = t(baseKey);

  // For dropdown items, show "symbol label" format if property is numeric and symbol exists
  let displayText = label;
  if (shouldUseSymbol(propertyType)) {
    const symbolKey = `${baseKey}.symbol`;
    const symbol = t(symbolKey);
    if (symbol !== symbolKey) {
      displayText = `${symbol} ${label}`;
    }
  }

  return {
    displayText,
    description: description === descriptionKey ? undefined : description,
  };
};
