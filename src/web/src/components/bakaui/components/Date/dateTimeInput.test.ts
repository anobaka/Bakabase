import { describe, expect, it } from "vitest";

import { formatDateTimeInput, parseDateTimeInput } from "./dateTimeInput";

import { getTimestampTicks, resolveRangeBoundary } from "@/pages/collection-memo/helpers";

describe("plain text date-time parsing", () => {
  it.each([
    "2026-09-05 16:00:35",
    "2026-9-5 16:00:35",
    "2026/9/5 16:00:35",
    "2026.9.5 16:00:35",
    "2026-09-05T16:00:35",
    "2026年9月5日 16:00:35",
    "2026年9月5日16时00分35秒",
    "2026年9月5日 16时0分35秒",
    "2026/9/5 16：00：35",
    "20260905160035",
    "  2026-09-05 16:00:35  ",
  ])("recognizes complete common local input %s", (value) => {
    expect(parseDateTimeInput(value)).toEqual({
      iso: new Date(2026, 8, 5, 16, 0, 35).toISOString(),
      hasExplicitTimezone: false,
    });
  });

  it.each(["2026-9-5", "2026/09/05", "2026.9.05", "2026年9月5日", "20260905"])(
    "treats the date %s as local midnight",
    (value) => {
      expect(parseDateTimeInput(value)).toEqual({
        iso: new Date(2026, 8, 5, 0, 0, 0).toISOString(),
        hasExplicitTimezone: false,
      });
    },
  );

  it.each(["2026-9-5 9:03", "2026年9月5日9时3分", "202609050903"])(
    "defaults omitted seconds to zero for %s",
    (value) => {
      expect(parseDateTimeInput(value)?.iso).toBe(new Date(2026, 8, 5, 9, 3).toISOString());
    },
  );

  it("accepts leap days and future instants without imposing a feature-specific cutoff", () => {
    expect(parseDateTimeInput("2024/2/29 12:34")?.iso).toBe(
      new Date(2024, 1, 29, 12, 34).toISOString(),
    );
    expect(parseDateTimeInput("2060-12-31T23:59:59Z")?.iso).toBe("2060-12-31T23:59:59.000Z");
  });

  it.each([
    ["2026-09-05T16:00:35Z", "2026-09-05T16:00:35.000Z"],
    ["2026-09-05 16:00:35+08:00", "2026-09-05T08:00:35.000Z"],
    ["2026-09-05 16:00:35+0800", "2026-09-05T08:00:35.000Z"],
    ["2026/9/5 16:00 UTC+08:00", "2026-09-05T08:00:00.000Z"],
    ["2026/9/5 16:00 UTC+0800", "2026-09-05T08:00:00.000Z"],
    ["2026-09-05 16:00:35 UTC", "2026-09-05T16:00:35.000Z"],
    ["2026-09-05 16:00:35-03:30", "2026-09-05T19:30:35.000Z"],
    ["2026-09-05T16:00:35-00:00", "2026-09-05T16:00:35.000Z"],
    ["2024-03-01 00:15+08:00", "2024-02-29T16:15:00.000Z"],
    ["2026-09-05T16:00:35.1234567+08:00", "2026-09-05T08:00:35.1234567Z"],
    ["2026-09-05T16:00:35.1Z", "2026-09-05T16:00:35.1Z"],
    ["2026年9月5日16时0分35.0000001秒 UTC+08:00", "2026-09-05T08:00:35.0000001Z"],
    ["0001-01-02T00:00Z", "0001-01-02T00:00:00.000Z"],
    ["0099-01-02T00:00Z", "0099-01-02T00:00:00.000Z"],
    ["9999-12-31T23:59:59.9999999Z", "9999-12-31T23:59:59.9999999Z"],
  ])("honors the explicit timezone in %s", (value, iso) => {
    expect(parseDateTimeInput(value)).toEqual({ iso, hasExplicitTimezone: true });
  });

  it("retains all seven fractional digits when converting local input", () => {
    const whole = new Date(2026, 8, 5, 16, 0, 35).toISOString();

    expect(parseDateTimeInput("2026-9-5 16:00:35.1234567")?.iso).toBe(
      whole.replace(".000Z", ".1234567Z"),
    );
    expect(parseDateTimeInput("2026年9月5日16时0分35.0000001秒")?.iso).toBe(
      whole.replace(".000Z", ".0000001Z"),
    );
  });

  it.each([
    "",
    "2026",
    "2026-09",
    "2026-09-05T",
    "2026-09-05 16",
    "2026-09-05 16:",
    "2026-09-05 16:00:",
    "2026-09-05 16:00:35.",
    "2026-09-05 16:00:35.12345678",
    "2026-09-05TT16:00",
    "2026-09/05 16:00",
    "09/05/2026 16:00",
    "09/05/26",
    "16:00",
    "October 1, 2026",
    "today",
    "1725494435000",
    "2026-02-29",
    "2026-02-30T16:00",
    "2024-04-31",
    "2026-13-01",
    "2026-00-01",
    "2026-09-00",
    "2026-09-05 24:00",
    "2026-09-05 16:60",
    "2026-09-05 16:00:60",
    "2026年9月5日25时",
    "2026年9月5日16时60分",
    "2026-09-05 16:00 PM",
    "0000-09-05 16:00",
    "10000-09-05 16:00",
    "2026-09-05Z",
    "2026-09-05 16:00+24:00",
    "2026-09-05 16:00+15:00",
    "2026-09-05 16:00+14:01",
    "2026-09-05 16:00+08:60",
    "2026-09-05 16:00 extra",
    "0001-01-01 00:00+14:00",
    "9999-12-31 23:59-14:00",
  ])("rejects incomplete, ambiguous, or invalid input %s", (value) => {
    expect(parseDateTimeInput(value)).toBeUndefined();
  });
});

