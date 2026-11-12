import { describe, expect, it } from "vitest";
import { eligibleSource, orderProviders, type SourceAvailabilityRow } from "../models";
const row: SourceAvailabilityRow = {
  key: "a",
  taskId: 1,
  partId: "part",
  sourceId: "source",
  url: "https://example.com",
  health: { status: "confirmedUnavailable" },
  disabled: false,
  inUse: false,
  actionable: true,
};
describe("source availability action selection", () => {
  it.each([
    "available",
    "accessRequired",
    "temporarilyUnavailable",
    "unknown",
    "unsupported",
    "unavailable",
  ])("never automatically disables %s", (status) =>
    expect(eligibleSource({ ...row, health: { status } }, "disable")).toBe(false),
  );
  it("requires explicit reliable evidence and server permission", () => {
    expect(eligibleSource(row, "disable")).toBe(true);
    expect(eligibleSource({ ...row, actionable: false }, "disable")).toBe(false);
    expect(eligibleSource({ ...row, disabled: true }, "disable")).toBe(false);
    expect(eligibleSource({ ...row, health: undefined }, "disable")).toBe(false);
  });
  it.each(["disable", "restore"] as const)("protects active sources against %s", (action) =>
    expect(eligibleSource({ ...row, inUse: true, disabled: action === "restore" }, action)).toBe(
      false,
    ),
  );
  it("restores only disabled eligible sources without claiming validity", () => {
    expect(
      eligibleSource({ ...row, disabled: true, health: { status: "unknown" } }, "restore"),
    ).toBe(true);
    expect(eligibleSource(row, "restore")).toBe(false);
  });
});
it("deduplicates catalog providers, drops unknown saved IDs, appends new providers", () => {
  expect(
    orderProviders(
      ["http", "baidu", "baidu", "pixeldrain", "magnet"],
      ["baidu", "obsolete", "http", "http"],
    ).map((provider) => provider.id),
  ).toEqual(["baidu", "http", "pixeldrain", "magnet"]);
});
