import {
  formatDateTimeInput,
  parseDateTimeInput,
} from "@/components/bakaui/components/Date/dateTimeInput";

export interface CollectionMemoRange {
  id: number;
  startAt: string;
  endAt: string;
}

export interface CollectionMemoTarget {
  id: number;
  name: string;
  ranges: CollectionMemoRange[];
}

export interface TimelineDomain {
  start: number;
  end: number;
  /** Exact global start when its precision exceeds the millisecond pixel scale. */
  startAt?: string;
}

export interface TimelineSegment extends TimelineDomain {
  collected: boolean;
  point: boolean;
  left: number;
  width: number;
}

export interface TimelineCoverage extends TimelineDomain {
  startAt: string;
  endAt: string;
  ranges: CollectionMemoRange[];
}

export interface TimelineRegion extends TimelineSegment {
  startAt: string;
  endAt: string;
}

export interface CollectionMemoCoverageResize {
  ranges: CollectionMemoRange[];
  edge: "start" | "end";
  at: string;
}

/** Date.parse supplies milliseconds; the remaining four digits preserve .NET ticks. */
export const getTimestampTicks = (value: string): bigint | undefined => {
  const milliseconds = Date.parse(value);

  if (!Number.isFinite(milliseconds)) return undefined;

  const fraction = /\.(\d+)(?:Z|[+-]\d{2}:?\d{2})$/i.exec(value)?.[1] ?? "";

  return BigInt(milliseconds) * 10_000n + BigInt(fraction.slice(3, 7).padEnd(4, "0"));
};

export const getTimelineCoverage = (ranges: CollectionMemoRange[]): TimelineCoverage[] => {
  const valid = ranges
    .map((range) => ({
      range,
      start: getTimestampTicks(range.startAt),
      end: getTimestampTicks(range.endAt),
    }))
    .filter(
      (item): item is { range: CollectionMemoRange; start: bigint; end: bigint } =>
        item.start !== undefined && item.end !== undefined && item.start <= item.end,
    )
    .sort((a, b) => (a.start < b.start ? -1 : a.start > b.start ? 1 : a.range.id - b.range.id));
  const result: TimelineCoverage[] = [];

  for (const item of valid) {
    const previous = result[result.length - 1];

    if (previous && item.start <= getTimestampTicks(previous.endAt)!) {
      previous.ranges.push({ ...item.range });
      if (item.end > getTimestampTicks(previous.endAt)!) {
        previous.endAt = item.range.endAt;
        previous.end = Date.parse(item.range.endAt);
      }
    } else {
      result.push({
        start: Date.parse(item.range.startAt),
        end: Date.parse(item.range.endAt),
        startAt: item.range.startAt,
        endAt: item.range.endAt,
        ranges: [{ ...item.range }],
      });
    }
  }

  return result;
};

export const getTimelineRegions = (
  coverage: TimelineCoverage[],
  domain: TimelineDomain,
): TimelineRegion[] => {
  const result: TimelineRegion[] = [];
  const duration = domain.end - domain.start;
  const domainStartAt = domain.startAt ?? new Date(domain.start).toISOString();
  const domainEndAt = new Date(domain.end).toISOString();
  const add = (startAt: string, endAt: string, collected: boolean) => {
    const start = Date.parse(startAt);
    const end = Date.parse(endAt);

    result.push({
      start,
      end,
      startAt,
      endAt,
      collected,
      point: collected && getTimestampTicks(startAt) === getTimestampTicks(endAt),
      left: duration > 0 ? ((start - domain.start) / duration) * 100 : 100,
      width: duration > 0 ? ((end - start) / duration) * 100 : 0,
    });
  };
  let cursorAt = domainStartAt;

  for (const component of coverage) {
    if (
      getTimestampTicks(component.endAt)! < getTimestampTicks(domainStartAt)! ||
      getTimestampTicks(component.startAt)! > getTimestampTicks(domainEndAt)!
    )
      continue;

    const startAt =
      getTimestampTicks(component.startAt)! < getTimestampTicks(domainStartAt)!
        ? domainStartAt
        : component.startAt;
    const endAt =
      getTimestampTicks(component.endAt)! > getTimestampTicks(domainEndAt)!
        ? domainEndAt
        : component.endAt;

    if (getTimestampTicks(startAt)! > getTimestampTicks(cursorAt)!) add(cursorAt, startAt, false);
    add(startAt, endAt, true);
    cursorAt = endAt;
  }
  if (getTimestampTicks(cursorAt)! < getTimestampTicks(domainEndAt)! || result.length === 0)
    add(cursorAt, domainEndAt, false);

  return result;
};

