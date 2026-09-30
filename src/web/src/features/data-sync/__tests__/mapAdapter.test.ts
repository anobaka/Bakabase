import type { DataSyncMapView } from "../api";
import type { DeviceGraph, DeviceGraphInput } from "@/features/federation/map/graph";

import { describe, expect, it } from "vitest";

import {
  buildSyncEdges,
  buildSyncOutgoingNodes,
  installIdOf,
  isSyncMapLive,
  syncClaims,
  syncIssues,
  waitsThere,
} from "../map/mapAdapter";

import { keyT, mapPeer, mapRequest, mapView, NOW, ownRequest } from "./dataSyncFixtures";

import { buildDeviceGraph, identityKey } from "@/features/federation/map/graph";
import { directionPhrases } from "@/features/federation/map/describe";
import {
  grant,
  peer,
  server,
  servers,
  sharingRequest,
  shuffled,
  status,
} from "@/features/federation/__tests__/deviceMapFixtures";
import {
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncRequestIntent,
} from "@/sdk/constants";

/*
 * Data sync on the device map (spec §11.1, §13.10): which device each record is drawn on, the
 * line it gets, and what the device's card says — through the map's own `buildDeviceGraph`, so
 * what is tested is what the map draws.
 */

const graphOf = (dataSync: DataSyncMapView, input: Partial<DeviceGraphInput> = {}) =>
  buildDeviceGraph({ ...input, dataSync }, NOW);
const node = (graph: DeviceGraph, id: string) => graph.nodes.find((item) => item.id === id);
const edge = (graph: DeviceGraph, id: string) => graph.edges.find((item) => item.id === id);
const lines = (graph: DeviceGraph) =>
  graph.edges
    .filter((item) => item.kind === "sync")
    .map(({ nodeId, in: into, out, mode, attention }) => ({
      nodeId,
      in: into,
      out,
      mode,
      attention,
    }));

/**
 * This device's own request to Garage, which only it names: waiting on a link that waits for
 * access, or ended — rejected or expired — on a stopped one.
 */
const garage = (ended?: "rejected" | "expired") =>
  mapPeer(
    "garage",
    "Garage",
    ended
      ? {
          id: 3,
          mode: DataSyncLinkMode.Off,
          state: DataSyncLinkState.Stopped,
          peerMayReadUs: false,
        }
      : { id: 3, state: DataSyncLinkState.AwaitingAccess, peerMayReadUs: false },
    {
      request: ownRequest({
        address: "192.168.1.30:34567",
        ...(ended ? { requestId: undefined, outcome: ended, expiresAt: undefined } : {}),
      }),
    },
  );

const attention = {
  headless: true,
  openDecisions: 0,
  pausedLinks: 0,
  restorePending: false,
  awaitingReview: 0,
};

