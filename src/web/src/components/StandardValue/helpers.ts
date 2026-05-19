import type { Dayjs } from "dayjs";
import type { Duration } from "dayjs/plugin/duration";
import type { LinkValue, MultilevelData, TagValue } from "./models";
import type { StandardValueOf } from "@/components/Property/PropertySystem";

import dayjs from "dayjs";

import { StandardValueType } from "@/sdk/constants";
import {
  joinWithEscapeChar,
  splitStringWithEscapeChar,
} from "@/components/utils";

export const filterMultilevelData = <V>(
  data: MultilevelData<V>[],
  keyword: string,
): MultilevelData<V>[] => {
  if (!keyword) {
    return data;
  }

  const result: MultilevelData<V>[] = [];

  for (const d of data) {
    if (d.label?.toLowerCase().includes(keyword)) {
      result.push(d);
    } else {
      if (d.children && d.children.length > 0) {
        const children = filterMultilevelData(d.children, keyword);

        if (children.length > 0) {
          result.push({
            ...d,
            children,
          });
        }
      }
    }
  }

  return result;
};

export const findNodeChainInMultilevelData = <V>(
  data: MultilevelData<V>[],
  value: V,
): MultilevelData<V>[] | undefined => {
  for (const d of data) {
    if (d.value === value) {
      return [d];
    } else {
      if (d.children && d.children.length > 0) {
        const children = findNodeChainInMultilevelData(d.children, value);

        if (children !== undefined) {
          if (children?.length > 0) {
            return [d, ...children];
          }
        }
      }
    }
  }

  return;
};

/**
 * Parse a .NET TimeSpan-formatted string to milliseconds.
 * Accepts both "c" format ("[-][d.]hh:mm:ss[.fffffff]") and
 * "g" format ("[-][d:]h:mm:ss[.FFFFFFF]"), as well as a raw
 * numeric string of milliseconds.
 */
const parseTimeSpanToMs = (value: string): number | undefined => {
  const numeric = Number(value);

  if (Number.isFinite(numeric) && !value.includes(":")) {
    return numeric;
  }

  const match = value.match(
    /^(-)?(?:(\d+)[.:])?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/,
  );

  if (!match) {
    return undefined;
  }

  const sign = match[1] ? -1 : 1;
  const days = parseInt(match[2] ?? "0", 10);
  const hours = parseInt(match[3], 10);
  const minutes = parseInt(match[4], 10);
  const seconds = parseInt(match[5], 10);
  let ms = (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000;

  if (match[6]) {
    const padded = (match[6] + "000").slice(0, 3);

    ms += parseInt(padded, 10);
  }

  return sign * ms;
};

/**
 * Convert API value to the correct runtime type.
 * Use convertFromApiValueTyped for type-safe access.
 */
export const convertFromApiValue = (
  value: any | null,
  type: StandardValueType,
): any | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
    case StandardValueType.ListString:
    case StandardValueType.Decimal:
    case StandardValueType.Link:
    case StandardValueType.Boolean:
    case StandardValueType.ListListString:
    case StandardValueType.ListTag:
      return value;
    case StandardValueType.DateTime:
      return dayjs.isDayjs(value) ? value : dayjs(value);
    case StandardValueType.Time: {
      if (typeof (value as any)?.asMilliseconds === "function") {
        return value;
      }

      if (typeof value === "number") {
        return dayjs.duration(value);
      }

      if (typeof value === "string") {
        const ms = parseTimeSpanToMs(value);

        return ms == undefined ? undefined : dayjs.duration(ms);
      }

      return dayjs.duration(value);
    }
  }
};

/**
 * Type-safe version of convertFromApiValue.
 * Returns correctly typed value based on StandardValueType.
 */
export function convertFromApiValueTyped<T extends StandardValueType>(
  value: any | null,
  type: T,
): StandardValueOf<T> | undefined {
  return convertFromApiValue(value, type) as StandardValueOf<T> | undefined;
}

const Serialization = {
  LowLevelSeparator: ",",
  HighLevelSeparator: ";",
  EscapeChar: "\\",
};

/**
 * Deserialize a string to the correct runtime type.
 * Use deserializeStandardValueTyped for type-safe access.
 */
