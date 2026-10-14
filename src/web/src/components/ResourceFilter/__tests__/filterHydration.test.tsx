import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FilterConfig, SearchFilter } from "../models";
import type { IProperty } from "@/components/Property/models";
import Filter from "../components/Filter";
import { PropertyPool, PropertyType, SearchOperation } from "@/sdk/constants";

const state = vi.hoisted(() => ({ config: undefined as FilterConfig | undefined }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({ getEnumKey: (key: string) => key }));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("../context/FilterContext", () => ({ useFilterConfig: () => state.config }));
vi.mock("../components/Filter/PropertyField", () => ({
  default: ({ property }: { property?: IProperty }) => (
    <span data-testid="original-property">{property?.type}</span>
  ),
}));
vi.mock("../components/Filter/OperationSelector", () => ({
  default: ({ availableOperations }: { availableOperations?: SearchOperation[] }) => (
    <span data-testid="operations">{availableOperations?.join(",")}</span>
  ),
}));
vi.mock("../components/Filter/DeleteButton", () => ({ default: () => null }));
vi.mock("../components/Filter/DisableButton", () => ({ default: () => null }));

const original = {
  id: 7,
  pool: PropertyPool.Custom,
  type: PropertyType.SingleChoice,
  name: "Remote genre",
} as IProperty;
const valueProperty = { ...original, type: PropertyType.MultipleChoice };
const singleChoiceOperations = [
  SearchOperation.Equals,
  SearchOperation.NotEquals,
  SearchOperation.In,
  SearchOperation.NotIn,
];
const restored = (operation: SearchOperation): SearchFilter => ({
  owner: { serverId: "remote", libraryEpoch: "epoch-remote" },
  propertyPool: PropertyPool.Custom,
  propertyId: 7,
  operation,
  dbValue: '["remote-choice"]',
  disabled: false,
});

beforeEach(() => {
  state.config = {
    api: {
      getProperty: vi.fn().mockResolvedValue(original),
      getValueProperty: vi.fn().mockResolvedValue(valueProperty),
      getAvailableOperations: vi.fn().mockResolvedValue(singleChoiceOperations),
      getAvailableOperationsByPropertyType: vi.fn(async (type) =>
        type === PropertyType.SingleChoice
          ? singleChoiceOperations
          : [SearchOperation.Contains, SearchOperation.NotContains],
      ),
      getRecentFilters: vi.fn().mockResolvedValue([]),
      saveRecentFilter: vi.fn(),
    },
    renderers: {
      openPropertySelector: vi.fn(),
      renderValueInput: (property) => <span data-testid="value-property">{property.type}</span>,
    },
  };
});
afterEach(cleanup);

describe("restored filter properties", () => {
  it.each([SearchOperation.In, SearchOperation.NotIn])(
    "keeps a remote single-choice property's operations for operation %s",
    async (operation) => {
      const filter = restored(operation);
      render(<Filter filter={filter} />);
      await waitFor(() =>
        expect(screen.getByTestId("operations").textContent).toBe(singleChoiceOperations.join(",")),
      );
      expect(screen.getByTestId("original-property").textContent).toBe(
        String(PropertyType.SingleChoice),
      );
      expect(screen.getByTestId("value-property").textContent).toBe(
        String(PropertyType.MultipleChoice),
      );
      expect(state.config!.api.getProperty).toHaveBeenCalledWith(filter);
      expect(state.config!.api.getAvailableOperationsByPropertyType).toHaveBeenCalledWith(
        PropertyType.SingleChoice,
      );
      expect(state.config!.api.getAvailableOperationsByPropertyType).not.toHaveBeenCalledWith(
        PropertyType.MultipleChoice,
      );
    },
  );

  it("preserves the existing value-property fallback for custom presets without an original-property callback", async () => {
    delete state.config!.api.getProperty;
    render(<Filter filter={restored(SearchOperation.In)} />);
    await waitFor(() =>
      expect(screen.getByTestId("original-property").textContent).toBe(
        String(PropertyType.MultipleChoice),
      ),
    );
    expect(screen.getByTestId("value-property").textContent).toBe(
      String(PropertyType.MultipleChoice),
    );
  });
});
