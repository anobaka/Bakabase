import type { BakabaseServiceModelsViewResourceProfileViewModel as ResourceProfile } from "@/sdk/Api";

import { describe, expect, it } from "vitest";

import { hasProfileConditions, profilePreviewSearch, toProfileInputModel } from "../profileUtils";

import { PropertyPool, PropertyValueScope, ResourceAdditionalItem } from "@/sdk/constants";

describe("resource profile request boundaries", () => {
  it("only counts active groups and tags as conditions, matching backend profile matching", () => {
    expect(hasProfileConditions(undefined)).toBe(false);
    expect(hasProfileConditions({ keyword: "legacy keyword" } as ResourceProfile["search"])).toBe(
      false,
    );
    const filter = {
      propertyPool: 4,
      propertyId: 9,
      operation: 1,
      dbValue: '"name"',
      disabled: false,
    };
    expect(
      hasProfileConditions({
        group: { combinator: 1, filters: [filter], disabled: true },
      } as ResourceProfile["search"]),
    ).toBe(false);
    expect(
      hasProfileConditions({
        group: {
          combinator: 1,
          groups: [{ combinator: 1, filters: [{ ...filter, disabled: true }] }],
        },
      } as ResourceProfile["search"]),
    ).toBe(false);
    expect(
      hasProfileConditions({
        group: { combinator: 1, groups: [{ combinator: 1, filters: [filter] }] },
      } as ResourceProfile["search"]),
    ).toBe(true);
    expect(hasProfileConditions({ tags: [1] } as ResourceProfile["search"])).toBe(true);
  });

  it("previews only matching criteria and its requested page, never legacy keyword or unrelated search options", () => {
    const group = {
      combinator: 2,
      filters: [{ propertyPool: 4, propertyId: 9, operation: 1, dbValue: '["author"]' }],
    };
    const profile = {
      search: {
        group,
        tags: [1],
        keyword: "not a matching criterion",
        orders: [{ propertyId: 9 }],
        pageIndex: 99,
        pageSize: 100,
        skipCount: 50,
      },
    } as unknown as ResourceProfile;
    const preview = profilePreviewSearch(profile, 3);
    expect(preview).toMatchObject({
      page: 3,
      pageSize: 25,
      tags: [1],
      additionalItems: ResourceAdditionalItem.DisplayName,
    });
    expect(preview.group).toMatchObject({
      ...group,
      disabled: false,
      filters: [{ ...group.filters[0], disabled: false }],
    });
    expect(preview).not.toHaveProperty("keyword");
    expect(preview).not.toHaveProperty("orders");
    expect(preview).not.toHaveProperty("skipCount");
  });

  it("retains property scope order and explicit empty blocks, while clearing omitted categories with null", () => {
    const properties = {
      properties: [
        {
          pool: PropertyPool.Custom,
          id: 9,
          scopePriority: [9999 as PropertyValueScope, PropertyValueScope.Manual],
        },
      ],
    };
    const input = toProfileInputModel({
      name: "Books",
      priority: -10,
      propertyOptions: properties,
      playerOptions: { players: [] },
      playableFileOptions: {},
      nameTemplate: "",
    });
    expect(input).toEqual({
      name: "Books",
      priority: -10,
      search: null,
      nameTemplate: null,
      enhancerOptions: null,
      playableFileOptions: {},
      playerOptions: { players: [] },
      propertyOptions: properties,
    });
  });
});
