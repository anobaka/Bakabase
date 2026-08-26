import { describe, expect, it } from "vitest";

import { filterItems, normalizeQuery, type SettingItem } from "../index";

const item = (id: string, label: string, tip?: string, keywords?: string[]): SettingItem => ({
  id,
  label,
  tip,
  keywords,
  render: () => null,
});

const items = [
  item("proxy", "Proxy", "Route requests through a proxy", ["network"]),
  item("tracking", "Anonymous data tracking", "Helps improve the app"),
  item("parallelism", "Max parallelism"),
];

describe("normalizeQuery", () => {
  it("trims and lowercases", () => {
    expect(normalizeQuery("  ProXY  ")).toBe("proxy");
  });
});

describe("filterItems", () => {
  it("returns everything for an empty query", () => {
    expect(filterItems("Others", items, "")).toHaveLength(3);
  });

  it("matches on the label", () => {
    const r = filterItems("Others", items, "parallel");

    expect(r.map((i) => i.id)).toEqual(["parallelism"]);
  });

  it("matches on the tip, not just the label", () => {
    const r = filterItems("Others", items, "improve");

    expect(r.map((i) => i.id)).toEqual(["tracking"]);
  });

  it("matches on keywords the label never shows", () => {
    const r = filterItems("Others", items, "network");

    expect(r.map((i) => i.id)).toEqual(["proxy"]);
  });

  it("keeps every row when the section title itself matches", () => {
    // Searching a section name should reveal the whole section rather than only
    // the rows that happen to repeat the word.
    expect(filterItems("Others", items, "others")).toHaveLength(3);
  });

  it("keeps every row when a section keyword matches", () => {
    expect(filterItems("Others", items, "misc", ["miscellaneous"])).toHaveLength(3);
  });

  it("returns nothing when there is no match", () => {
    expect(filterItems("Others", items, "zzzz")).toHaveLength(0);
  });

  it("ignores non-string labels when matching", () => {
    const withNode: SettingItem[] = [{ id: "n", label: null, render: () => null }];

    expect(filterItems("Others", withNode, "anything")).toHaveLength(0);
  });
});
