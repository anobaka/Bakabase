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
}

export interface TimelineSegment extends TimelineDomain {
  collected: boolean;
  point: boolean;
  left: number;
  width: number;
}

/** All targets share the same scale, including targets hidden by the search field. */
export const getTimelineDomain = (
  targets: CollectionMemoTarget[],
  now: number,
): TimelineDomain => ({
  start: targets.reduce(
    (earliest, target) =>
      target.ranges.reduce((start, range) => {
        const value = Date.parse(range.startAt);

        return Number.isFinite(value) ? Math.min(start, value) : start;
      }, earliest),
    now,
  ),
  end: now,
});

/** Merge overlaps for display only; the original records remain individually editable. */
export const getTimelineSegments = (
  ranges: CollectionMemoRange[],
  domain: TimelineDomain,
): TimelineSegment[] => {
  const spans = ranges
    .map((range) => ({ start: Date.parse(range.startAt), end: Date.parse(range.endAt) }))
    .filter(
      (range) =>
        Number.isFinite(range.start) &&
        Number.isFinite(range.end) &&
        range.start <= range.end &&
        range.start <= domain.end &&
        range.end >= domain.start,
    )
    .map((range) => ({
      start: Math.max(domain.start, range.start),
      end: Math.min(domain.end, range.end),
    }))
    .sort((a, b) => a.start - b.start || a.end - b.end);

  const merged: TimelineDomain[] = [];

  for (const span of spans) {
    const previous = merged[merged.length - 1];

    if (previous && span.start <= previous.end) {
      previous.end = Math.max(previous.end, span.end);
    } else {
      merged.push({ ...span });
    }
  }

  const result: TimelineSegment[] = [];
  const duration = domain.end - domain.start;
  const add = (start: number, end: number, collected: boolean) => {
    result.push({
      start,
      end,
      collected,
      point: collected && start === end,
      left: duration > 0 ? ((start - domain.start) / duration) * 100 : 100,
      width: duration > 0 ? ((end - start) / duration) * 100 : 0,
    });
  };
  let cursor = domain.start;

  for (const span of merged) {
    if (span.start > cursor) add(cursor, span.start, false);
    add(span.start, span.end, true);
    cursor = span.end;
  }
  if (cursor < domain.end || merged.length === 0) add(cursor, domain.end, false);

  return result;
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

/** Keep precision and the original DST-fold instant when the displayed field is unchanged. */
export const resolveRangeBoundary = (value: string, original?: string): string | undefined => {
  const converted = localDateTimeToIso(value);

  if (original && converted && converted === localDateTimeToIso(toLocalDateTimeInput(original))) {
    return original;
  }

  return converted;
};

/** The generated SDK returns application errors as successful promises. */
export const requireSuccess = <T extends { code?: number; message?: string | null }>(
  response: T,
): T => {
  if (response.code !== undefined && response.code !== 0) {
    throw new Error(response.message || String(response.code));
  }

  return response;
};
