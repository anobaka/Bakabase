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
const listeners = new Map<DataOrigin, (data: DiscoveryData | null, error?: string) => void>();

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

/** Discovery answered, and the answer was "there is no cover here". */
async function discoverNothing(origin: DataOrigin, error?: string) {
  await act(async () =>
    listeners.get(origin)!(error ? null : { origin, dataType: ResourceDataType.Cover }, error),
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

describe("discovery that finds nothing", () => {
  it("settles to not-found instead of spinning forever", async () => {
    // dataStates is a prop and stays NotStarted for the life of the resource object, so
    // without recording that discovery answered, a resource that simply has no cover sat
    // at "loading" for the rest of the session — one spinner per card, animating in a
    // WebView nobody was looking at.
    await act(async () =>
      root.render(<Probe resource={resourceWithOrigins(DataOrigin.FileSystem)} />),
    );
    expect(latest.status).toBe("loading");

    await discoverNothing(DataOrigin.FileSystem);

    expect(latest).toEqual({ covers: null, status: "not-found" });
  });

  it("keeps waiting while another origin has not answered", async () => {
    await act(async () =>
      root.render(
        <Probe resource={resourceWithOrigins(DataOrigin.FileSystem, DataOrigin.Steam)} />,
      ),
    );

    await discoverNothing(DataOrigin.FileSystem);
    expect(latest.status).toBe("loading");

    await discoverNothing(DataOrigin.Steam);
    expect(latest.status).toBe("not-found");
  });

  it("treats a failed discovery as answered too", async () => {
    // A stream that errors is never coming back with a cover. Leaving it "loading" is
    // both a lie and a permanent animation.
    await act(async () =>
      root.render(<Probe resource={resourceWithOrigins(DataOrigin.FileSystem)} />),
    );

    await discoverNothing(DataOrigin.FileSystem, "stream closed");

    expect(latest.status).toBe("not-found");
  });

  it("still prefers a cover that arrives from a lower-priority origin", async () => {
    await act(async () =>
      root.render(
        <Probe resource={resourceWithOrigins(DataOrigin.FileSystem, DataOrigin.Steam)} />,
      ),
    );

    await discoverNothing(DataOrigin.Steam);
    await discover(DataOrigin.FileSystem, "/covers/local.jpg");

    expect(latest).toEqual({ covers: ["/covers/local.jpg"], status: "ready" });
  });
});