export const getCoverageResizeBounds = (
  coverage: TimelineCoverage[],
  index: number,
  domain: TimelineDomain,
  edge: "start" | "end",
): { min: string; max: string } => {
  const component = coverage[index];
  const domainStart = domain.startAt ?? new Date(domain.start).toISOString();
  const domainEnd = new Date(domain.end).toISOString();
  const previousEnd = coverage[index - 1]?.endAt;
  const nextStart = coverage[index + 1]?.startAt;

  return edge === "start"
    ? {
        min:
          previousEnd && getTimestampTicks(previousEnd)! > getTimestampTicks(domainStart)!
            ? previousEnd
            : domainStart,
        max: component.endAt,
      }
    : {
        min: component.startAt,
        max:
          nextStart && getTimestampTicks(nextStart)! < getTimestampTicks(domainEnd)!
            ? nextStart
            : domainEnd,
      };
};

export const clampCoverageBoundary = (
  value: number,
  bounds: { min: string; max: string },
): string => {
  const candidate = new Date(value).toISOString();

  if (getTimestampTicks(candidate)! <= getTimestampTicks(bounds.min)!) return bounds.min;
  if (getTimestampTicks(candidate)! >= getTimestampTicks(bounds.max)!) return bounds.max;

  return candidate;
};

/** All targets share the same scale, including targets hidden by the search field. */
export const getTimelineDomain = (targets: CollectionMemoTarget[], now: number): TimelineDomain => {
  let startAt = new Date(now).toISOString();
  let earliest = getTimestampTicks(startAt)!;

  for (const target of targets) {
    for (const range of target.ranges) {
      const ticks = getTimestampTicks(range.startAt);

      if (ticks !== undefined && ticks < earliest) {
        earliest = ticks;
        startAt = range.startAt;
      }
    }
  }
  const start = Date.parse(startAt);
  const hasExtraPrecision = earliest !== getTimestampTicks(new Date(start).toISOString());

  return { start, end: now, ...(hasExtraPrecision ? { startAt } : {}) };
};

/** Merge overlaps for display only; the original records remain individually editable. */
export const getTimelineSegments = (
  ranges: CollectionMemoRange[],
  domain: TimelineDomain,
): TimelineSegment[] => {
  return getTimelineRegions(getTimelineCoverage(ranges), domain).map(
    ({ start, end, collected, point, left, width }) => ({
      start,
      end,
      collected,
      point,
      left,
      width,
    }),
  );
};

const pad = (value: number) => String(value).padStart(2, "0");

export const toLocalDateTimeInput = (value: string | number): string => {
  const date = new Date(value);

  if (!Number.isFinite(date.getTime())) return "";

  return `${String(date.getFullYear()).padStart(4, "0")}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
};

/** Reject nonexistent dates (including daylight-saving gaps) instead of silently normalizing. */
export const localDateTimeToIso = (value: string): string | undefined => {
  // Native datetime-local controls may normalize whole seconds to ".000".
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,3}))?)?$/.exec(
    value,
  );

  if (!match) return undefined;

  const [, year, month, day, hour, minute, second = "0", fraction = "0"] = match;
  const parts = [year, month, day, hour, minute, second].map(Number);

  if (parts[0] < 1) return undefined;

  const date = new Date(0);

  date.setFullYear(parts[0], parts[1] - 1, parts[2]);
  date.setHours(parts[3], parts[4], parts[5], Number(fraction.padEnd(3, "0")));

  if (
    date.getFullYear() !== parts[0] ||
    date.getMonth() !== parts[1] - 1 ||
    date.getDate() !== parts[2] ||
    date.getHours() !== parts[3] ||
    date.getMinutes() !== parts[4] ||
    date.getSeconds() !== parts[5]
  ) {
    return undefined;
  }

  return date.toISOString();
};

/** Keep the original DST-fold instant only for an equivalent local field value. */
export const resolveRangeBoundary = (value: string, original?: string): string | undefined => {
  const parsed = parseDateTimeInput(value);

  if (parsed && original && !parsed.hasExplicitTimezone) {
    const displayedOriginal = parseDateTimeInput(formatDateTimeInput(original));

    if (
      displayedOriginal &&
      getTimestampTicks(parsed.iso) === getTimestampTicks(displayedOriginal.iso)
    )
      return original;
  }

  return parsed?.iso;
};

/** The generated SDK returns application errors as successful promises. */
export const requireSuccess = <T extends { code?: number; message?: string | null }>(
  response: T,
): T => {
  if (response.code !== undefined && response.code !== 0) {
    throw Object.assign(new Error(response.message || String(response.code)), {
      code: response.code,
    });
  }

  return response;
};
