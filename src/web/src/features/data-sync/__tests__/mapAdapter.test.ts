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

import { mapPeer, mapRequest, mapView, minutesAgo, NOW, outgoing } from "./dataSyncFixtures";

import { buildDeviceGraph, identityKey } from "@/features/federation/map/graph";
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
    expect(node(graph, "peer:nas")?.sources.sync?.linkId).toBe(1);
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
      mapView({ peers: [mapPeer("laptop", "Laptop", { peerMayRead: true, mode: 0 })] }),
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
    const graph = graphOf(mapView({ outgoing: [outgoing(3, "garage", "Garage")] }));
    const garage = node(graph, "peer:garage");

    expect(garage).toMatchObject({ name: "Garage", address: "192.168.1.30:34567" });
    expect(garage?.keys).toEqual(
      expect.arrayContaining([
        identityKey.install("garage"),
        identityKey.address("192.168.1.30:34567"),
      ]),
    );
    expect(garage?.sources.syncOutgoing.map((item) => item.linkId)).toEqual([3]);
    // Waiting for access: the receive direction waits.
    expect(edge(graph, "sync:peer:garage")).toMatchObject({ in: "pending", out: "none" });
  });

  it("keeps a request that ended on its device, to be dismissed there, without drawing it", () => {
    const ended = outgoing(3, "garage", "Garage", {
      state: DataSyncLinkState.Stopped,
      outcome: "rejected",
      expiresAt: minutesAgo(5),
    });
    const graph = graphOf(mapView({ outgoing: [ended] }));

    expect(node(graph, "peer:garage")?.sources.syncOutgoing).toEqual([ended]);
    expect(edge(graph, "sync:peer:garage")).toBeUndefined();
    // It stays among the devices, not dropped with its line.
    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:garage"]);
  });

  it("puts a request of this device's own on the device it already knows, by id", () => {
    const graph = graphOf(mapView({ outgoing: [outgoing(3, "nas", "NAS")] }), {
      status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }),
    });

    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    expect(node(graph, "peer:nas")?.sources.syncOutgoing).toHaveLength(1);
    // Its address stays the one its own record gives.
    expect(node(graph, "peer:nas")?.address).toBe("http://192.168.1.13:34567");
  });

  it("does not depend on the order the records come in", () => {
    const view = mapView({
      peers: [
        mapPeer("a", "A"),
        mapPeer("b", "B", { linkId: 2 }),
        mapPeer("c", "C", { linkId: 3 }),
      ],
      outgoing: [outgoing(4, "d", "D"), outgoing(5, "e", "E")],
      requests: [mapRequest("r1", "x", "X"), mapRequest("r2", "y", "Y")],
    });
    const reordered = {
      ...view,
      peers: shuffled(view.peers),
      outgoing: shuffled(view.outgoing),
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
    expect(one({ peerMayRead: false })).toMatchObject({
      in: "active",
      out: "none",
      mode: "twoWay",
    });
    expect(
      one({ mode: DataSyncLinkMode.Follow, peerMode: undefined, peerMayRead: false }),
    ).toMatchObject({
      in: "active",
      out: "none",
      mode: "follow",
    });
    // A grant alone: it reads this device, this device does not receive.
    expect(
      one({
        linkId: undefined,
        mode: DataSyncLinkMode.Off,
        state: undefined,
        receiving: false,
        peerMayRead: true,
      }),
    ).toEqual({
      nodeId: "peer:nas",
      in: "none",
      out: "active",
      mode: undefined,
      attention: undefined,
    });
  });

  it("says both ways where each device receives from the other", () => {
    expect(one({ mode: DataSyncLinkMode.Follow, peerMode: "follow" })?.mode).toBe("twoWay");
  });

  it("draws waiting for access or for a review as pending", () => {
    for (const state of [
      DataSyncLinkState.AwaitingAccess,
      DataSyncLinkState.AwaitingReview,
      DataSyncLinkState.WaitingForPeerReview,
    ])
      expect(one({ state, receiving: false, receivingPending: true })?.in, String(state)).toBe(
        "pending",
      );
  });

  it("marks what does not work on the receive direction, and never an offline device", () => {
    const issue = (patch: Parameters<typeof mapPeer>[2]) =>
      one({ receiving: false, ...patch })?.attention;

    expect(
      issue({ state: DataSyncLinkState.Paused, pausedReason: DataSyncPauseReason.MassDeletion }),
    ).toEqual({ direction: "in", issue: "syncPaused" });
    expect(issue({ state: DataSyncLinkState.PeerTooOld })?.issue).toBe("syncUpdateNeeded");
    expect(issue({ state: DataSyncLinkState.ThisTooOld })?.issue).toBe("syncUpdateNeeded");
    for (const state of [
      DataSyncLinkState.AccessRevoked,
      DataSyncLinkState.PeerSharingOff,
      DataSyncLinkState.PeerRemoteAccessOff,
    ])
      expect(issue({ state })?.issue, String(state)).toBe("syncAccessLost");
    expect(
      issue({ state: DataSyncLinkState.Active, lastErrorCode: "InvalidResponse" })?.issue,
    ).toBe("syncFailed");
    // Unreachable is offline, which is not a failure.
    expect(
      issue({ state: DataSyncLinkState.Active, lastErrorCode: "Unreachable" }),
    ).toBeUndefined();
    // A paused link is still set up: drawn, dotted, with the mark — never taken off the map.
    expect(one({ state: DataSyncLinkState.Paused, receiving: false })?.in).toBe("active");
  });

  it("draws a stopped link with nothing going either way as no line", () => {
    const graph = graphOf(
      mapView({
        peers: [
          mapPeer("nas", "NAS", {
            mode: DataSyncLinkMode.Off,
            state: DataSyncLinkState.Stopped,
            receiving: false,
            peerMayRead: false,
          }),
        ],
      }),
    );

    expect(edge(graph, "sync:peer:nas")).toBeUndefined();
    // The device stays, with its link, to be turned on again from its details.
    expect(node(graph, "peer:nas")?.sources.sync?.linkId).toBe(1);
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
    expect(issuesOf({ attention: { ...attention, openDecisions: 2 } })).toEqual([
      "syncNeedsYouThere",
    ]);
    expect(issuesOf({ attention: { ...attention, pausedLinks: 1 } })).toEqual([
      "syncNeedsYouThere",
    ]);
    expect(issuesOf({ attention: { ...attention, restorePending: true } })).toEqual([
      "syncNeedsYouThere",
    ]);
    // A desktop decides where it is used; one waiting only for a review asks nothing there.
    expect(issuesOf({ attention: { ...attention, headless: false, openDecisions: 2 } })).toEqual(
      [],
    );
    expect(issuesOf({ attention: { ...attention, awaitingReview: 1 } })).toEqual([]);
    expect(waitsThere(undefined)).toBe(false);
  });

  it("says nothing of data sync for a device it has nothing to do with", () => {
    expect(syncIssues({ syncOutgoing: [] })).toEqual([]);
  });

  it("marks a device whose request from this device ended, until it is dismissed", () => {
    const graph = graphOf(
      mapView({
        outgoing: [
          outgoing(3, "garage", "Garage", {
            state: DataSyncLinkState.Stopped,
            outcome: "expired",
            expiresAt: minutesAgo(5),
          }),
        ],
      }),
    );

    // Its own words, not the words for an ended request to manage a device.
    expect(node(graph, "peer:garage")?.issues).toEqual(["syncRequestEnded"]);
    // Still waiting: nothing to mark.
    expect(
      node(graphOf(mapView({ outgoing: [outgoing(3, "garage", "Garage")] })), "peer:garage")
        ?.issues,
    ).toEqual([]);
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
  it("lists this device's own records once per device, in id order", () => {
    const records = buildSyncOutgoingNodes(
      mapView({
        peers: [mapPeer("b", "B"), mapPeer("a", "A", { linkId: 2 })],
        outgoing: [outgoing(3, "b", "B", { state: DataSyncLinkState.AwaitingAccess })],
      }),
    );

    expect(records.map((record) => record.nodeId)).toEqual(["a", "b"]);
    expect(records[1]).toMatchObject({ peer: { nodeId: "b" }, outgoing: { linkId: 3 } });
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
    expect(isSyncMapLive(mapView({ outgoing: [outgoing(3, "a", "A")] }), NOW)).toBe(true);
    expect(
      isSyncMapLive(
        mapView({ peers: [mapPeer("nas", "NAS", { state: DataSyncLinkState.AwaitingAccess })] }),
        NOW,
      ),
    ).toBe(true);
  });
});
