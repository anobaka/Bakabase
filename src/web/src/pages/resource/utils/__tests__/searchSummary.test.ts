import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import { describe, expect, it } from "vitest";

import { buildAutoTabName } from "../buildAutoTabName";
import { groupHasContent, hasSearchSummary } from "../searchSummary";

import { GroupCombinator } from "@/components/ResourceFilter/models";
import { PropertyPool, PropertyType, ResourceTag, SearchOperation } from "@/sdk/constants";

const filter = (bizValue?: string, disabled = false): SearchFilter => ({
  propertyId: 1,
  propertyPool: PropertyPool.Custom,
  operation: SearchOperation.Contains,
  bizValue,
  disabled,
  property: {
    id: 1,
    pool: PropertyPool.Custom,
    name: "Name",
    type: PropertyType.SingleLineText,
  } as SearchFilter["property"],
});

const group = (partial: Partial<SearchFilterGroup> = {}): SearchFilterGroup => ({
  combinator: GroupCombinator.And,
  disabled: false,
  ...partial,
});

const form = (partial: Partial<SearchForm> = {}): SearchForm => ({
  page: 1,
  pageSize: 50,
  ...partial,
});

describe("groupHasContent", () => {
  it("is false for an empty group", () => {
    expect(groupHasContent(group())).toBe(false);
    expect(groupHasContent(group({ filters: [], groups: [] }))).toBe(false);
  });

  it("is true for a group holding a filter", () => {
    expect(groupHasContent(group({ filters: [filter("a")] }))).toBe(true);
  });

  it("looks into nested groups", () => {
    expect(groupHasContent(group({ groups: [group({ filters: [filter("a")] })] }))).toBe(true);
    expect(groupHasContent(group({ groups: [group(), group()] }))).toBe(false);
  });

  it("counts a disabled filter — it still describes the tab", () => {
    expect(groupHasContent(group({ filters: [filter("a", true)] }))).toBe(true);
  });
});

describe("hasSearchSummary", () => {
  it("is false without a form or with a bare one", () => {
    expect(hasSearchSummary(undefined)).toBe(false);
    expect(hasSearchSummary(form())).toBe(false);
    expect(hasSearchSummary(form({ group: group() }))).toBe(false);
  });

  it("is true for any single criterion", () => {
    expect(hasSearchSummary(form({ keyword: "one piece" }))).toBe(true);
    expect(hasSearchSummary(form({ tags: [ResourceTag.Pinned] }))).toBe(true);
    expect(
      hasSearchSummary(form({ orders: [{ property: 1, asc: true }] as SearchForm["orders"] })),
    ).toBe(true);
    expect(hasSearchSummary(form({ group: group({ filters: [filter("a")] }) }))).toBe(true);
  });

  it("ignores empty collections", () => {
    expect(hasSearchSummary(form({ keyword: "", tags: [], orders: [] }))).toBe(false);
  });
});

describe("buildAutoTabName", () => {
  it("is empty without a group", () => {
    expect(buildAutoTabName(undefined)).toBe("");
    expect(buildAutoTabName(form())).toBe("");
  });

  it("joins the values of enabled filters", () => {
    expect(buildAutoTabName(form({ group: group({ filters: [filter("a"), filter("b")] }) }))).toBe(
      "a, b",
    );
  });

  it("skips disabled filters and disabled groups", () => {
    expect(
      buildAutoTabName(form({ group: group({ filters: [filter("a"), filter("b", true)] }) })),
    ).toBe("a");
    expect(
      buildAutoTabName(form({ group: group({ disabled: true, filters: [filter("a")] }) })),
    ).toBe("");
  });

  it("descends into nested groups", () => {
    expect(
      buildAutoTabName(
        form({
          group: group({
            filters: [filter("a")],
            groups: [group({ filters: [filter("b")] })],
          }),
        }),
      ),
    ).toBe("a, b");
  });
});
