import { describe, expect, it } from "vitest";

import { intervalInput, intervalValue, mergeTaskOption } from "./schedule";

describe("background task schedules", () => {
  it("preserves durations longer than a clock day", () => {
    expect(intervalInput("2.00:00:00")).toEqual({ amount: "2", unit: "days" });
    expect(intervalValue("2", "days")).toBe("2.00:00:00");
    expect(intervalValue("25", "hours")).toBe("1.01:00:00");
  });
  it("uses readable units without rounding a stored interval", () => {
    expect(intervalInput("00:05:00")).toEqual({ amount: "5", unit: "minutes" });
    expect(intervalInput("00:01:15")).toEqual({ amount: "75", unit: "seconds" });
    expect(intervalValue("1.5", "minutes")).toBe("00:01:30");
  });
  it.each(["", "0", "-1", "NaN", "Infinity", "0.5", "922337203686"])(
    "refuses invalid whole-second intervals: %s",
    (amount) => {
      expect(intervalValue(amount, "seconds")).toBeUndefined();
    },
  );
  it("preserves saved settings for currently unregistered tasks", () => {
    const dormant = {
      id: "disabled-by-config",
      interval: "01:00:00",
      enableAfter: "2026-10-10T12:00:00",
    };
    const saved = [dormant, { id: "editing", interval: "00:01:00" }];
    const edited = { id: "editing", interval: "00:05:00" };

    expect(mergeTaskOption(saved, edited)).toEqual([dormant, edited]);
    expect(saved[1].interval).toBe("00:01:00");
  });
});
