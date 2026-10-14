import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceTabContent from "../components/ResourceTabContent";
import type { ManagementSource } from "@/features/management/api";
import { resourceSelectionKey } from "@/features/management/resourceIdentity";

const source = (serverId: string, isLocal = false): ManagementSource => ({
  serverId,
  name: serverId,
  isLocal,
  available: true,
  libraryEpoch: "epoch",
  contractVersion: 1,
});
const mocks = vi.hoisted(() => ({
  cards: new Map<string, any>(),
  search: vi.fn(async () => []),
  reload: vi.fn(),
  remove: vi.fn(),
  selectAll: vi.fn(),
  sources: [] as any[],
  resources: [] as any[],
  sourcesLoading: false,
  sourcesError: undefined as Error | undefined,
  refreshSources: vi.fn(),
  cancelSelection: vi.fn(),
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: { getSavedSearch: async () => ({ data: { search: { page: 1, pageSize: 50 } } }) },
    options: { patchResourceOptions: vi.fn(), patchUiOptions: vi.fn() },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/features/management/resourceSourcesContext", () => ({
  useResourceSources: () => ({
    sources: mocks.sources,
    loading: mocks.sourcesLoading,
    error: mocks.sourcesError,
    refresh: mocks.refreshSources,
  }),
}));
vi.mock("@/hooks/useResourceSearch", () => ({
  useResourceSearch: () => ({
    resources: mocks.resources,
    setResources: vi.fn(),
    loading: false,
    response: undefined,
    search: mocks.search,
    reloadResources: mocks.reload,
    removeResources: mocks.remove,
    searchAllResourceKeys: mocks.selectAll,
    cancelResourceSelection: mocks.cancelSelection,
  }),
}));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: () => ({ initialized: true, data: { resource: { colCount: 3 } } }),
  useResourceOptionsStore: () => ({ initialized: true, data: {} }),
}));
vi.mock("@/stores/bTasks", () => ({
  useBTasksStore: Object.assign((selector: any) => selector({ tasks: [] }), {
    getState: () => ({ tasks: [] }),
  }),
  selectResourceMovingTask: () => () => undefined,
}));
vi.mock("@/stores/resourceMovePanel", () => ({
  bindMovePayload: (value: any) => value,
  clearMovePanelContext: vi.fn(),
  setMovePanelContext: vi.fn(),
  setMovePanelDragging: vi.fn(),
  RESOURCE_MOVE_MIME: "move",
  RESOURCE_MOVE_SUBMITTED_EVENT: "move-submitted",
  RESOURCE_MOVE_UPDATED_EVENT: "move-updated",
  selectPanelResourceReservation: () => () => undefined,
  useResourceMovePanelStore: Object.assign((selector: any) => selector({ panelEnabled: false }), {
    getState: () => ({ panelEnabled: false }),
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, ...props }: any) => <button onClick={onPress}>{children}</button>,
  Card: ({ children }: any) => <div>{children}</div>,
  CardBody: ({ children }: any) => <div>{children}</div>,
  Link: ({ children }: any) => <span>{children}</span>,
  Pagination: () => null,
  Spinner: () => null,
}));
vi.mock("@/components/ResizablePanelDivider", () => ({
  default: ({ leftPanel, rightPanel }: any) => (
    <>
      {leftPanel}
      {rightPanel}
    </>
  ),
}));
vi.mock("../components/FilterPanel", () => ({
  default: ({ selectedResourceCount, onSelectAllChange, onSearch }: any) => (
    <div>
      <span data-testid="selected-count">{selectedResourceCount}</span>
      <button onClick={() => onSelectAllChange(true, true)}>select all matches</button>
      <button onClick={() => onSearch({ keyword: "updated filter" }, false)}>apply filter</button>
    </div>
  ),
}));
vi.mock("../components/Resources", async () => {
  const React = await import("react");

  return {
    default: React.forwardRef(({ cellCount, columnCount, renderCell }: any, ref) => {
      React.useImperativeHandle(ref, () => ({ measure: vi.fn(), rearrange: vi.fn() }));

      return (
        <div>
          {Array.from({ length: cellCount }, (_, index) =>
            renderCell({
              columnIndex: index % columnCount,
              rowIndex: Math.floor(index / columnCount),
              style: {},
            }),
          )}
        </div>
      );
    }),
  };
});
vi.mock("@/components/Resource", () => ({
  default: (props: any) => {
    const name = props.resource.owner.serverId;
    mocks.cards.set(name, props);

    return (
      <button
        data-testid={name}
        onClick={(event) => props.onSelected(props.resource.id, event.shiftKey)}
      >
        {name}:{String(props.selected)}
      </button>
    );
  },
}));

beforeEach(() => {
  mocks.cards.clear();
  mocks.reload.mockClear();
  mocks.remove.mockClear();
  mocks.search.mockClear();
  mocks.sourcesLoading = false;
  mocks.sourcesError = undefined;
  mocks.refreshSources.mockClear();
  mocks.cancelSelection.mockClear();
  mocks.sources = [source("local", true), source("one"), source("two")];
  mocks.resources = mocks.sources.map((owner) => ({
    id: 7,
    owner,
    path: `/${owner.serverId}`,
    tags: [],
  }));
  mocks.selectAll.mockResolvedValue(mocks.resources.map(resourceSelectionKey));
});
afterEach(cleanup);

