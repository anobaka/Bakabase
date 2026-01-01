/**
 * StandardValueFactory - Type-safe factory for creating StandardValue instances.
 *
 * Provides compile-time type safety when constructing values.
 * Mirrors the backend StandardValueFactory for consistency.
 */

import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";

import { StandardValueType } from "@/sdk/constants";
import type { LinkValue, TagValue } from "./models";
import type { StandardValueOf } from "@/components/Property/PropertySystem";

// Ensure duration plugin is loaded
dayjs.extend(duration);

// ============================================================================
// StandardValue Wrapper Type
// ============================================================================

/**
 * Type-safe wrapper for StandardValue with compile-time type checking.
 * Similar to backend StandardValue<T>.
 */
export interface StandardValue<T> {
  type: StandardValueType;
  value: T | undefined;
}

/**
 * Check if a StandardValue is empty.
 */
export function isEmpty<T>(sv: StandardValue<T> | undefined): boolean {
  if (!sv || sv.value === undefined || sv.value === null) {
    return true;
  }

  const value = sv.value;

  if (typeof value === "string") {
    return value.length === 0;
  }

  if (Array.isArray(value)) {
    return value.length === 0;
  }

  if (typeof value === "object" && value !== null) {
    // LinkValue check
    if ("text" in value || "url" in value) {
      const lv = value as LinkValue;
      return (!lv.text || lv.text.length === 0) && (!lv.url || lv.url.length === 0);
    }
  }

  return false;
}

// ============================================================================
// String Value Factory
// ============================================================================

/**
 * Create a String StandardValue.
 */
export function createString(value?: string): StandardValue<string> {
  return {
    type: StandardValueType.String,
    value: value?.trim(),
  };
}

// ============================================================================
// ListString Value Factory
// ============================================================================

/**
 * Create a ListString StandardValue.
 */
export function createListString(value?: string[]): StandardValue<string[]> {
  return {
    type: StandardValueType.ListString,
    value: value?.filter((s) => s && s.length > 0),
  };
}

/**
 * Create a ListString from individual strings.
 */
export function createListStringFromItems(...items: string[]): StandardValue<string[]> {
  return createListString(items);
}

// ============================================================================
// Decimal Value Factory
// ============================================================================

/**
 * Create a Decimal StandardValue.
 */
export function createDecimal(value?: number): StandardValue<number> {
  return {
    type: StandardValueType.Decimal,
    value: value !== undefined && !Number.isNaN(value) ? value : undefined,
  };
}

/**
 * Create a Decimal from string (parsed).
 */
export function createDecimalFromString(value?: string): StandardValue<number> {
  if (!value) {
    return { type: StandardValueType.Decimal, value: undefined };
  }

  const parsed = parseFloat(value);
  return createDecimal(Number.isNaN(parsed) ? undefined : parsed);
}

// ============================================================================
// Link Value Factory
// ============================================================================

/**
 * Create a Link StandardValue.
 */
export function createLink(text?: string, url?: string): StandardValue<LinkValue>;
export function createLink(value?: LinkValue): StandardValue<LinkValue>;
export function createLink(
  textOrValue?: string | LinkValue,
  url?: string
): StandardValue<LinkValue> {
  if (typeof textOrValue === "object") {
    return {
      type: StandardValueType.Link,
      value: textOrValue,
    };
  }

  return {
    type: StandardValueType.Link,
    value: { text: textOrValue, url },
  };
}

// ============================================================================
// Boolean Value Factory
// ============================================================================

/**
 * Create a Boolean StandardValue.
 */
export function createBoolean(value?: boolean): StandardValue<boolean> {
  return {
    type: StandardValueType.Boolean,
    value,
  };
}

// ============================================================================
// DateTime Value Factory
// ============================================================================

/**
 * Create a DateTime StandardValue.
 */
export function createDateTime(value?: Dayjs | Date | string | number): StandardValue<Dayjs> {
  let dayjsValue: Dayjs | undefined;

  if (value !== undefined) {
    if (typeof value === "object" && "isValid" in value) {
      // Already a Dayjs instance
      dayjsValue = value as Dayjs;
    } else {
      dayjsValue = dayjs(value);
    }

    if (!dayjsValue.isValid()) {
      dayjsValue = undefined;
    }
  }

  return {
    type: StandardValueType.DateTime,
    value: dayjsValue,
  };
}

