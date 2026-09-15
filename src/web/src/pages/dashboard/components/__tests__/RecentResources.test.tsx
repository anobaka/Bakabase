import type { Resource as ResourceModel } from "@/core/models/Resource";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import RecentResources from "../RecentResources";
import { buildRecentResourceSearch } from "../recentResourcesData";

import {
  PropertyPool,
  ResourceAdditionalItem,
  ResourceProperty,
  ResourceSearchSortableProperty,
  ResourceTag,
  SearchOperation,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ search: vi.fn(), navigate: vi.fn(), pendingSearch: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { resource: { searchResources: mocks.search } } }));
vi.mock("react-router-dom", () => ({ useNavigate: () => mocks.navigate }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/stores/pendingSearch", () => ({
  usePendingSearchStore: (selector: (store: unknown) => unknown) =>
    selector({ setPendingSearch: mocks.pendingSearch }),
}));
vi.mock("@/components/bakaui", async () => await vi.importActual("@heroui/react"));
vi.mock("@/components/Resource", () => ({
  default: ({
    resource,
    onResourcesDeleted,
  }: {
    resource: ResourceModel;
    onResourcesDeleted: (ids: number[]) => void;
  }) => (
    <article data-resource={resource.id}>
      {resource.displayName}
      <button onClick={() => onResourcesDeleted([resource.id])}>Delete {resource.id}</button>
    </article>
  ),
}));

let host: HTMLDivElement;
let root: Root;

function response(ids: number[], totalCount = ids.length) {
  return { code: 0, data: ids.map((id) => ({ id, displayName: `Resource ${id}` })), totalCount };
}

function deferred() {
  let resolve!: (value: ReturnType<typeof response>) => void;
  const promise = new Promise<ReturnType<typeof response>>((res) => {
    resolve = res;
  });

  return { promise, resolve };
}

async function render(refreshKey = 0) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <RecentResources refreshKey={refreshKey} />
      </HeroUIProvider>,
    ),
  );
}

async function click(element: Element) {
  await act(async () => (element as HTMLElement).click());
}

function tab(label: "recentlyAdded" | "recentlyPlayed" | "pinned") {
  return [...host.querySelectorAll('[role="tab"]')].find(
    (el) => el.textContent === `dashboard.tab.${label}`,
  )!;
}

function button(label: string) {
  return [...host.querySelectorAll("button")].find((el) => el.textContent === label)!;
}

