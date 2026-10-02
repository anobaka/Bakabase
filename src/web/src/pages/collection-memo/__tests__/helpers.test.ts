import { describe, expect, it } from "vitest";

import {
  getTimelineDomain,
  getTimelineSegments,
  localDateTimeToIso,
  requireSuccess,
  resolveRangeBoundary,
  toLocalDateTimeInput,
} from "../helpers";

import { formatDateTimeInput } from "@/components/bakaui/components/Date/dateTimeInput";

const range = (id: number, start: number, end: number) => ({
  id,
  startAt: new Date(start).toISOString(),
  endAt: new Date(end).toISOString(),
});

describe("collection memo timeline", () => {
  it("uses the earliest date across every target, with now as the right endpoint", () => {
    expect(
      getTimelineDomain(
        [
          { id: 1, name: "First", ranges: [range(1, 100, 200)] },
          { id: 2, name: "Earlier", ranges: [range(2, 20, 30)] },
          { id: 3, name: "Empty", ranges: [] },
        ],
        300,
      ),
    ).toEqual({ start: 20, end: 300 });
    expect(getTimelineDomain([], 300)).toEqual({ start: 300, end: 300 });
  });

  it("merges overlaps and adjoining spans without double counting, and preserves gray gaps", () => {
    const records = [range(1, 20, 40), range(2, 30, 50), range(3, 50, 60), range(4, 80, 90)];

    expect(getTimelineSegments(records, { start: 0, end: 100 })).toEqual([
      { start: 0, end: 20, collected: false, point: false, left: 0, width: 20 },
      { start: 20, end: 60, collected: true, point: false, left: 20, width: 40 },
      { start: 60, end: 80, collected: false, point: false, left: 60, width: 20 },
      { start: 80, end: 90, collected: true, point: false, left: 80, width: 10 },
      { start: 90, end: 100, collected: false, point: false, left: 90, width: 10 },
    ]);
    expect(records).toHaveLength(4);
    expect(records[0].endAt).toBe(new Date(40).toISOString());
  });

  it("shows isolated points without coloring surrounding time as collected", () => {
    expect(
      getTimelineSegments([range(1, 20, 20), range(2, 100, 100)], { start: 0, end: 100 }),
    ).toEqual([
      { start: 0, end: 20, collected: false, point: false, left: 0, width: 20 },
      { start: 20, end: 20, collected: true, point: true, left: 20, width: 0 },
      { start: 20, end: 100, collected: false, point: false, left: 20, width: 80 },
      { start: 100, end: 100, collected: true, point: true, left: 100, width: 0 },
    ]);
    expect(getTimelineSegments([range(1, 100, 100)], { start: 100, end: 100 })).toEqual([
      { start: 100, end: 100, collected: true, point: true, left: 100, width: 0 },
    ]);
  });

  it("shows empty targets as uncollected across the shared domain", () => {
    expect(getTimelineSegments([], { start: 0, end: 100 })).toEqual([
      { start: 0, end: 100, collected: false, point: false, left: 0, width: 100 },
    ]);
    expect(getTimelineSegments([], { start: 100, end: 100 })[0].width).toBe(0);
  });

  it("clips records to the visible dates and excludes invalid records", () => {
    expect(
      getTimelineSegments(
        [range(1, -20, 10), range(2, 80, 120), range(3, 150, 180), range(4, 70, 60)],
        { start: 0, end: 100 },
      ).filter((segment) => segment.collected),
    ).toMatchObject([
      { start: 0, end: 10 },
      { start: 80, end: 100 },
    ]);
  });
});

describe("collection memo date-time conversion", () => {
  it("converts local input to an explicit UTC ISO timestamp without changing seconds", () => {
    const local = "2026-09-05T16:00:35";
    const expected = new Date(2026, 8, 5, 16, 0, 35).toISOString();

    expect(localDateTimeToIso(local)).toBe(expected);
    expect(localDateTimeToIso(`${local}.000`)).toBe(expected);
    expect(toLocalDateTimeInput(expected)).toBe(local);
    expect(localDateTimeToIso("2026-09-05T16:00")).toBe(new Date(2026, 8, 5, 16, 0).toISOString());
  });

  it("accepts timezone-bearing server timestamps and shows their equivalent local time", () => {
    const serverTime = "2026-09-05T16:00:35+08:00";

    expect(localDateTimeToIso(toLocalDateTimeInput(serverTime))).toBe(
      new Date(serverTime).toISOString(),
    );
  });

  it("preserves unchanged fractional precision and DST-fold timestamps", () => {
    // In America/New_York this is the second 01:30, which local Date parsing
    // would otherwise reconstruct as the first 01:30, one hour earlier.
    const original = "2025-11-02T06:30:35.1234567Z";
    const visible = formatDateTimeInput(original);

    expect(resolveRangeBoundary(visible, original)).toBe(original);
    const wholeSeconds = visible.replace(/\.\d+$/, ".000");

    expect(resolveRangeBoundary(wholeSeconds, original)).not.toBe(original);
    const changed = "2025-11-02T03:30:35";

    expect(resolveRangeBoundary(changed, original)).toBe(localDateTimeToIso(changed));
  });

  it.each([
    "",
    "2026-02-30T16:00",
    "2026-09-05T24:00",
    "2026-09-05T16:60",
    "2026-09-05T16:00:60",
    "2026-09-05",
    "0000-09-05T16:00",
  ])("rejects invalid local input %s", (value) =>
    expect(localDateTimeToIso(value)).toBeUndefined(),
  );
});

it("rejects application error responses so failed saves cannot close their dialog", () => {
  expect(() => requireSuccess({ code: 400, message: "Duplicate target" })).toThrow(
    "Duplicate target",
  );
  expect(requireSuccess({ code: 0, data: [] })).toEqual({ code: 0, data: [] });
});
