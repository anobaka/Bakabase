import type { IProperty } from "@/components/Property/models";

import { describe, expect, it, vi } from "vitest";

import {
  createResourceFilterFixtureConfig,
  getFixtureCounts,
  getFixtureOperations,
} from "../resourceFilterFixtures";

import { PropertyPool, PropertyType, SearchOperation, StandardValueType } from "@/sdk/constants";

vi.mock("@/components/Property/components/PropertyValueRenderer", () => ({ default: () => null }));
vi.mock("@/sdk/BApi", () => ({
  default: new Proxy(
    {},
    {
      get: () => {
        throw new Error("Visual fixtures must not access the backend");
      },
    },
  ),
}));

const choice: IProperty = {
  id: 131,
  pool: PropertyPool.Custom,
  name: "Fixture status",
  type: PropertyType.SingleChoice,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleChoice",
  poolName: "Custom",
  order: 0,
  options: {
    choices: [
      { value: "ready", label: "Ready" },
      { value: "waiting", label: "Waiting" },
    ],
  },
};

describe("resource filter visual fixture adapters", () => {
  it("resolves mock IDs, operation-specific value metadata and recent edits locally", async () => {
    const config = createResourceFilterFixtureConfig([choice], vi.fn());
    const filter = {
      propertyPool: choice.pool,
      propertyId: choice.id,
      operation: SearchOperation.In,
      disabled: false,
    };
    const property = await config.api.getValueProperty(filter);

    expect(property?.type).toBe(PropertyType.MultipleChoice);
    expect(property?.dbValueType).toBe(StandardValueType.ListString);
    expect(property?.options).toEqual(choice.options);
    expect(await config.api.getAvailableOperations(choice.pool, choice.id)).toContain(
      SearchOperation.In,
    );
    await config.api.saveRecentFilter(filter);
    expect(await config.api.getRecentFilters()).toEqual([filter]);
    expect(await config.api.getValueProperty({ ...filter, propertyId: 99999 })).toBeUndefined();
    expect(await config.api.getAvailableOperations(choice.pool, 99999)).toEqual([]);
  });

  it("offers local operations and accurate value types for range, text and tags", async () => {
    const config = createResourceFilterFixtureConfig([], vi.fn());
    const metadata = async (type: PropertyType) =>
      config.api.getValueProperty({
        property: { ...choice, type },
        operation: SearchOperation.Equals,
        disabled: false,
      });

    expect((await metadata(PropertyType.Rating))?.type).toBe(PropertyType.Number);
    expect((await metadata(PropertyType.MultilineText))?.type).toBe(PropertyType.SingleLineText);
    expect((await metadata(PropertyType.Tags))?.bizValueType).toBe(StandardValueType.ListTag);
    expect(getFixtureOperations(PropertyType.Date)).toContain(SearchOperation.GreaterThanOrEquals);
    expect(getFixtureOperations(PropertyType.Boolean)).not.toContain(SearchOperation.Contains);
    expect(getFixtureOperations(PropertyType.Tags)).toContain(SearchOperation.IsNull);
  });

  it("supplies deterministic local counts for choices, nested options and empty fixtures", () => {
    expect(getFixtureCounts(choice)).toEqual(getFixtureCounts(choice));
    expect(Object.keys(getFixtureCounts(choice))).toEqual(["ready", "waiting"]);
    expect(getFixtureCounts({ ...choice, options: undefined })).toEqual({});
    expect(
      Object.keys(
        getFixtureCounts({
          ...choice,
          options: { data: [{ value: "parent", children: [{ value: "child" }] }] },
        }),
      ),
    ).toEqual(["parent", "child"]);
  });
});
