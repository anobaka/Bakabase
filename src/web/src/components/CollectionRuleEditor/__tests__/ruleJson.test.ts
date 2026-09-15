import type { MultilevelData } from "@/components/StandardValue/models";

import { describe, expect, it } from "vitest";

import {
  buildMultilevelSubtreeRule,
  buildRuleSearchJson,
  isEmptyGroup,
  parseRuleSearchJson,
} from "@/components/CollectionRuleEditor/ruleJson";
import { GroupCombinator } from "@/components/ResourceFilter/models";
import { PropertyPool, SearchOperation, StandardValueType } from "@/sdk/constants";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";

const group = {
  combinator: GroupCombinator.And,
  disabled: false,
  filters: [
    {
      propertyId: 3,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Equals,
      dbValue: "Asagi",
      disabled: false,
    },
  ],
};

describe("collection rule json", () => {
  /**
   * The editor and the server disagree about one word — the editor calls a filter's
   * value `dbValue`, the stored shape calls it `value` — and a rule that lost its
   * values on the way through would look saved and match nothing.
   */
  it("round-trips a rule without losing filter values", () => {
    const parsed = parseRuleSearchJson(buildRuleSearchJson(group));

    expect(parsed?.filters?.[0]).toMatchObject({
      propertyId: 3,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Equals,
      dbValue: "Asagi",
    });
  });

  it("stores nothing for a rule with no filters in it", () => {
    // A group with no filters matches everything; saved as a rule it would quietly
    // pull the whole library into the collection.
    expect(buildRuleSearchJson({ combinator: GroupCombinator.And, disabled: false })).toBeNull();
    expect(isEmptyGroup({ combinator: GroupCombinator.And, disabled: false })).toBe(true);
  });

  it("survives a rule it cannot read", () => {
    expect(parseRuleSearchJson("{ not json")).toBeUndefined();
    expect(parseRuleSearchJson(null)).toBeUndefined();
  });
});

describe("buildMultilevelSubtreeRule", () => {
  const data: MultilevelData<string>[] = [
    {
      value: "1",
      label: "Doujinshi",
      children: [
        { value: "2", label: "Manga" },
        { value: "3", label: "CG set" },
      ],
    },
  ];

  /**
   * Resources are filed on leaves, so a rule naming the branch alone would make an
   * empty collection out of a category with a hundred things in it.
   */
  it("asks for the node and everything under it", () => {
    const json = buildMultilevelSubtreeRule(7, PropertyPool.Custom, data, "1")!;
    const filter = parseRuleSearchJson(json)!.filters![0];

    expect(filter.operation).toBe(SearchOperation.In);
    expect(deserializeStandardValue(filter.dbValue!, StandardValueType.ListString)).toEqual([
      "1",
      "2",
      "3",
    ]);
  });

  it("builds no rule for a node the tree does not have", () => {
    expect(buildMultilevelSubtreeRule(7, PropertyPool.Custom, data, "404")).toBeNull();
  });
});
