import { describe, expect, it } from "vitest";

import type { MultilevelData } from "@/components/StandardValue/models";

import {
  collectSubtreeValues,
  deserializeStandardValue,
  findNodeChainByLabels,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";
import { StandardValueType } from "@/sdk/constants";

describe("deserializeStandardValue - Boolean", () => {
  it("accepts backend bool.ToString() forms", () => {
    expect(deserializeStandardValue("True", StandardValueType.Boolean)).toBe(true);
    expect(deserializeStandardValue("False", StandardValueType.Boolean)).toBe(false);
  });

  it("accepts lowercase and 1/0 forms", () => {
    expect(deserializeStandardValue("true", StandardValueType.Boolean)).toBe(true);
    expect(deserializeStandardValue("false", StandardValueType.Boolean)).toBe(false);
    expect(deserializeStandardValue("1", StandardValueType.Boolean)).toBe(true);
    expect(deserializeStandardValue("0", StandardValueType.Boolean)).toBe(false);
  });

  it("returns undefined for garbage", () => {
    expect(deserializeStandardValue("yes", StandardValueType.Boolean)).toBeUndefined();
    expect(deserializeStandardValue("2", StandardValueType.Boolean)).toBeUndefined();
  });

  it("round-trips through serializeStandardValue", () => {
    const serialized = serializeStandardValue(true, StandardValueType.Boolean);

    expect(serialized).toBe("True");
    expect(deserializeStandardValue(serialized!, StandardValueType.Boolean)).toBe(true);
  });
});

describe("deserializeStandardValue - ListTag", () => {
  it("parses well-formed group,name entries", () => {
    const tags = deserializeStandardValue("G,N;,NoGroup", StandardValueType.ListTag);

    expect(tags).toEqual([
      { group: "G", name: "N" },
      { group: undefined, name: "NoGroup" },
    ]);
  });

  it("recovers a single-segment entry as a group-less tag", () => {
    const tags = deserializeStandardValue("loneName;G,N", StandardValueType.ListTag);

    expect(tags).toEqual([
      { group: undefined, name: "loneName" },
      { group: "G", name: "N" },
    ]);
  });

  it("round-trips tags containing separators", () => {
    const original = [
      { group: "G", name: "name,with,commas" },
      { group: undefined, name: "semi;colons" },
    ];
    const serialized = serializeStandardValue(original, StandardValueType.ListTag);
    const roundTripped = deserializeStandardValue(serialized!, StandardValueType.ListTag);

    expect(roundTripped).toEqual(original);
  });
});

describe("findNodeChainByLabels", () => {
  const data = [
    {
      value: "1",
      label: "Category",
      color: "red",
      children: [{ value: "2", label: "Sub", color: "blue" }],
    },
    { value: "3", label: "Other" },
  ];

  it("returns the node chain matching each level's label", () => {
    const chain = findNodeChainByLabels(data, ["Category", "Sub"]);

    expect(chain?.map((n) => n.value)).toEqual(["1", "2"]);
    expect(chain?.map((n) => n.color)).toEqual(["red", "blue"]);
  });

  it("returns undefined when any level does not match", () => {
    expect(findNodeChainByLabels(data, ["Category", "Missing"])).toBeUndefined();
    expect(findNodeChainByLabels(data, ["Missing"])).toBeUndefined();
    expect(findNodeChainByLabels(data, [])).toBeUndefined();
  });
});

describe("collectSubtreeValues", () => {
  const data: MultilevelData<string>[] = [
    {
      value: "1",
      label: "Doujinshi",
      children: [
        { value: "2", label: "Manga", children: [{ value: "3", label: "Full color" }] },
        { value: "4", label: "CG set" },
      ],
    },
    { value: "5", label: "Game" },
  ];

  it("returns the node and everything under it", () => {
    // A resource is filed under a leaf, so filtering by the branch alone would
    // find nothing — the subtree is what the user means by "Doujinshi".
    expect(collectSubtreeValues(data, "1")).toEqual(["1", "2", "3", "4"]);
  });

  it("returns just the node when it is a leaf", () => {
    expect(collectSubtreeValues(data, "5")).toEqual(["5"]);
  });

  it("returns nothing for a value the tree does not have", () => {
    expect(collectSubtreeValues(data, "404")).toEqual([]);
  });
});
