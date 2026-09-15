import type { Resource } from "@/core/models/Resource";
import type { DiscoveryData } from "@/services/ResourceDiscoveryChannel";

import { act } from "react-dom/test-utils";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useCoverResolution } from "../useCoverResolution";

import { DataOrigin, DataStatus, ResourceDataType } from "@/sdk/constants";

const { subscribe } = vi.hoisted(() => ({ subscribe: vi.fn() }));
vi.mock("@/services/ResourceDiscoveryChannel", () => ({ resourceDiscoveryChannel: { subscribe } }));

let root: ReturnType<typeof createRoot>;
let container: HTMLDivElement;
let latest: ReturnType<typeof useCoverResolution>;
const listeners = new Map<DataOrigin, (data: DiscoveryData) => void>();

function Probe({ resource }: { resource: Resource }) {
  latest = useCoverResolution(resource);
  return null;
}

beforeEach(() => {
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  listeners.clear();
  subscribe.mockReset().mockImplementation(async (_id, origin, _type, callback) => {
    listeners.set(origin, callback);
    return () => listeners.delete(origin);
  });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

const resourceWithOrigins = (...origins: DataOrigin[]) =>
  ({
    id: 12,
    dataStates: origins.map((origin) => ({
      resourceId: 12,
      origin,
      dataType: ResourceDataType.Cover,
      status: DataStatus.NotStarted,
    })),
  }) as Resource;

async function discover(origin: DataOrigin, path: string) {
  await act(async () =>
    listeners.get(origin)!({ origin, dataType: ResourceDataType.Cover, coverPaths: [path] }),
  );
}

describe("external work identity covers", () => {
  it("displays a catalog cover delivered by discovery without a content source", async () => {
    await act(async () =>
      root.render(<Probe resource={resourceWithOrigins(DataOrigin.ExternalIdentity)} />),
    );
    expect(latest.status).toBe("loading");
    await discover(DataOrigin.ExternalIdentity, "/covers/bangumi.jpg");
    expect(latest).toEqual({ covers: ["/covers/bangumi.jpg"], status: "ready" });
  });

  it("keeps platform covers ahead of identity covers and identity covers ahead of filesystem discovery", async () => {
    await act(async () =>
      root.render(
        <Probe
          resource={resourceWithOrigins(
            DataOrigin.FileSystem,
            DataOrigin.ExternalIdentity,
            DataOrigin.Steam,
          )}
        />,
      ),
    );
    await discover(DataOrigin.FileSystem, "/covers/local.jpg");
    await discover(DataOrigin.ExternalIdentity, "/covers/catalog.jpg");
    expect(latest.covers).toEqual(["/covers/catalog.jpg"]);
    await discover(DataOrigin.Steam, "/covers/steam.jpg");
    expect(latest.covers).toEqual(["/covers/steam.jpg"]);
  });
});
