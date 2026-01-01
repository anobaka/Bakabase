/**
 * PropertySystem - Type-safe utilities for the Property system.
 *
 * This module provides compile-time type safety for:
 * - PropertyType to StandardValueType mapping
 * - Property options by type (discriminated unions)
 * - Value types for each StandardValueType
 * - Type-safe property attribute access
 *
 * Mirrors the backend PropertySystem for consistency.
 */

import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import { PropertyType, StandardValueType } from "@/sdk/constants";
import type { LinkValue, TagValue, MultilevelData } from "@/components/StandardValue/models";

// ============================================================================
// StandardValue Type Mapping
// ============================================================================

/**
 * Maps StandardValueType enum to actual TypeScript types.
 * Use this for type-safe value handling.
 */
export type StandardValueTypeMap = {
  [StandardValueType.String]: string;
  [StandardValueType.ListString]: string[];
  [StandardValueType.Decimal]: number;
  [StandardValueType.Link]: LinkValue;
  [StandardValueType.Boolean]: boolean;
  [StandardValueType.DateTime]: Dayjs;
  [StandardValueType.Time]: Duration;
  [StandardValueType.ListListString]: string[][];
  [StandardValueType.ListTag]: TagValue[];
};

/**
 * Get the TypeScript type for a StandardValueType.
 */
export type StandardValueOf<T extends StandardValueType> = StandardValueTypeMap[T];

// ============================================================================
// PropertyType Attributes
// ============================================================================

/**
 * Property attributes defining DB/Biz value types and reference status.
 */
export interface PropertyAttribute {
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  isReferenceValueType: boolean;
}

/**
 * Complete mapping of PropertyType to its attributes.
 * Mirrors backend PropertyInternals.PropertyAttributeMap.
 */
export const PropertyAttributeMap: Record<PropertyType, PropertyAttribute> = {
  [PropertyType.SingleLineText]: {
    dbValueType: StandardValueType.String,
    bizValueType: StandardValueType.String,
    isReferenceValueType: false,
  },
  [PropertyType.MultilineText]: {
    dbValueType: StandardValueType.String,
    bizValueType: StandardValueType.String,
    isReferenceValueType: false,
  },
  [PropertyType.SingleChoice]: {
    dbValueType: StandardValueType.String,
    bizValueType: StandardValueType.String,
    isReferenceValueType: true,
  },
  [PropertyType.MultipleChoice]: {
    dbValueType: StandardValueType.ListString,
    bizValueType: StandardValueType.ListString,
    isReferenceValueType: true,
  },
  [PropertyType.Number]: {
    dbValueType: StandardValueType.Decimal,
    bizValueType: StandardValueType.Decimal,
    isReferenceValueType: false,
  },
  [PropertyType.Percentage]: {
    dbValueType: StandardValueType.Decimal,
    bizValueType: StandardValueType.Decimal,
    isReferenceValueType: false,
  },
  [PropertyType.Rating]: {
    dbValueType: StandardValueType.Decimal,
    bizValueType: StandardValueType.Decimal,
    isReferenceValueType: false,
  },
  [PropertyType.Boolean]: {
    dbValueType: StandardValueType.Boolean,
    bizValueType: StandardValueType.Boolean,
    isReferenceValueType: false,
  },
  [PropertyType.Link]: {
    dbValueType: StandardValueType.Link,
    bizValueType: StandardValueType.Link,
    isReferenceValueType: false,
  },
  [PropertyType.Attachment]: {
    dbValueType: StandardValueType.ListString,
    bizValueType: StandardValueType.ListString,
    isReferenceValueType: false,
  },
  [PropertyType.Date]: {
    dbValueType: StandardValueType.DateTime,
    bizValueType: StandardValueType.DateTime,
    isReferenceValueType: false,
  },
  [PropertyType.DateTime]: {
    dbValueType: StandardValueType.DateTime,
    bizValueType: StandardValueType.DateTime,
    isReferenceValueType: false,
  },
  [PropertyType.Time]: {
    dbValueType: StandardValueType.Time,
    bizValueType: StandardValueType.Time,
    isReferenceValueType: false,
  },
  [PropertyType.Formula]: {
    dbValueType: StandardValueType.String,
    bizValueType: StandardValueType.String,
    isReferenceValueType: false,
  },
  [PropertyType.Multilevel]: {
    dbValueType: StandardValueType.ListString,
    bizValueType: StandardValueType.ListListString,
    isReferenceValueType: true,
  },
  [PropertyType.Tags]: {
    dbValueType: StandardValueType.ListString,
    bizValueType: StandardValueType.ListTag,
    isReferenceValueType: true,
  },
};