describe("data sync on the device map: which device", () => {
  it("draws a link on the device that carries its install id", () => {
    const graph = graphOf(mapView({ peers: [mapPeer("nas", "NAS")] }), {
      status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }),
      servers: servers({ servers: [server("nas", { name: "NAS" })] }),
    });

    // One device — its sharing record, the server this device manages, and its link.
    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    expect(node(graph, "peer:nas")?.sources.sync?.link?.id).toBe(1);
    expect(edge(graph, "sync:peer:nas")).toMatchObject({
      kind: "sync",
      nodeId: "peer:nas",
      in: "active",
      out: "active",
      mode: "twoWay",
    });
  });

  it("finds a managed server by its install id too", () => {
    const graph = graphOf(mapView({ peers: [mapPeer("attic", "Attic NAS")] }), {
      servers: servers({ servers: [server("attic", { name: "Attic NAS" })] }),
    });

    expect(graph.nodes.map((item) => item.id)).toEqual(["server:attic"]);
    expect(edge(graph, "sync:server:attic")?.nodeId).toBe("server:attic");
  });

  it("creates the device a link names when nothing else knows it", () => {
    const graph = graphOf(
      mapView({ peers: [mapPeer("laptop", "Laptop", { mode: DataSyncLinkMode.Off })] }),
    );

    expect(node(graph, "peer:laptop")).toMatchObject({ name: "Laptop", unverified: false });
    expect(node(graph, "peer:laptop")?.keys).toContain(identityKey.install("laptop"));
  });

  it("never joins a device by name: another install of the same name stays another device", () => {
    const graph = graphOf(mapView({ peers: [mapPeer("nas-2", "NAS")] }), {
      status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }),
    });

    expect(edge(graph, "sync:peer:nas")).toBeUndefined();
    expect(edge(graph, "sync:peer:nas-2")?.nodeId).toBe("peer:nas-2");
    // Said on both, never drawn as one.
    expect(node(graph, "peer:nas")?.namesakes.map((item) => item.nodeId)).toEqual(["peer:nas-2"]);
  });

  it("puts a request of this device's own to a device that is no peer yet on a device of its own", () => {
    const graph = graphOf(mapView({ peers: [garage()] }));
    const device = node(graph, "peer:garage");

    expect(device).toMatchObject({ name: "Garage", address: "192.168.1.30:34567" });
    expect(device?.keys).toEqual(
      expect.arrayContaining([
        identityKey.install("garage"),
        identityKey.address("192.168.1.30:34567"),
      ]),
    );
    expect(device?.sources.sync?.request?.outcome).toBe("awaitingApproval");
    // Waiting for access: the receive direction waits.
    expect(edge(graph, "sync:peer:garage")).toMatchObject({ in: "pending", out: "none" });
  });

  it("keeps a request that ended on its device, to be dismissed there, without drawing it", () => {
    const ended = garage("rejected");
    const graph = graphOf(mapView({ peers: [ended] }));

    expect(node(graph, "peer:garage")?.sources.sync).toEqual(ended);
    expect(edge(graph, "sync:peer:garage")).toBeUndefined();
    // It stays among the devices, not dropped with its line.
    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:garage"]);
  });

  it("puts a request of this device's own on the device it already knows, by id", () => {
    const graph = graphOf(
      mapView({
        peers: [
          mapPeer(
            "nas",
            "NAS",
            { id: 3, state: DataSyncLinkState.AwaitingAccess, peerMayReadUs: false },
            { request: ownRequest({ address: "192.168.1.30:34567" }) },
          ),
        ],
      }),
      { status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }) },
    );

    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    expect(node(graph, "peer:nas")?.sources.sync?.request).toBeDefined();
    // Its address stays the one its own record gives.
    expect(node(graph, "peer:nas")?.address).toBe("http://192.168.1.13:34567");
  });

  it("does not depend on the order the records come in", () => {
    const view = mapView({
      peers: [
        mapPeer("a", "A"),
        mapPeer("b", "B", { id: 2 }),
        mapPeer("c", "C", { id: 3 }),
        mapPeer("d", "D", null, { reader: undefined, request: ownRequest() }),
        mapPeer("e", "E", null, { reader: undefined, request: ownRequest({ requestId: "r-e" }) }),
      ],
      requests: [mapRequest("r1", "x", "X"), mapRequest("r2", "y", "Y")],
    });
    const reordered = {
      ...view,
      peers: shuffled(view.peers),
      requests: shuffled(view.requests, 3),
    };

    expect(graphOf(reordered)).toEqual(graphOf(view));
  });

  it("never draws this device itself", () => {
    const graph = graphOf(
      mapView({
        peers: [mapPeer("self-node", "Studio PC")],
        requests: [mapRequest("r", "self-node", "Studio PC")],
      }),
      { status: status() },
    );

    expect(graph.nodes).toEqual([]);
    expect(graph.edges).toEqual([]);
  });
});

