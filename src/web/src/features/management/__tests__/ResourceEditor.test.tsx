import type { ManagementSource } from "../api";
import type * as OptionsStore from "@/stores/options";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ResourceEditor } from "../ResourceEditor";

import { PropertyPool, PropertyType, PropertyValueScope, StandardValueType } from "@/sdk/constants";
const { clients, writes, options } = vi.hoisted(() => ({
  clients: new Map(),
  writes: vi.fn(),
  options: { propertyValueScopePriority: [] as number[] },
}));

vi.mock("../api", () => ({
  ownerClient: (source: ManagementSource) => clients.get(source.serverId),
}));
vi.mock("@/stores/options", async (original) => ({
  ...(await original<typeof OptionsStore>()),
  useResourceOptionsStore: (selector: (state: unknown) => unknown) => selector({ data: options }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ title, children }: any) => <section aria-label={title}>{children}</section>,
  Spinner: () => <span>Loading</span>,
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
}));
vi.mock("@/components/Property/components/PropertyValueRenderer", () => ({
  default: ({ property, onValueChange, bizValue }: any) => (
    <div>
      <span data-testid="property-value">{bizValue}</span>
      <button onClick={() => onValueChange('"edited"')}>Change {property.name}</button>
    </div>
  ),
}));
const source = (id: string): ManagementSource => ({
  serverId: id,
  name: id,
  libraryEpoch: `epoch-${id}`,
  available: true,
  isLocal: false,
  contractVersion: 1,
});
const property = {
  id: 1,
  pool: PropertyPool.Custom,
  name: "Title",
  type: PropertyType.SingleLineText,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
};

function client(owner: string, reject?: number) {
  return vi.fn(async (operation, input) => {
    if (operation === "resourceGet")
      return {
        data: input.ids.map((id: number) => ({
          id,
          displayName: `${owner}-${id}`,
          mediaLibraries: [],
          properties: {},
        })),
      };
    if (operation === "properties")
      return {
        data: [property, { ...property, id: 2, name: "Attachment", type: PropertyType.Attachment }],
      };
    if (operation === "libraries") return { data: [{ id: 1, name: `${owner} library` }] };
    writes(owner, operation, input);
    if (input.id === reject) throw new Error("Offline");

    return { code: 0 };
  });
}
beforeEach(() => {
  vi.clearAllMocks();
  clients.clear();
  options.propertyValueScopePriority = [
    PropertyValueScope.Manual,
    PropertyValueScope.Synchronization,
  ];
});
afterEach(cleanup);
describe("owner-scoped resource edits", () => {
  it.each([false, true])(
    "honors the owning device's global priority (local=%s)",
    async (isLocal) => {
      const values = [
        { scope: PropertyValueScope.Synchronization, value: "synced", bizValue: "synced" },
        { scope: PropertyValueScope.Manual, value: "manual", bizValue: "manual" },
      ];

      // B's sorted response uses Sync first even though the manager prefers Manual.
      // The local branch must instead use its own original options, not response order.
      if (isLocal) {
        options.propertyValueScopePriority = [
          PropertyValueScope.Synchronization,
          PropertyValueScope.Manual,
        ];
        values.reverse();
      }
      const owner = client("b");
      const request = vi.fn(async (operation: string, input: any) => {
        if (operation !== "resourceGet") return owner(operation, input);

        return { data: [{ id: 7, properties: { [PropertyPool.Custom]: { 1: { values } } } }] };
      });

      clients.set("b", request);
      render(
        <ResourceEditor
          ids={[7]}
          source={{ ...source("b"), isLocal }}
          onClose={() => {}}
          onUpdated={() => {}}
        />,
      );
      await waitFor(() => expect(screen.getByTestId("property-value")).toHaveTextContent("synced"));
      expect(screen.getByTestId("property-value")).not.toHaveTextContent("manual");
    },
  );

  it.each(["profile", "resource"])(
    "keeps %s scope overrides above the owner's global order",
    async (override) => {
      const owner = client("b");

      clients.set(
        "b",
        vi.fn(async (operation: string, input: any) => {
          if (operation !== "resourceGet") return owner(operation, input);

          return {
            data: [
              {
                id: 7,
                properties: {
                  [PropertyPool.Custom]: {
                    1: {
                      values: [
                        {
                          scope: PropertyValueScope.Synchronization,
                          value: "synced",
                          bizValue: "synced",
                        },
                        { scope: PropertyValueScope.Manual, value: "manual", bizValue: "manual" },
                      ],
                      profileScopePriority:
                        override === "profile" ? [PropertyValueScope.Manual] : undefined,
                    },
                  },
                },
                scopePreferences:
                  override === "resource"
                    ? [
                        {
                          propertyPool: PropertyPool.Custom,
                          propertyId: 1,
                          priorities: [
                            { scope: PropertyValueScope.Manual, fallbackOnEmpty: false },
                          ],
                        },
                      ]
                    : [],
              },
            ],
          };
        }),
      );
      render(
        <ResourceEditor ids={[7]} source={source("b")} onClose={() => {}} onUpdated={() => {}} />,
      );
      await waitFor(() => expect(screen.getByTestId("property-value")).toHaveTextContent("manual"));
    },
  );

  it("writes same-numbered resources and definitions to each modal's original owner", async () => {
    clients.set("a", client("a"));
    clients.set("b", client("b"));
    render(
      <>
        <ResourceEditor ids={[7]} source={source("a")} onClose={() => {}} onUpdated={() => {}} />
        <ResourceEditor ids={[7]} source={source("b")} onClose={() => {}} onUpdated={() => {}} />
      </>,
    );
    await waitFor(() => expect(screen.getAllByText("Change Title")).toHaveLength(2));
    fireEvent.click(screen.getAllByText("Change Title")[1]);
    await waitFor(() =>
      expect(writes).toHaveBeenCalledWith("b", "resourcePropertyPut", {
        id: 7,
        body: { propertyId: 1, isCustomProperty: true, value: '"edited"' },
      }),
    );
    expect(writes.mock.calls.every(([owner]) => owner === "b")).toBe(true);
    expect(screen.queryByText("Change Attachment")).toBeNull();
  });
  it("shows partial failure instead of pretending a batch was atomic", async () => {
    clients.set("b", client("b", 8));
    render(
      <ResourceEditor ids={[7, 8]} source={source("b")} onClose={() => {}} onUpdated={() => {}} />,
    );
    await waitFor(() => expect(screen.getByText("Change Title")).toBeTruthy());
    await act(async () => fireEvent.click(screen.getByText("Change Title")));
    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toContain("#8: Error: Offline"),
    );
    expect(writes.mock.calls.map(([, , input]) => input.id)).toEqual([7, 8]);
    expect(screen.getByRole("status").textContent).toContain("management.writeResult");
  });
});