// ============================================================================
// PropertyType to StandardValueType Helpers
// ============================================================================

/**
 * Get the DB value type for a PropertyType.
 */
export function getDbValueType(propertyType: PropertyType): StandardValueType {
  return PropertyAttributeMap[propertyType].dbValueType;
}

/**
 * Get the Biz value type for a PropertyType.
 */
export function getBizValueType(propertyType: PropertyType): StandardValueType {
  return PropertyAttributeMap[propertyType].bizValueType;
}

/**
 * Check if a PropertyType stores reference values (UUIDs).
 */
export function isReferenceValueType(propertyType: PropertyType): boolean {
  return PropertyAttributeMap[propertyType].isReferenceValueType;
}

/**
 * Get the full attribute for a PropertyType.
 */
export function getAttribute(propertyType: PropertyType): PropertyAttribute {
  return PropertyAttributeMap[propertyType];
}

// ============================================================================
// Type-Safe Property Options
// ============================================================================

/**
 * Choice option for SingleChoice/MultipleChoice properties.
 */
export interface ChoiceOption {
  value: string;
  label?: string;
  color?: string;
  hide?: boolean;
}

/**
 * Tag option for Tags properties.
 */
export interface TagOption {
  value: string;
  group?: string;
  name?: string;
  color?: string;
  hide?: boolean;
}

/**
 * Options for SingleChoice property.
 */
export interface SingleChoicePropertyOptions {
  choices: ChoiceOption[];
  defaultValue?: string;
}

/**
 * Options for MultipleChoice property.
 */
export interface MultipleChoicePropertyOptions {
  choices: ChoiceOption[];
  defaultValue?: string;
}

/**
 * Options for Number property.
 */
export interface NumberPropertyOptions {
  precision: number;
}

/**
 * Options for Percentage property.
 */
export interface PercentagePropertyOptions {
  precision: number;
  showProgressbar: boolean;
}

/**
 * Options for Rating property.
 */
export interface RatingPropertyOptions {
  maxValue: number;
}

/**
 * Options for Multilevel property.
 */
export interface MultilevelPropertyOptions {
  data?: MultilevelData<string>[];
  defaultValue?: string;
}

/**
 * Options for Tags property.
 */
export interface TagsPropertyOptions {
  tags: TagOption[];
}

/**
 * Discriminated union of all property options by PropertyType.
 * Provides compile-time type safety when accessing options.
 */
export type PropertyOptionsMap = {
  [PropertyType.SingleLineText]: undefined;
  [PropertyType.MultilineText]: undefined;
  [PropertyType.SingleChoice]: SingleChoicePropertyOptions;
  [PropertyType.MultipleChoice]: MultipleChoicePropertyOptions;
  [PropertyType.Number]: NumberPropertyOptions;
  [PropertyType.Percentage]: PercentagePropertyOptions;
  [PropertyType.Rating]: RatingPropertyOptions;
  [PropertyType.Boolean]: undefined;
  [PropertyType.Link]: undefined;
  [PropertyType.Attachment]: undefined;
  [PropertyType.Date]: undefined;
  [PropertyType.DateTime]: undefined;
  [PropertyType.Time]: undefined;
  [PropertyType.Formula]: undefined;
  [PropertyType.Multilevel]: MultilevelPropertyOptions;
  [PropertyType.Tags]: TagsPropertyOptions;
};

/**
 * Get the options type for a PropertyType.
 */
export type PropertyOptionsOf<T extends PropertyType> = PropertyOptionsMap[T];

/**
 * Property types that have options.
 */
export type PropertyTypeWithOptions = {
  [K in PropertyType]: PropertyOptionsMap[K] extends undefined ? never : K;
}[PropertyType];