// ============================================================================
// Time Value Factory
// ============================================================================

/**
 * Create a Time StandardValue from Duration.
 */
export function createTime(value?: Duration): StandardValue<Duration> {
  return {
    type: StandardValueType.Time,
    value,
  };
}

/**
 * Create a Time StandardValue from milliseconds.
 */
export function createTimeFromMs(milliseconds?: number): StandardValue<Duration> {
  return {
    type: StandardValueType.Time,
    value: milliseconds !== undefined ? dayjs.duration(milliseconds) : undefined,
  };
}

/**
 * Create a Time StandardValue from components.
 */
export function createTimeFromComponents(
  hours?: number,
  minutes?: number,
  seconds?: number
): StandardValue<Duration> {
  if (hours === undefined && minutes === undefined && seconds === undefined) {
    return { type: StandardValueType.Time, value: undefined };
  }

  return {
    type: StandardValueType.Time,
    value: dayjs.duration({
      hours: hours ?? 0,
      minutes: minutes ?? 0,
      seconds: seconds ?? 0,
    }),
  };
}

// ============================================================================
// ListListString Value Factory
// ============================================================================

/**
 * Create a ListListString StandardValue.
 */
export function createListListString(value?: string[][]): StandardValue<string[][]> {
  return {
    type: StandardValueType.ListListString,
    value: value?.filter((arr) => arr && arr.length > 0),
  };
}

// ============================================================================
// ListTag Value Factory
// ============================================================================

/**
 * Create a ListTag StandardValue.
 */
export function createListTag(value?: TagValue[]): StandardValue<TagValue[]> {
  return {
    type: StandardValueType.ListTag,
    value: value?.filter((t) => t && t.name && t.name.length > 0),
  };
}

/**
 * Create a ListTag from group-name tuples.
 */
export function createListTagFromTuples(
  ...tuples: Array<[group: string | undefined, name: string]>
): StandardValue<TagValue[]> {
  return createListTag(
    tuples.map(([group, name]) => ({ group: group ?? undefined, name }))
  );
}

// ============================================================================
// Generic Factory
// ============================================================================

/**
 * Create a StandardValue of any type.
 * Use specific factory functions for better type safety.
 */
export function create<T extends StandardValueType>(
  type: T,
  value?: StandardValueOf<T>
): StandardValue<StandardValueOf<T>> {
  return {
    type,
    value,
  };
}

// ============================================================================
// Export all factories
// ============================================================================

/**
 * Type-safe StandardValue factory.
 * Usage:
 *   StandardValueFactory.String("hello")
 *   StandardValueFactory.ListString(["a", "b"])
 *   StandardValueFactory.Decimal(42.5)
 *   StandardValueFactory.Link("text", "https://example.com")
 *   StandardValueFactory.Boolean(true)
 *   StandardValueFactory.DateTime(dayjs())
 *   StandardValueFactory.Time(dayjs.duration(3600000))
 *   StandardValueFactory.ListListString([["a", "b"], ["c"]])
 *   StandardValueFactory.ListTag([{ group: "g", name: "n" }])
 */
export const StandardValueFactory = {
  // String
  String: createString,

  // ListString
  ListString: createListString,
  ListStringFromItems: createListStringFromItems,

  // Decimal
  Decimal: createDecimal,
  DecimalFromString: createDecimalFromString,

  // Link
  Link: createLink,

  // Boolean
  Boolean: createBoolean,

  // DateTime
  DateTime: createDateTime,

  // Time
  Time: createTime,
  TimeFromMs: createTimeFromMs,
  TimeFromComponents: createTimeFromComponents,

  // ListListString
  ListListString: createListListString,

  // ListTag
  ListTag: createListTag,
  ListTagFromTuples: createListTagFromTuples,

  // Generic
  create,

  // Utilities
  isEmpty,
} as const;

export default StandardValueFactory;