describe("data sync on the device map: the line", () => {
  const one = (patch: Parameters<typeof mapPeer>[2]) =>
    lines(graphOf(mapView({ peers: [mapPeer("nas", "NAS", patch)] })))[0];

  it("points to the device that receives: this device, it, or both", () => {
    expect(one({ peerMayReadUs: false })).toMatchObject({
      in: "active",
      out: "none",
      mode: "twoWay",
    });
    expect(
      one({ mode: DataSyncLinkMode.Follow, peerModeTowardsUs: undefined, peerMayReadUs: false }),
    ).toMatchObject({
      in: "active",
      out: "none",
      mode: "follow",
    });
    // A grant alone: it reads this device, this device does not receive.
    expect(one(null)).toEqual({
      nodeId: "peer:nas",
      in: "none",
      out: "active",
      mode: undefined,
      attention: undefined,
    });
  });

  it("says both ways where each device receives from the other", () => {
    expect(one({ mode: DataSyncLinkMode.Follow, peerModeTowardsUs: "follow" })?.mode).toBe(
      "twoWay",
    );
  });

  it("draws waiting for access or for a review as pending", () => {
    for (const state of [
      DataSyncLinkState.AwaitingAccess,
      DataSyncLinkState.AwaitingReview,
      DataSyncLinkState.WaitingForPeerReview,
    ])
      expect(one({ state })?.in, String(state)).toBe("pending");
  });

  it("says a line waiting for the first review waits for that, not for access", () => {
    const words = (state: DataSyncLinkState) => {
      const graph = graphOf(
        mapView({
          peers: [mapPeer("nas", "NAS", { state })],
        }),
      );
      const line = graph.edges.find((item) => item.kind === "sync")!;

      return { inReview: line.inReview, phrases: directionPhrases(keyT, line, "NAS") };
    };

    expect(words(DataSyncLinkState.AwaitingAccess)).toEqual({
      inReview: undefined,
      phrases: expect.arrayContaining(["federation.map.direction.sync.in.pending NAS"]),
    });
    for (const state of [DataSyncLinkState.AwaitingReview, DataSyncLinkState.WaitingForPeerReview])
      expect(words(state), String(state)).toEqual({
        inReview: true,
        phrases: expect.arrayContaining(["federation.map.direction.sync.in.review NAS"]),
      });
  });

  it("marks what does not work on the receive direction, and never an offline device", () => {
    const issue = (patch: Parameters<typeof mapPeer>[2]) => one(patch)?.attention;

    expect(
      issue({ state: DataSyncLinkState.Paused, pausedReason: DataSyncPauseReason.ByUser }),
    ).toEqual({ direction: "in", issue: "syncPaused" });
    for (const lastErrorCode of ["PeerTooOld", "ThisTooOld"])
      expect(issue({ state: DataSyncLinkState.Active, lastErrorCode })?.issue).toBe(
        "syncUpdateNeeded",
      );
    for (const lastErrorCode of ["AccessRevoked", "PeerSharingOff", "PeerRemoteAccessOff"])
      expect(issue({ state: DataSyncLinkState.Active, lastErrorCode })?.issue, lastErrorCode).toBe(
        "syncAccessLost",
      );
    expect(
      issue({ state: DataSyncLinkState.Active, lastErrorCode: "InvalidResponse" })?.issue,
    ).toBe("syncFailed");
    // Unreachable is offline, which is not a failure.
    expect(
      issue({ state: DataSyncLinkState.Active, lastErrorCode: "Unreachable" }),
    ).toBeUndefined();
    // Reading back a device this one approved to keep in step failed: waiting on nobody.
    expect(
      issue({
        state: DataSyncLinkState.AwaitingAccess,
        initiator: DataSyncLinkInitiator.Peer,
        lastErrorCode: "ReadBackFailed",
        lastErrorDetail: "Unreachable",
      })?.issue,
    ).toBe("syncFailed");
    // A paused link is still set up: drawn, dotted, with the mark — never taken off the map.
    expect(one({ state: DataSyncLinkState.Paused })?.in).toBe("active");
  });

  it("draws a stopped link with nothing going either way as no line", () => {
    const graph = graphOf(
      mapView({
        peers: [
          mapPeer("nas", "NAS", {
            mode: DataSyncLinkMode.Off,
            state: DataSyncLinkState.Stopped,
            peerMayReadUs: false,
          }),
        ],
      }),
    );

    expect(edge(graph, "sync:peer:nas")).toBeUndefined();
    // The device stays, with its link, to be turned on again from its details.
    expect(node(graph, "peer:nas")?.sources.sync?.link?.id).toBe(1);
  });
});