describe("the original grid with resources from several devices", () => {
  it("marks only remote covers and keeps their device color stable when resources reorder", async () => {
    const view = render(<ResourceTabContent searchId="test" activated />);
    await screen.findByTestId("one");
    expect(screen.queryByLabelText("local")).toBeNull();
    const color = screen.getByLabelText("one").style.backgroundColor;
    expect(color).not.toBe("");
    mocks.resources = [...mocks.resources].reverse();
    view.rerender(<ResourceTabContent searchId="test" searchName="Reordered" activated />);
    expect(screen.getByLabelText("one").style.backgroundColor).toBe(color);
    expect(screen.queryByLabelText("local")).toBeNull();
  });

  it("selects colliding IDs independently and routes edits/deletions to owner-qualified keys", async () => {
    render(<ResourceTabContent searchId="test" activated />);
    await waitFor(() => expect(screen.getByTestId("one")).toBeTruthy());
    fireEvent.click(screen.getByTestId("one"), { ctrlKey: true });
    expect(screen.getByTestId("one").textContent).toBe("one:true");
    expect(screen.getByTestId("local").textContent).toBe("local:false");
    expect(screen.getByTestId("two").textContent).toBe("two:false");
    fireEvent.click(screen.getByTestId("two"), { ctrlKey: true });
    expect(screen.getByTestId("selected-count").textContent).toBe("2");
    expect(
      mocks.cards.get("one").selectedResourceGroups.map((group: any) => group.owner.serverId),
    ).toEqual(["one", "two"]);
    await act(async () => mocks.cards.get("one").onSelectedResourcesChanged([7]));
    expect(mocks.reload).toHaveBeenCalledWith([resourceSelectionKey(mocks.resources[1])]);
    act(() => mocks.cards.get("two").onResourcesDeleted([7]));
    expect(mocks.remove).toHaveBeenCalledWith([resourceSelectionKey(mocks.resources[2])]);
    expect(screen.getByTestId("one").textContent).toBe("one:true");
    expect(screen.getByTestId("selected-count").textContent).toBe("1");
  });

  it("selects all matching resources through the multi-owner search rather than local IDs", async () => {
    render(<ResourceTabContent searchId="test" activated />);
    await waitFor(() => expect(screen.getByText("select all matches")).toBeTruthy());
    await act(async () => fireEvent.click(screen.getByText("select all matches")));
    expect(mocks.selectAll).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("selected-count").textContent).toBe("3");
  });

  it("uses the latest owner-aware search for filter clicks after sources finish loading", async () => {
    mocks.sourcesLoading = true;
    const initialSearch = mocks.search;
    const view = render(<ResourceTabContent searchId="test" activated />);

    expect(initialSearch).not.toHaveBeenCalled();
    const managedSearch = vi.fn(async () => []);
    mocks.search = managedSearch;
    mocks.sourcesLoading = false;
    view.rerender(<ResourceTabContent searchId="test" searchName="Ready" activated />);
    await waitFor(() => expect(managedSearch).toHaveBeenCalled());
    managedSearch.mockClear();
    await act(async () => fireEvent.click(screen.getByText("apply filter")));

    expect(initialSearch).not.toHaveBeenCalled();
    expect(managedSearch).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: "updated filter" }),
      expect.objectContaining({ mode: "replace" }),
    );
  });

  it("keeps source discovery failures visible and retries source discovery", async () => {
    mocks.sources = [];
    mocks.sourcesError = new Error("Devices could not be loaded");
    render(<ResourceTabContent searchId="test" activated />);
    expect(await screen.findByRole("status")).toHaveTextContent("Devices could not be loaded");
    fireEvent.click(screen.getByText("management.retry"));
    expect(mocks.refreshSources).toHaveBeenCalledOnce();
  });

  it.each(["filter", "manual"])(
    "does not reapply a pending full selection after a %s change",
    async (change) => {
      let resolve!: (keys: ReturnType<typeof resourceSelectionKey>[]) => void;
      mocks.selectAll.mockImplementationOnce(
        () =>
          new Promise((done) => {
            resolve = done;
          }),
      );
      render(<ResourceTabContent searchId="test" activated />);
      await screen.findByText("select all matches");
      fireEvent.click(screen.getByText("select all matches"));
      if (change === "filter")
        await act(async () => fireEvent.click(screen.getByText("apply filter")));
      else fireEvent.click(screen.getByTestId("one"), { ctrlKey: true });
      await act(async () => resolve(mocks.resources.map(resourceSelectionKey)));
      expect(screen.getByTestId("selected-count")).toHaveTextContent(
        change === "filter" ? "0" : "1",
      );
      expect(mocks.cancelSelection).toHaveBeenCalled();
    },
  );
});
