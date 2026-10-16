import type * as ManagementApi from "../api";
import type { ManagementSource } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ResourceGroup } from "../ResourceGroup";
const { clients } = vi.hoisted(() => ({ clients: new Map() }));

vi.mock("../api", async (original) => ({
  ...(await original<typeof ManagementApi>()),
  ownerClient: (source: ManagementSource) => clients.get(source.serverId),
}));
vi.mock("../OwnedResourcePlay", () => ({ default: () => null }));
vi.mock("../ResourceEditor", () => ({
  ResourceEditor: ({ source, ids }: any) => (
    <div data-testid="edit-target">
      {source.serverId}:{ids.join(",")}
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span>Loading</span>,
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
}));
const source = (id: string, available = true): ManagementSource => ({
  serverId: id,
  name: id,
  libraryEpoch: `epoch-${id}`,
  available,
  isLocal: false,
  contractVersion: 1,
});

beforeEach(() => {
  clients.clear();
});
afterEach(cleanup);
describe("source-grouped resources", () => {
  it("keeps equal IDs selectable and editable under different owners", async () => {
    for (const id of ["a", "b"])
      clients.set(
        id,
        vi
          .fn()
          .mockResolvedValue({ data: [{ id: 7, displayName: `${id} resource` }], totalCount: 1 }),
      );
    render(
      <>
        <ResourceGroup keyword="" source={source("a")} />
        <ResourceGroup keyword="" source={source("b")} />
      </>,
    );
    await waitFor(() => expect(screen.getByText("a resource")).toBeTruthy());
    fireEvent.click(screen.getByText("a resource"));
    fireEvent.click(screen.getByText("b resource"));
    expect(screen.getAllByTestId("edit-target").map((x) => x.textContent)).toEqual(["a:7", "b:7"]);
  });
  it("keeps an unavailable source visible without issuing a search or showing empty success", () => {
    const request = vi.fn();

    clients.set("offline", request);
    render(<ResourceGroup keyword="" source={source("offline", false)} />);
    expect(screen.getByRole("region", { name: "offline" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("management.unavailable");
    expect(screen.queryByText("management.empty")).toBeNull();
    expect(request).not.toHaveBeenCalled();
  });
  it("ignores results from an older search after the keyword changes", async () => {
    let finishOld!: (value: unknown) => void;
    const client = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finishOld = resolve;
          }),
      )
      .mockResolvedValue({ data: [{ id: 8, displayName: "New result" }], totalCount: 1 });

    clients.set("b", client);
    const owner = source("b");
    const { rerender } = render(<ResourceGroup keyword="old" source={owner} />);

    rerender(<ResourceGroup keyword="new" source={owner} />);
    await waitFor(() => expect(screen.getByText("New result")).toBeTruthy());
    await act(async () =>
      finishOld({ data: [{ id: 7, displayName: "Old result" }], totalCount: 1 }),
    );
    expect(screen.queryByText("Old result")).toBeNull();
    expect(screen.getByText("New result")).toBeTruthy();
  });
  it("turns only the selected owner's page and reports its own page count", async () => {
    for (const id of ["a", "b"])
      clients.set(
        id,
        vi
          .fn()
          .mockResolvedValue({ data: [{ id: 7, displayName: `${id} resource` }], totalCount: 45 }),
      );
    render(
      <>
        <ResourceGroup keyword="" source={source("a")} />
        <ResourceGroup keyword="" source={source("b")} />
      </>,
    );
    await waitFor(() => expect(screen.getByText("b resource")).toBeTruthy());
    fireEvent.click(within(screen.getByRole("region", { name: "b" })).getByText("management.next"));
    await waitFor(() =>
      expect(clients.get("b")).toHaveBeenLastCalledWith("resourceSearch", {
        body: { keyword: "", page: 2, pageSize: 30 },
      }),
    );
    expect(clients.get("a")).toHaveBeenCalledTimes(1);
  });
});