describe("data sync on the device map: what a device's card says", () => {
  const issuesOf = (patch: Parameters<typeof mapPeer>[2]) =>
    node(graphOf(mapView({ peers: [mapPeer("nas", "NAS", patch)] })), "peer:nas")?.issues;

  it("says what needs this device, and what does not work", () => {
    expect(issuesOf({})).toEqual([]);
    expect(issuesOf({ openItems: 3 })).toEqual(["syncNeedsYou"]);
    expect(issuesOf({ state: DataSyncLinkState.Paused, openItems: 1 })).toEqual([
      "syncNeedsYou",
      "syncPaused",
    ]);
  });

  it("says decisions wait on a headless device, from what it reported", () => {
    expect(issuesOf({ peerAttention: { ...attention, openDecisions: 2 } })).toEqual([
      "syncNeedsYouThere",
    ]);
    expect(issuesOf({ peerAttention: { ...attention, pausedLinks: 1 } })).toEqual([
      "syncNeedsYouThere",
    ]);
    expect(issuesOf({ peerAttention: { ...attention, restorePending: true } })).toEqual([
      "syncNeedsYouThere",
    ]);
    // A desktop decides where it is used; one waiting only for a review asks nothing there.
    expect(
      issuesOf({ peerAttention: { ...attention, headless: false, openDecisions: 2 } }),
    ).toEqual([]);
    expect(issuesOf({ peerAttention: { ...attention, awaitingReview: 1 } })).toEqual([]);
    expect(waitsThere(undefined)).toBe(false);
  });

  it("says nothing of data sync for a device it has nothing to do with", () => {
    expect(syncIssues({})).toEqual([]);
  });

  it("marks a device whose request from this device ended, until it is dismissed", () => {
    const graph = graphOf(mapView({ peers: [garage("expired")] }));

    // Its own words, not the words for an ended request to manage a device.
    expect(node(graph, "peer:garage")?.issues).toEqual(["syncRequestEnded"]);
    // Still waiting: nothing to mark.
    expect(node(graphOf(mapView({ peers: [garage()] })), "peer:garage")?.issues).toEqual([]);
  });
});