describe("date-time text display and edit preservation", () => {
  it("keeps valid UTC year boundaries editable when the local year is outside the supported range", () => {
    for (const iso of ["0001-01-01T00:00:00.0000001Z", "9999-12-31T23:59:59.9999999Z"]) {
      expect(getTimestampTicks(resolveRangeBoundary(formatDateTimeInput(iso), iso)!)).toBe(
        getTimestampTicks(iso),
      );
    }
  });

  it("formats numeric instants as local whole seconds and retains string fractions", () => {
    const date = new Date(2026, 8, 5, 16, 0, 35, 123);
    const original = date.toISOString().replace(".123Z", ".1234567Z");

    expect(formatDateTimeInput(date.getTime())).toBe("2026-09-05 16:00:35");
    expect(formatDateTimeInput(original)).toBe("2026-09-05 16:00:35.1234567");
    expect(formatDateTimeInput("2026.9.05")).toBe("2026-09-05 00:00:00");
    expect(formatDateTimeInput("2026年9月5日16时0分35.1234567秒")).toBe(
      "2026-09-05 16:00:35.1234567",
    );
    expect(formatDateTimeInput("invalid")).toBe("");
    expect(formatDateTimeInput(Number.NaN)).toBe("");
  });

  it("preserves exact original local-equivalent values while respecting changed fractions", () => {
    const original = new Date(2026, 8, 5, 16, 0, 35, 123)
      .toISOString()
      .replace(".123Z", ".1234567Z");

    expect(resolveRangeBoundary(formatDateTimeInput(original), original)).toBe(original);
    expect(resolveRangeBoundary("2026/9/5 16:00:35.1234567", original)).toBe(original);
    const changed = resolveRangeBoundary("2026/9/5 16:00:35.000", original)!;

    expect(getTimestampTicks(original)! - getTimestampTicks(changed)!).toBe(1_234_567n);
    expect(resolveRangeBoundary("2026-9-5 16:00:35.1234568", original)).not.toBe(original);
  });

  it("always honors explicit timezone and fraction choices", () => {
    const original = "2026-09-05T08:00:35.1234567Z";

    expect(resolveRangeBoundary("2026-09-05T08:00:35.000Z", original)).toBe(
      "2026-09-05T08:00:35.000Z",
    );
    expect(resolveRangeBoundary("2026-09-05 16:00:35.000+08:00", original)).toBe(
      "2026-09-05T08:00:35.000Z",
    );
  });
});

// Run this file with TZ=America/New_York to exercise DST without mutating shared worker state.
describe.skipIf(Intl.DateTimeFormat().resolvedOptions().timeZone !== "America/New_York")(
  "daylight-saving date-time input",
  () => {
    it("rejects the spring-forward gap and accepts a specified fixed-offset instant", () => {
      expect(parseDateTimeInput("2025-03-09 02:30")).toBeUndefined();
      expect(parseDateTimeInput("2025-03-09 01:30")?.iso).toBe("2025-03-09T06:30:00.000Z");
      expect(parseDateTimeInput("2025-03-09 03:30")?.iso).toBe("2025-03-09T07:30:00.000Z");
      expect(parseDateTimeInput("2025-03-09 02:30-05:00")?.iso).toBe("2025-03-09T07:30:00.000Z");
    });

    it("preserves an unchanged later-fold instant and all seven fractional digits", () => {
      const laterFold = "2025-11-02T06:30:35.1234567Z";

      expect(formatDateTimeInput(laterFold)).toBe("2025-11-02 01:30:35.1234567");
      expect(resolveRangeBoundary(formatDateTimeInput(laterFold), laterFold)).toBe(laterFold);
      expect(resolveRangeBoundary("2025年11月2日1时30分35.1234567秒", laterFold)).toBe(laterFold);
    });

    it("honors an explicit earlier-fold instant even when its wall clock matches the original", () => {
      const laterFold = "2025-11-02T06:30:35.1234567Z";

      expect(resolveRangeBoundary("2025-11-02T05:30:35Z", laterFold)).toBe(
        "2025-11-02T05:30:35.000Z",
      );
      expect(resolveRangeBoundary("2025-11-02 01:30:35.1234567-04:00", laterFold)).toBe(
        "2025-11-02T05:30:35.1234567Z",
      );
    });
  },
);
