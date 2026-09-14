import { describe, expect, it } from "vitest";

import {
  choicesFromText,
  moveReferenceValue,
  sortReferenceValues,
  tagsFromText,
  tagText,
} from "../helpers";

describe("reference option editing", () => {
  it("moves by the IDs supplied by dnd-kit without mutating the original options", () => {
    const items = [{ value: "a", color: "red" }, { value: "b" }, { value: "c" }];
    const result = moveReferenceValue(items, "a", "c");

    expect(result.map((item) => item.value)).toEqual(["b", "c", "a"]);
    expect(result[2]).toBe(items[0]);
    expect(items.map((item) => item.value)).toEqual(["a", "b", "c"]);
  });

  it("ignores cancelled drops, missing IDs and drops onto the same option", () => {
    const items = [{ value: "a" }, { value: "b" }];

    expect(moveReferenceValue(items, "a")).toBe(items);
    expect(moveReferenceValue(items, "missing", "b")).toBe(items);
    expect(moveReferenceValue(items, "a", "a")).toBe(items);
  });

  it("sorts tags by their own group and name, including ungrouped tags", () => {
    const tags = [
      { value: "z", name: "Zebra" },
      { value: "b", group: "Genre", name: "Western" },
      { value: "a", group: "Genre", name: "Drama" },
      { value: "c", name: "Comedy" },
    ];

    expect(sortReferenceValues(tags, tagText).map((tag) => tag.value)).toEqual([
      "c",
      "a",
      "b",
      "z",
    ]);
    expect(tags[0].value).toBe("z");
  });

  it("retains saved choice IDs and settings while deduplicating bulk rows", () => {
    const choice = { value: "saved-choice", label: "Drama", color: "#ff0000", hide: true };
    const result = choicesFromText([choice], "Drama\nDrama\nComedy\n\n");

    expect(result).toHaveLength(2);
    expect(result[0]).toBe(choice);
    expect(result[1].label).toBe("Comedy");
    expect(result[1].value).not.toBe(choice.value);
    expect(result[1].value).toMatch(/^[a-f0-9-]{36}$/i);
  });

  it("preserves a saved tag with colons in its name and retains the remainder when parsing new tags", () => {
    const saved = { value: "tag", group: "Genre", name: "Part:One", color: "#f00", hide: true };
    const result = tagsFromText([saved], "Genre:Part:One\nGenre:Part:One\nGenre:Part:Two");

    expect(result).toHaveLength(2);
    expect(result[0]).toBe(saved);
    expect(result[1]).toMatchObject({ group: "Genre", name: "Part:Two" });
    expect(result[1].value).not.toBe(saved.value);
  });
});
