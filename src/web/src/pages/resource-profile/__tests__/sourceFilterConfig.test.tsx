import { describe, expect, it, vi } from "vitest";

import { createSourceFilterConfig } from "../sourceFilterConfig";
import SourceParentResourceInput from "../components/SourceParentResourceInput";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import PropertySelector from "@/components/PropertySelector";
import { InternalProperty, PropertyPool, PropertyType, SearchOperation } from "@/sdk/constants";

const fixture = vi.hoisted(() => ({ operations: vi.fn(), value: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { resource: { getSearchOperationsByPropertyType: fixture.operations } },
}));
vi.mock("../sourceProfiles", () => ({ profileSourceApi: { valueProperty: fixture.value } }));
vi.mock("../components/SourceParentResourceInput", () => ({ default: () => null }));
vi.mock("@/components/Property/components/PropertyValueRenderer", () => ({ default: () => null }));
vi.mock("@/components/PropertySelector", () => ({ default: () => null }));

const scope = { sourceDeviceId: "mac", sourceLibraryEpoch: "epoch" };
const property = {
  id: 9,
  pool: PropertyPool.Custom,
  type: PropertyType.MultipleChoice,
  name: "Mac genres",
  options: { choices: [{ value: "foreign-id", label: "Source genre" }] },
} as any;

describe("resource profile source filter adapter", () => {
  it("opens the existing selector with source metadata and resolves operations by type rather than colliding local IDs", async () => {
    const portal = vi.fn();
    const select = vi.fn();
    const config = createSourceFilterConfig(scope, [property], portal);

    fixture.operations.mockResolvedValue({ data: [SearchOperation.In] });
    config.renderers.openPropertySelector(undefined, select);
    expect(portal).toHaveBeenCalledWith(
      PropertySelector,
      expect.objectContaining({
        properties: [property],
        addable: false,
        editable: false,
        removable: false,
      }),
    );
    await portal.mock.calls[0][1].onSubmit([property]);
    expect(fixture.operations).toHaveBeenCalledWith({ propertyType: PropertyType.MultipleChoice });
    expect(select).toHaveBeenCalledWith(property, [SearchOperation.In]);
    expect(await config.api.getRecentFilters()).toEqual([]);
    await expect(
      config.api.saveRecentFilter({
        propertyId: 9,
        propertyPool: PropertyPool.Custom,
        disabled: false,
      }),
    ).resolves.toBeUndefined();
  });
  it("resolves value options on the source and does not reuse local reference-count queries", async () => {
    const config = createSourceFilterConfig(scope, [property], vi.fn());
    const filter = {
      propertyId: 9,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.In,
      disabled: false,
    };

    fixture.value.mockResolvedValue(property);
    expect(await config.api.getValueProperty(filter)).toBe(property);
    expect(fixture.value).toHaveBeenCalledWith(scope, filter);
    const renderer: any = config.renderers.renderValueInput(
      property,
      '["foreign-id"]',
      '["Source genre"]',
      vi.fn(),
      { isEditing: true },
    );

    expect(renderer.type).toBe(PropertyValueRenderer);
    expect(renderer.props.property).toBe(property);
  });
  it("selects parent resources from the source's own paged search", () => {
    const config = createSourceFilterConfig(scope, [property], vi.fn());
    const parent = {
      ...property,
      id: InternalProperty.ParentResource,
      pool: PropertyPool.Internal,
    };
    const renderer: any = config.renderers.renderValueInput(
      parent,
      '"12"',
      '"Mac collection"',
      vi.fn(),
    );

    expect(renderer.type).toBe(SourceParentResourceInput);
    expect(renderer.props.scope).toEqual(scope);
  });
});
