import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useCoverResolution } from "../useCoverResolution";
import { usePlayableItemResolution } from "../usePlayableItemResolution";

import { ResourceOwnerProvider } from "@/features/management/resourceContext";
import type { ManagementSource } from "@/features/management/api";
import type { Resource } from "@/core/models/Resource";
import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ subscribe: vi.fn(), request: vi.fn() }));
vi.mock("@/services/ResourceDiscoveryChannel", () => ({
  resourceDiscoveryChannel: { subscribe: mocks.subscribe },
}));
vi.mock("@/sdk/BApi", async (original) => {
  const actual = await original<{ default: any }>();
  actual.default.request = mocks.request;

  return actual;
});

const owner = (serverId: string): ManagementSource => ({
  serverId,
  name: serverId,
  isLocal: false,
  libraryEpoch: "epoch",
  available: true,
  contractVersion: 1,
});
const resource = {
  id: 7,
  path: "/resource",
  hasLocalPath: true,
  dataStates: [ResourceDataType.Cover, ResourceDataType.PlayableItem].map((dataType) => ({
    resourceId: 7,
    origin: DataOrigin.FileSystem,
    dataType,
    status: DataStatus.NotStarted,
  })),
} as Resource;

function Probe({ id, refreshKey = 0 }: { id: string; refreshKey?: number }) {
  const cover = useCoverResolution(resource, undefined, refreshKey);
  const playable = usePlayableItemResolution(resource);

  return (
    <div data-testid={id}>
      {JSON.stringify({ cover, items: playable.groups.flatMap((group) => group.items) })}
    </div>
  );
}

beforeEach(() => {
  mocks.subscribe.mockReset();
  mocks.request.mockReset().mockImplementation(async (request) => ({
    code: 0,
    data: [
      {
        origin: DataOrigin.FileSystem,
        key: request.path.includes("/one/") ? "/one.mp4" : "/two.mp4",
      },
    ],
  }));
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => ({
      ok: true,
      json: async () => ({ code: 0, data: [url.includes("/one/") ? "/one.jpg" : "/two.jpg"] }),
    })),
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("managed resource discovery", () => {
  it("rediscovers an absent remote cover when the cover is explicitly refreshed", async () => {
    const source = owner("one");
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ code: 0, data: [] }),
    } as Response);
    const view = render(
      <ResourceOwnerProvider source={source} resourceId={7}>
        <Probe id="cover" />
      </ResourceOwnerProvider>,
    );
    await act(async () => {});
    expect(screen.getByTestId("cover").textContent).toContain('"status":"not-found"');

    await act(async () => view.rerender(
      <ResourceOwnerProvider source={source} resourceId={7}>
        <Probe id="cover" refreshKey={1} />
      </ResourceOwnerProvider>,
    ));

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId("cover").textContent).toContain("/one.jpg");
    expect(screen.getByTestId("cover").textContent).toContain('"status":"ready"');
    expect(mocks.subscribe).not.toHaveBeenCalled();
  });

  it("loads each owner's cover and playable files without subscribing to local numeric IDs", async () => {
    await act(async () =>
      render(
        <>
          <ResourceOwnerProvider source={owner("one")} resourceId={7}>
            <Probe id="one" />
          </ResourceOwnerProvider>
          <ResourceOwnerProvider source={owner("two")} resourceId={7}>
            <Probe id="two" />
          </ResourceOwnerProvider>
        </>,
      ),
    );

    expect(screen.getByTestId("one").textContent).toContain("/one.jpg");
    expect(screen.getByTestId("one").textContent).toContain("/one.mp4");
    expect(screen.getByTestId("two").textContent).toContain("/two.jpg");
    expect(screen.getByTestId("two").textContent).toContain("/two.mp4");
    expect(mocks.subscribe).not.toHaveBeenCalled();
    expect(mocks.request.mock.calls.map(([request]) => request.body.libraryEpoch)).toEqual([
      "epoch",
      "epoch",
    ]);
  });
});