/**
 * Property types that don't have options.
 */
export type PropertyTypeWithoutOptions = Exclude<PropertyType, PropertyTypeWithOptions>;

// ============================================================================
// Type-Safe Property Interface
// ============================================================================

/**
 * Base property interface without type-specific options.
 */
export interface BaseProperty {
  id: number;
  name: string;
  type: PropertyType;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  pool: number; // PropertyPool
  typeName: string;
  poolName: string;
  order: number;
  valueCount?: number;
  categories?: { id: number; name: string }[];
}

/**
 * Type-safe property with correctly typed options based on PropertyType.
 */
export type TypedProperty<T extends PropertyType = PropertyType> = BaseProperty & {
  type: T;
  options: PropertyOptionsMap[T];
};

/**
 * Helper to cast a property to a typed property.
 * Use when you know the specific PropertyType at runtime.
 */
export function asTypedProperty<T extends PropertyType>(
  property: BaseProperty & { options?: unknown },
  expectedType: T
): TypedProperty<T> | undefined {
  if (property.type === expectedType) {
    return property as TypedProperty<T>;
  }
  return undefined;
}

// ============================================================================
// PropertyType Groups (for UI organization)
// ============================================================================

/**
 * Property type groups for UI organization.
 */
export const PropertyTypeGroups = {
  text: [PropertyType.SingleLineText, PropertyType.MultilineText, PropertyType.Link] as const,
  number: [PropertyType.Number, PropertyType.Percentage, PropertyType.Rating] as const,
  choice: [PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Multilevel] as const,
  dateTime: [PropertyType.DateTime, PropertyType.Date, PropertyType.Time] as const,
  other: [PropertyType.Attachment, PropertyType.Boolean, PropertyType.Tags] as const,
  underDevelopment: [PropertyType.Formula] as const,
} as const;

/**
 * All property types in display order.
 */
export const AllPropertyTypes = [
  ...PropertyTypeGroups.text,
  ...PropertyTypeGroups.number,
  ...PropertyTypeGroups.choice,
  ...PropertyTypeGroups.dateTime,
  ...PropertyTypeGroups.other,
  ...PropertyTypeGroups.underDevelopment,
] as const;

// ============================================================================
// Value Validation
// ============================================================================

/**
 * Check if a value matches the expected StandardValueType.
 */
export function isValidStandardValue(value: unknown, type: StandardValueType): boolean {
  if (value === null || value === undefined) {
    return true; // null/undefined are valid for any type
  }

  switch (type) {
    case StandardValueType.String:
      return typeof value === "string";
    case StandardValueType.ListString:
      return Array.isArray(value) && value.every((v) => typeof v === "string");
    case StandardValueType.Decimal:
      return typeof value === "number" && !Number.isNaN(value);
    case StandardValueType.Link:
      return (
        typeof value === "object" &&
        value !== null &&
        ("text" in value || "url" in value)
      );
    case StandardValueType.Boolean:
      return typeof value === "boolean";
    case StandardValueType.DateTime:
      // Dayjs check - has isValid method
      return typeof value === "object" && value !== null && "isValid" in value;
    case StandardValueType.Time:
      // Duration check - has asMilliseconds method
      return typeof value === "object" && value !== null && "asMilliseconds" in value;
    case StandardValueType.ListListString:
      return (
        Array.isArray(value) &&
        value.every((v) => Array.isArray(v) && v.every((s) => typeof s === "string"))
      );
    case StandardValueType.ListTag:
      return (
        Array.isArray(value) &&
        value.every((v) => typeof v === "object" && v !== null && "name" in v)
      );
    default:
      return false;
  }
}

// ============================================================================
// Export all utilities
// ============================================================================

export const PropertySystem = {
  // Attribute access
  getAttribute,
  getDbValueType,
  getBizValueType,
  isReferenceValueType,

  // Type info
  PropertyAttributeMap,
  PropertyTypeGroups,
  AllPropertyTypes,

  // Validation
  isValidStandardValue,

  // Type helpers
  asTypedProperty,
} as const;

export default PropertySystem;
