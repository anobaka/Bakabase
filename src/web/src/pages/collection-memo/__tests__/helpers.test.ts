import { describe, expect, it } from "vitest";

import {
  getTimelineDomain,
  getCollectionMemoRangeUrl,
  getTimelineCoverage,
  getTimelineRegions,
  getCoverageResizeBounds,
  getTimestampTicks,
  getTimelineSegments,
  localDateTimeToIso,
  requireSuccess,
  resolveRangeBoundary,
  resolveCollectionMemoRangeStart,
  toLocalDateTimeInput,
} from "../helpers";

import { formatDateTimeInput } from "@/components/bakaui/components/Date/dateTimeInput";

const range = (id: number, start: number, end: number) => ({
  id,
  startAt: new Date(start).toISOString(),
  endAt: new Date(end).toISOString(),
});

describe("collection memo range links", () => {
  it.each([
    "https://example.com/path?q=test#source",
    "http://example.com/%20",
    "https://例子.测试/来源",
  ])("preserves a valid external link: %s", (url) =>
    expect(getCollectionMemoRangeUrl(`  ${url}  `)).toBe(url),
  );

  it.each([
    null,
    "",
    "not a URL",
    "javascript:alert(1)",
    "https://user:pass@example.com",
    "http:///example.com",
    "https://example.com/a b",
    "https://example.com/%no",
    "https://example.com\\path",
  ])("rejects a missing or malformed link: %s", (url) =>
    expect(getCollectionMemoRangeUrl(url)).toBeUndefined(),
  );

  it("retains each original record's metadata when coverage is combined", () => {
    const records = [
      { ...range(1, 20, 50), url: "https://example.com/one", note: "First" },
      { ...range(2, 40, 60), url: "https://example.com/two", note: "Second" },
    ];

    expect(getTimelineCoverage(records)[0].ranges).toEqual(records);
  });
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

  it("uses the configured exact global start even when explicit records are earlier or targets empty", () => {
    const globalStartAt = "2026-09-01T00:00:00.1234567Z";
    const now = Date.parse("2026-09-02T00:00:00Z");
    const expected = { start: Date.parse(globalStartAt), end: now, startAt: globalStartAt };

    expect(
      getTimelineDomain([{ id: 1, name: "Old", ranges: [range(1, 0, 100)] }], now, globalStartAt),
    ).toEqual(expected);
    expect(getTimelineDomain([], now, globalStartAt)).toEqual(expected);
    expect(getTimelineDomain([], now, "2026-09-01T08:00:00+08:00")).toEqual({
      start: Date.parse("2026-09-01T00:00:00Z"),
      end: now,
      startAt: "2026-09-01T08:00:00+08:00",
    });
  });

  it("resolves inherited ranges dynamically while preserving nullable raw snapshots and precision", () => {
    const startAt = "2026-09-01T00:00:00.1234567Z";
    const endAt = "2026-09-01T00:00:00.1234568Z";
    const records = [{ id: 1, startAt: null, endAt }];
    const coverage = getTimelineCoverage(records, startAt);

    expect(resolveCollectionMemoRangeStart(records[0], startAt)).toBe(startAt);
    expect(resolveCollectionMemoRangeStart(records[0])).toBeUndefined();
    expect(coverage[0]).toMatchObject({ startAt, endAt, ranges: records });
    expect(coverage[0].ranges[0]).not.toBe(records[0]);
    expect(coverage[0].ranges[0].startAt).toBeNull();
    expect(getTimestampTicks(coverage[0].endAt)! - getTimestampTicks(coverage[0].startAt)!).toBe(
      1n,
    );
    expect(getTimelineCoverage(records, endAt)[0].startAt).toBe(endAt);
    expect(getTimelineCoverage(records)).toEqual([]);
    expect(records[0].startAt).toBeNull();
  });

  it("merges inherited and explicit records using effective starts without rewriting raw endpoints", () => {
    const records = [{ id: 1, startAt: null, endAt: new Date(40).toISOString() }, range(2, 30, 60)];
    const coverage = getTimelineCoverage(records, new Date(20).toISOString());

    expect(coverage).toHaveLength(1);
    expect(coverage[0]).toMatchObject({ start: 20, end: 60, ranges: records });
    expect(coverage[0].ranges[0].startAt).toBeNull();
    expect(getTimelineCoverage(records, new Date(70).toISOString())).toHaveLength(1);
    expect(getTimelineCoverage(records, new Date(70).toISOString())[0].ranges).toEqual([
      records[1],
    ]);
  });

  it("clips earlier explicit ranges without changing stored endpoints and bounds the visible end at global start", () => {
    const records = [range(1, 0, 40), range(2, 0, 10)];
    const configuredDomain = getTimelineDomain([], 100, new Date(20).toISOString());
    const coverage = getTimelineCoverage(records);

    expect(getTimelineRegions(coverage, configuredDomain)).toMatchObject([
      { start: 20, end: 40, collected: true },
      { start: 40, end: 100, collected: false },
    ]);
    expect(getCoverageResizeBounds(coverage, 0, configuredDomain, "end").min).toBe(
      new Date(20).toISOString(),
    );
    expect(coverage[0].startAt).toBe(new Date(0).toISOString());
    expect(records[0].startAt).toBe(new Date(0).toISOString());
  });

  it("mirrors visible positions while leaving chronological endpoints and point widths intact", () => {
    const records = [range(1, 20, 40), range(2, 80, 80)];
    const forward = getTimelineSegments(records, { start: 0, end: 100 });
    const reversed = getTimelineSegments(records, { start: 0, end: 100 }, true);

    expect(reversed).toEqual(
      forward.map((segment) => ({ ...segment, left: 100 - segment.left - segment.width })),
    );
    expect(getTimelineSegments([], { start: 0, end: 100 }, true)).toEqual([
      { start: 0, end: 100, collected: false, point: false, left: 0, width: 100 },
    ]);
    expect(getTimelineSegments([range(1, 100, 100)], { start: 100, end: 100 }, true)).toEqual([
      { start: 100, end: 100, collected: true, point: true, left: 0, width: 0 },
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