function resourceIds() {
  return [...host.querySelectorAll("[data-resource]")].map((el) =>
    Number(el.getAttribute("data-resource")),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("dashboard recent resources", () => {
  it("loads only the selected tab, caps its grid at 12, and reuses completed tab caches", async () => {
    mocks.search.mockResolvedValueOnce(
      response(
        Array.from({ length: 15 }, (_, i) => i + 1),
        60,
      ),
    );
    await render();

    expect(mocks.search).toHaveBeenCalledTimes(1);
    expect(mocks.search.mock.calls[0][0]).toMatchObject({
      page: 1,
      pageSize: 12,
      orders: [{ property: ResourceSearchSortableProperty.AddDt, asc: false }],
    });
    expect(mocks.search.mock.calls[0][1]).toEqual({
      additionalItems: ResourceAdditionalItem.All,
      saveSearch: false,
    });
    expect(resourceIds()).toEqual(Array.from({ length: 12 }, (_, i) => i + 1));

    mocks.search.mockResolvedValueOnce(response([20]));
    await click(tab("recentlyPlayed"));
    expect(mocks.search.mock.calls[1][0]).toMatchObject({
      pageSize: 12,
      orders: [{ property: ResourceSearchSortableProperty.PlayedAt, asc: false }],
      group: {
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: ResourceProperty.PlayedAt,
            operation: SearchOperation.IsNotNull,
            disabled: false,
          },
        ],
      },
    });
    // Server filtering owns eligibility; the browser does not trim after taking 12 results.
    expect(resourceIds()).toEqual([20]);

    mocks.search.mockResolvedValueOnce(response([30]));
    await click(tab("pinned"));
    expect(mocks.search.mock.calls[2][0].tags).toEqual([ResourceTag.Pinned]);
    await click(tab("recentlyAdded"));
    expect(mocks.search).toHaveBeenCalledTimes(3);
    expect(resourceIds()).toHaveLength(12);

    const wheel = new WheelEvent("wheel", { bubbles: true, cancelable: true, deltaY: 80 });

    host.querySelector("[data-resource]")!.dispatchEvent(wheel);
    expect(wheel.defaultPrevented).toBe(false);
    expect(host.querySelector(".grid")).not.toBeNull();
  });

  it("carries each tab's actual query and a full page size to view all", async () => {
    mocks.search.mockResolvedValue(response([1]));
    await render();
    for (const [tabLabel, key] of [
      ["recentlyAdded", "added"],
      ["recentlyPlayed", "played"],
      ["pinned", "pinned"],
    ] as const) {
      await click(tab(tabLabel));
      await click(button("dashboard.action.viewAll"));
      expect(mocks.pendingSearch).toHaveBeenLastCalledWith(buildRecentResourceSearch(key, 100));
      expect(mocks.navigate).toHaveBeenLastCalledWith("/resource");
    }
  });

  it("refreshes the active tab and invalidates other caches without prefetching them", async () => {
    mocks.search.mockResolvedValueOnce(response([1]));
    await render();
    mocks.search.mockResolvedValueOnce(response([2]));
    await click(tab("recentlyPlayed"));
    await click(tab("recentlyAdded"));

    mocks.search.mockResolvedValueOnce(response([3]));
    await render(1);
    expect(mocks.search).toHaveBeenCalledTimes(3);
    expect(resourceIds()).toEqual([3]);

    mocks.search.mockResolvedValueOnce(response([4]));
    await click(tab("recentlyPlayed"));
    expect(mocks.search).toHaveBeenCalledTimes(4);
    expect(resourceIds()).toEqual([4]);
    await click(tab("recentlyAdded"));
    expect(mocks.search).toHaveBeenCalledTimes(4);
    expect(resourceIds()).toEqual([3]);
  });

  it("ignores late responses after switching tabs and refreshing the current tab", async () => {
    const added = deferred();
    const played = deferred();
    const refreshed = deferred();

    mocks.search
      .mockReturnValueOnce(added.promise)
      .mockReturnValueOnce(played.promise)
      .mockReturnValueOnce(refreshed.promise);
    await render();
    const addedSignal = mocks.search.mock.calls[0][2].signal;

    await click(tab("recentlyPlayed"));
    expect(addedSignal.aborted).toBe(true);
    await render(1);
    await act(async () => refreshed.resolve(response([3])));
    expect(resourceIds()).toEqual([3]);
    await act(async () => {
      added.resolve(response([1]));
      played.resolve(response([2]));
    });
    expect(resourceIds()).toEqual([3]);
  });

  it("aborts unfinished work on unmount and does not reuse its response on a later mount", async () => {
    const old = deferred();

    mocks.search.mockReturnValueOnce(old.promise);
    await render();
    const signal = mocks.search.mock.calls[0][2].signal;

    await act(async () => root.render(<></>));
    expect(signal.aborted).toBe(true);
    await act(async () => old.resolve(response([1])));
    mocks.search.mockResolvedValueOnce(response([2]));
    await render();
    expect(resourceIds()).toEqual([2]);
    expect(mocks.search).toHaveBeenCalledTimes(2);
  });

  it.each(["api", "network"])(
    "shows a retryable %s failure instead of an empty result",
    async (kind) => {
      if (kind === "api")
        mocks.search.mockResolvedValueOnce({ code: 500, message: "failed", data: [] });
      else mocks.search.mockRejectedValueOnce(new Error("offline"));
      await render();
      expect(host.querySelector('[role="alert"]')).toHaveTextContent(
        "dashboard.resources.loadFailed",
      );
      expect(host.textContent).not.toContain("dashboard.empty.noResources");
      mocks.search.mockResolvedValueOnce(response([4]));
      await click(button("dashboard.resources.retry"));
      expect(resourceIds()).toEqual([4]);
      expect(host.querySelector('[role="alert"]')).toBeNull();
    },
  );

  it("removes deleted resources from all cached lists immediately", async () => {
    mocks.search.mockResolvedValueOnce(response([1]));
    await render();
    mocks.search.mockResolvedValueOnce(response([1, 2]));
    await click(tab("recentlyPlayed"));
    await click(button("Delete 1"));
    expect(resourceIds()).toEqual([2]);
    await click(tab("recentlyAdded"));
    expect(resourceIds()).toEqual([]);
    expect(host.textContent).toContain("dashboard.empty.noResources");
    expect(mocks.search).toHaveBeenCalledTimes(2);
  });

  it("uses tab-specific empty states and an add-resources action only on the added tab", async () => {
    mocks.search.mockResolvedValue(response([]));
    await render();
    expect(host.textContent).toContain("dashboard.empty.noResources");
    await click(button("dashboard.action.addResources"));
    expect(mocks.navigate).toHaveBeenLastCalledWith("/path-mark-config");
    await click(tab("recentlyPlayed"));
    expect(host.textContent).toContain("dashboard.empty.noPlayHistory");
    expect(button("dashboard.action.addResources")).toBeUndefined();
    await click(tab("pinned"));
    expect(host.textContent).toContain("dashboard.empty.noPinned");
  });
});
