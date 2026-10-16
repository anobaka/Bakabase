import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ManagementSource } from "@/features/management/api";
import { PropertyPool, PropertyType } from "@/sdk/constants";
import CustomPropertyPage from "..";

const state = vi.hoisted(() => ({
  sources: [] as ManagementSource[],
  request: vi.fn(),
  createPortal: vi.fn(),
  refresh: vi.fn(),
}));
vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");
  return { default: Object.assign(new Api(), { request: state.request }) };
});
vi.mock("@/features/management/DeviceDot", () => ({
  default: ({ name }: any) => <span aria-label={name}>●</span>,
}));
vi.mock("@/features/management/useManagementSources", () => ({
  useManagementSources: () => ({ sources: state.sources, refresh: state.refresh }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("@/features/data-sync/components/EntitySyncList", () => ({
  propertyHasOptions: () => false,
}));
vi.mock("@/features/data-sync/components/DefinitionsPageSync", () => ({
  useDefinitionSync: () => ({}),
  DataSyncHeaderLink: () => null,
  DataSyncEmptyStateLine: () => null,
  DefinitionSyncRow: ({ localKey }: any) => <span>local-sync-{localKey}</span>,
}));
vi.mock("../components/PropertySummary", () => ({ default: () => <span>Summary</span> }));
vi.mock("../components/TypeConversionRuleOverviewDialog", () => ({ default: () => null }));
vi.mock("@/components/CustomPropertySortModal", () => ({ default: () => null }));
vi.mock("@/components/Property/components/PropertyTypeIcon", () => ({
  default: () => <span>Text</span>,
}));
vi.mock("@/components/PropertyModal", async () => {
  const { useResourceApi } = await import("@/features/management/resourceContext");
  return {
    default: function MockPropertyModal({ value }: any) {
      const api = useResourceApi();
      return (
        <button
          onClick={() =>
            api.customProperty.putCustomProperty(value.id, { name: "Edited", type: value.type })
          }
        >
          Save original editor
        </button>
      );
    },
  };
});
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, "aria-label": label }: any) => (
    <button disabled={isDisabled} aria-label={label} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ value, onValueChange, "aria-label": label }: any) => (
    <input aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />
  ),
  Listbox: ({ children }: any) => <div>{children}</div>,
  ListboxItem: ({ children }: any) => <div>{children}</div>,
  Modal: () => null,
  Popover: ({ trigger }: any) => trigger,
  Select: () => null,
  Spinner: () => <span>Loading</span>,
  Tooltip: ({ children }: any) => children,
}));
const source = (id: string): ManagementSource => ({
  serverId: id,
  libraryEpoch: `epoch-${id}`,
  name: id,
  isLocal: id === "local",
  available: true,
  contractVersion: 1,
});
const property = (name: string, order: number) => ({
  id: 7,
  name,
  order,
  pool: PropertyPool.Custom,
  type: PropertyType.SingleLineText,
  valueCount: 3,
});
const remote = () => state.sources.find((s) => !s.isLocal)!;
beforeEach(() => {
  state.sources = [source("local"), source("remote")];
  state.createPortal.mockReset();
  state.request.mockReset().mockImplementation(async ({ body, path }) => {
    if (path === "/custom-property/all") return { data: [property("Local author", 2)] };
    if (body?.operation === "customProperties")
      return { data: [{ ...property("Remote author", 1), valueCount: undefined }] };
    return { code: 0 };
  });
});
afterEach(cleanup);

describe("original custom property page with synced definitions", () => {
  it("interleaves owners by ordinary order and keeps local definition-sync state off a remote same-ID row", async () => {
    render(<CustomPropertyPage />);
    await act(async () => {});
    const rows = [...document.querySelectorAll("li")];
    expect(rows.map((row) => row.querySelector("button")?.textContent)).toEqual([
      "Remote author",
      "Local author",
    ]);
    expect(rows[0]).not.toHaveTextContent("local-sync-7");
    expect(rows[1]).toHaveTextContent("local-sync-7");
    expect(within(rows[0]).getByLabelText("remote")).toBeInTheDocument();
    expect(within(rows[0]).getAllByText("—")).toHaveLength(2);
    expect(within(rows[0]).queryByText("customProperty.valueCount")).not.toBeInTheDocument();
    fireEvent.click(within(rows[0]).getByRole("button", { name: "Remote author" }));
    await act(async () =>
      fireEvent.click(screen.getByRole("button", { name: "Save original editor" })),
    );
    expect(
      state.request.mock.calls.some(
        ([r]) =>
          r.path === "/federation/local/management/remote/request" &&
          r.body.operation === "customPropertyPut" &&
          r.body.id === 7,
      ),
    ).toBe(true);
    expect(state.request.mock.calls.some(([r]) => r.path === "/custom-property/7")).toBe(false);
  });
  it("keeps an offline synced property visible and read-only, then removes it when management is revoked", async () => {
    remote().available = false;
    remote().metadata = {
      identity: { serverId: "remote", libraryEpoch: "epoch-remote" },
      name: "remote",
      denied: false,
      data: {
        activeGeneration: "complete",
        activeSequence: 1,
        resourceCount: 1,
        stagedResourceCount: 0,
      },
      sync: { paused: false, isOffline: true },
    };
    const view = render(<CustomPropertyPage />);
    await act(async () => {});
    expect(screen.getByRole("button", { name: "Remote author" })).toBeDisabled();
    expect(screen.getByText("management.catalog.readOnly")).toBeInTheDocument();
    state.sources = state.sources.map((s) =>
      s.isLocal ? s : { ...s, metadata: { ...s.metadata!, denied: true } },
    );
    view.rerender(<CustomPropertyPage />);
    await act(async () => {});
    expect(screen.queryByText("Remote author")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Local author" })).toBeEnabled();
  });
  it("deletes a remote same-ID property only through that owner's original delete operation", async () => {
    render(<CustomPropertyPage />);
    await act(async () => {});
    const row = document.querySelector("li")!;
    fireEvent.click(within(row).getByRole("button", { name: "customProperty.action.deleteNamed" }));
    await act(async () => state.createPortal.mock.calls[0][1].onOk());
    expect(
      state.request.mock.calls.some(
        ([r]) =>
          r.path === "/federation/local/management/remote/request" &&
          r.body.operation === "customPropertyDelete" &&
          r.body.id === 7,
      ),
    ).toBe(true);
    expect(state.request.mock.calls.some(([r]) => r.path === "/custom-property/7")).toBe(false);
  });
});