describe("data sync on the device map: requests other devices filed", () => {
  it("draws each on a device of its own, unverified, asking to read this device", () => {
    const graph = graphOf(mapView({ requests: [mapRequest("r1", "newpc", "New PC")] }));
    const claim = node(graph, "sync-request:r1");

    expect(claim).toMatchObject({ name: "New PC", unverified: true });
    expect(claim?.keys).toEqual([identityKey.request("r1")]);
    expect(edge(graph, "sync:sync-request:r1")).toMatchObject({ out: "pending", in: "none" });
  });

  it("offers its own definitions back when it asks to keep in step both ways", () => {
    const graph = graphOf(
      mapView({
        requests: [mapRequest("r1", "newpc", "New PC", { intent: DataSyncRequestIntent.TwoWay })],
      }),
    );

    expect(edge(graph, "sync:sync-request:r1")).toMatchObject({ out: "pending", in: "pending" });
  });

  it("is never merged into the device it claims to be, which it names", () => {
    const graph = graphOf(
      mapView({
        peers: [mapPeer("nas", "NAS")],
        requests: [
          mapRequest("r1", "nas", "NAS", {
            remoteAddress: "192.168.1.99",
            claimsKnownDevice: true,
            knownAddress: "192.168.1.20:34567",
          }),
        ],
      }),
      { status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }) },
    );

    expect(node(graph, "peer:nas")?.sources.syncRequests).toEqual([]);
    expect(node(graph, "sync-request:r1")?.claimsToBe).toMatchObject({ nodeId: "peer:nas" });
    // The device's own line is its link's; the claim has its own.
    expect(edge(graph, "sync:peer:nas")?.out).toBe("active");
    expect(edge(graph, "sync:sync-request:r1")?.out).toBe("pending");
    // It lends the claim nothing.
    expect(node(graph, "sync-request:r1")?.kind).toBe("unknown");
  });

  it("draws the claims of one requester as one device: the same address and the same name", () => {
    const graph = graphOf(
      mapView({
        requests: [
          mapRequest("r1", "newpc", "New PC", { remoteAddress: "192.168.1.40" }),
          mapRequest("r2", "newpc-2", "new pc", { remoteAddress: "192.168.1.40:51000" }),
          // Another place, or another name: another requester.
          mapRequest("r3", "newpc", "New PC", { remoteAddress: "192.168.1.41" }),
          mapRequest("r4", "newpc", "Old PC", { remoteAddress: "192.168.1.40" }),
        ],
      }),
      {
        // …and a library request from the same requester joins them.
        status: status({
          requests: [
            sharingRequest("newpc", "incoming", {
              requestId: "s1",
              nodeName: "New PC",
              remoteAddress: "192.168.1.40",
            }),
          ],
        }),
      },
    );
    const claims = graph.nodes.filter((item) => item.unverified);

    expect(claims.map((item) => item.id).sort()).toEqual([
      "sharing-request:s1",
      "sync-request:r3",
      "sync-request:r4",
    ]);
    expect(node(graph, "sharing-request:s1")?.sources.syncRequests.map((r) => r.requestId)).toEqual(
      ["r1", "r2"],
    );
    expect(edge(graph, "sync:sharing-request:s1")?.out).toBe("pending");
    expect(edge(graph, "sharing:sharing-request:s1")?.out).toBe("pending");
  });

  it("leaves out a request whose time is over, read as UTC", () => {
    const view = mapView({
      requests: [
        mapRequest("live", "a", "A", { expiresAt: "2026-09-01 08:30:00.000" }),
        mapRequest("over", "b", "B", { expiresAt: "2026-09-01 07:59:00.000" }),
      ],
    });

    expect(syncClaims(view, NOW).map((request) => request.requestId)).toEqual(["live"]);
  });
});

describe("data sync on the device map: the pieces", () => {
  it("lists this device's own records in id order, keyed by where its request went", () => {
    const records = buildSyncOutgoingNodes(
      mapView({ peers: [garage(), mapPeer("a", "A", { id: 2 })] }),
    );

    expect(records.map((record) => record.nodeId)).toEqual(["a", "garage"]);
    expect(records[1]).toMatchObject({ address: "192.168.1.30:34567", peer: { nodeId: "garage" } });
    expect(records[1].keys).toContain(identityKey.address("192.168.1.30:34567"));
    expect(buildSyncOutgoingNodes(undefined)).toEqual([]);
  });

  it("draws nothing for a device the map does not carry", () => {
    const view = mapView({ peers: [mapPeer("nas", "NAS")] });

    expect(buildSyncEdges(view, [])).toEqual([]);
    // Never on a device found nearby, whatever id it carries.
    expect(
      buildSyncEdges(view, [
        { id: "ghost:x", self: false, ghost: true, unverified: false, keys: ["id:nas"] },
      ]),
    ).toEqual([]);
  });

  it("reads the install id a device is known by", () => {
    expect(installIdOf(["address:host:1", identityKey.install("nas")])).toBe("nas");
    expect(installIdOf([identityKey.device("d1")])).toBeUndefined();
  });

  it("is read again sooner while something waits on someone", () => {
    expect(isSyncMapLive(undefined, NOW)).toBe(false);
    expect(isSyncMapLive(mapView({ peers: [mapPeer("nas", "NAS")] }), NOW)).toBe(false);
    expect(isSyncMapLive(mapView({ requests: [mapRequest("r", "a", "A")] }), NOW)).toBe(true);
    expect(
      isSyncMapLive(mapView({ peers: [mapPeer("a", "A", null, { request: ownRequest() })] }), NOW),
    ).toBe(true);
    expect(
      isSyncMapLive(
        mapView({ peers: [mapPeer("nas", "NAS", { state: DataSyncLinkState.AwaitingAccess })] }),
        NOW,
      ),
    ).toBe(true);
  });
});