export const deserializeStandardValue = (
  value: string | null,
  type: StandardValueType,
): any | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
      return value;
    case StandardValueType.ListString:
      return splitStringWithEscapeChar(
        value,
        Serialization.LowLevelSeparator,
        Serialization.EscapeChar,
      );
    case StandardValueType.Decimal: {
      const d = parseFloat(value);

      if (Number.isNaN(d)) {
        return undefined;
      }

      return d;
    }
    case StandardValueType.Link: {
      const parts = splitStringWithEscapeChar(
        value,
        Serialization.LowLevelSeparator,
        Serialization.EscapeChar,
      );

      if (parts) {
        return {
          text: parts[0],
          url: parts[1],
        } as LinkValue;
      }

      return undefined;
    }
    case StandardValueType.Boolean: {
      if (value === "True") {
        return true;
      } else if (value === "False") {
        return false;
      }

      return undefined;
    }
    case StandardValueType.ListListString: {
      const parts = splitStringWithEscapeChar(
        value,
        Serialization.HighLevelSeparator,
        Serialization.EscapeChar,
      );

      if (parts) {
        return parts.map((p) =>
          splitStringWithEscapeChar(
            p,
            Serialization.LowLevelSeparator,
            Serialization.EscapeChar,
          ),
        );
      }

      return undefined;
    }
    case StandardValueType.ListTag: {
      const parts = splitStringWithEscapeChar(
        value,
        Serialization.HighLevelSeparator,
        Serialization.EscapeChar,
      );

      if (parts) {
        return parts
          .map((p) => {
            const tagSegments = splitStringWithEscapeChar(
              p,
              Serialization.LowLevelSeparator,
              Serialization.EscapeChar,
            );

            if (tagSegments) {
              return {
                group: tagSegments[0],
                name: tagSegments[1],
              };
            }

            return null;
          })
          .filter((p) => p != null);
      }

      return undefined;
    }
    case StandardValueType.DateTime: {
      return dayjs(parseInt(value, 10));
    }
    case StandardValueType.Time:
      return dayjs.duration(parseInt(value, 10));
  }
};

/**
 * Type-safe version of deserializeStandardValue.
 * Returns correctly typed value based on StandardValueType.
 */
export function deserializeStandardValueTyped<T extends StandardValueType>(
  value: string | null,
  type: T,
): StandardValueOf<T> | undefined {
  return deserializeStandardValue(value, type) as StandardValueOf<T> | undefined;
}

/**
 * Serialize a value to string representation.
 * Use serializeStandardValueTyped for type-safe input.
 */
export const serializeStandardValue = (
  value: any | null,
  type: StandardValueType,
): string | undefined => {
  if (value == undefined) {
    return undefined;
  }

  switch (type) {
    case StandardValueType.String:
      return value as string;
    case StandardValueType.ListString:
      return joinWithEscapeChar(
        value as string[],
        Serialization.LowLevelSeparator,
        Serialization.EscapeChar,
      );
    case StandardValueType.Decimal:
      return value.toString();
    case StandardValueType.Link: {
      const lv = value as LinkValue;

      if (!lv) {
        return undefined;
      }

      return joinWithEscapeChar(
        [lv.text, lv.url],
        Serialization.LowLevelSeparator,
        Serialization.EscapeChar,
      );
    }
    case StandardValueType.Boolean: {
      return value ? "True" : "False";
    }
    case StandardValueType.ListListString: {
      return joinWithEscapeChar(
        (value as string[][]).map((p) =>
          joinWithEscapeChar(
            p,
            Serialization.LowLevelSeparator,
            Serialization.EscapeChar,
          ),
        ),
        Serialization.HighLevelSeparator,
        Serialization.EscapeChar,
      );
    }
    case StandardValueType.ListTag: {
      const tvs = value as TagValue[];

      if (!tvs) {
        return undefined;
      }

      return joinWithEscapeChar(
        tvs.map((tv) =>
          joinWithEscapeChar(
            [tv.group, tv.name],
            Serialization.LowLevelSeparator,
            Serialization.EscapeChar,
          ),
        ),
        Serialization.HighLevelSeparator,
        Serialization.EscapeChar,
      );
    }
    case StandardValueType.DateTime: {
      const dt = value as Dayjs;

      return dt.valueOf().toString();
    }
    case StandardValueType.Time: {
      // Value may arrive already as a Duration, but during edits/roundtrips it
      // can also reach us as a number (ms), a parseable string, or a moment
      // Duration shape — normalise before serialising so we never blow up on
      // `dur.asMilliseconds is not a function`.
      if (typeof (value as any)?.asMilliseconds === "function") {
        return ((value as any).asMilliseconds() as number).toString();
      }

      if (typeof value === "number") {
        return value.toString();
      }

      if (typeof value === "string") {
        const ms = parseTimeSpanToMs(value);
        return ms == undefined ? undefined : ms.toString();
      }

      return undefined;
    }
  }
};

/**
 * Type-safe version of serializeStandardValue.
 * Ensures type-safe input value based on StandardValueType.
 */
export function serializeStandardValueTyped<T extends StandardValueType>(
  value: StandardValueOf<T> | null | undefined,
  type: T,
): string | undefined {
  return serializeStandardValue(value, type);
}
