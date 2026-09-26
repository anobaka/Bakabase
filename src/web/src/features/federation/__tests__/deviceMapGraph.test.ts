import type { DeviceGraph, MapEdge } from "../map/graph";
import type { DataSyncMapPeer, DataSyncMapView } from "@/features/data-sync/api";

import { describe, expect, it } from "vitest";

import {
  addressKey,
  buildDeviceGraph,
  hostKey,
  ownHostsOf,
  sameMachine,
  SELF_ID,
  withScheme,
} from "../map/graph";

import {
  aMinuteAgo,
  access,
  grant,
  inTenMinutes,
  manager,
  managementRequestIn,
  managementRequestOut,
  peer,
  server,
  servers,
  sharingRequest,
  shuffled,
  status,
} from "./deviceMapFixtures";

import {
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncRequestIntent,
  ManagedServerOutcome,
  ManagedServerState,
  RemoteAccessMode,
  RemoteDevicePlatform,
  ServerKind,
} from "@/sdk/constants";

const edge = (graph: DeviceGraph, id: string) => graph.edges.find((item) => item.id === id);
const node = (graph: DeviceGraph, id: string) => graph.nodes.find((item) => item.id === id);
const directions = (item?: MapEdge) => item && { out: item.out, in: item.in };

describe("device map model: this device", () => {
  it("is the desktop app where it can manage others, a headless server where it cannot", () => {
    expect(buildDeviceGraph({ servers: servers({ available: true }) }).self.kind).toBe("desktop");
    expect(buildDeviceGraph({ servers: servers({ available: false }) }).self.kind).toBe("server");
    // Not guessed when the listing could not be read.
    expect(buildDeviceGraph({}).self.kind).toBe("unknown");
  });

  it("is named by its sharing identity, or by the server name when sharing could not be read", () => {
    expect(buildDeviceGraph({ status: status(), selfName: "fallback" }).self.name).toBe(
      "Studio PC",
    );
    expect(buildDeviceGraph({ selfName: "fallback" }).self).toMatchObject({
      id: SELF_ID,
      name: "fallback",
      self: true,
      presence: "online",
    });
  });

  it("never lists itself among the others, even when a listing names it", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [peer("self-node")],
        requests: [sharingRequest("self-node", "incoming")],
      }),
      discovery: {
        sharing: [{ nodeId: "self-node", name: "Studio PC", address: "http://192.168.1.2:34567" }],
      },
    });

    expect(graph.nodes).toEqual([]);
    expect(graph.edges).toEqual([]);
  });
});

describe("device map model: library sharing", () => {
  it("points from the library to the device that may browse it, one way or both", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("reads", { outboundGrant: grant("g1") }),
          peer("readby", { inboundGrant: grant("g2") }),
          peer("both", { outboundGrant: grant("g3"), inboundGrant: grant("g4") }),
        ],
      }),
    });

    // This device may browse it: towards this device.
    expect(directions(edge(graph, "sharing:peer:reads"))).toEqual({ out: "none", in: "active" });
    // It may browse this device: towards it.
    expect(directions(edge(graph, "sharing:peer:readby"))).toEqual({ out: "active", in: "none" });
    expect(directions(edge(graph, "sharing:peer:both"))).toEqual({ out: "active", in: "active" });
    expect(graph.edges.every((item) => item.kind === "sharing")).toBe(true);
  });

  it("draws requests as pending, and a device known only by one as unverified when it asked", () => {
    const graph = buildDeviceGraph({
      status: status({
        requests: [
          sharingRequest("asker", "incoming"),
          sharingRequest("asked", "outgoing"),
          sharingRequest("both", "incoming", { offersReciprocalAccess: true }),
        ],
      }),
    });

    expect(directions(edge(graph, "sharing:sharing-request:incoming-asker"))).toEqual({
      out: "pending",
      in: "none",
    });
    expect(directions(edge(graph, "sharing:peer:asked"))).toEqual({ out: "none", in: "pending" });
    // Approving a reciprocal request opens both ways.
    expect(directions(edge(graph, "sharing:sharing-request:incoming-both"))).toEqual({
      out: "pending",
      in: "pending",
    });
    // Its name and ID are its own claims. This device's own request went to an address it chose.
    expect(node(graph, "sharing-request:incoming-asker")?.unverified).toBe(true);
    expect(node(graph, "peer:asked")?.unverified).toBe(false);
  });

  it("keeps a request that claims a paired device's identity off that device", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [peer("nas", { outboundGrant: grant("g1") })],
        requests: [sharingRequest("nas", "incoming", { remoteAddress: "192.168.1.66" })],
      }),
    });

    // The paired device's own line says only what it has.
    expect(directions(edge(graph, "sharing:peer:nas"))).toEqual({ out: "none", in: "active" });
    expect(node(graph, "peer:nas")?.unverified).toBe(false);
    // The request is its own device, which says whom it claims to be and where that one is.
    expect(directions(edge(graph, "sharing:sharing-request:incoming-nas"))).toEqual({
      out: "pending",
      in: "none",
    });
    expect(node(graph, "sharing-request:incoming-nas")).toMatchObject({
      unverified: true,
      claimsToBe: { nodeId: "peer:nas", name: "NAS", address: "http://192.168.1.13:34567" },
    });
  });

  it("ignores requests that expired or were decided", () => {
    const graph = buildDeviceGraph({
      status: status({
        requests: [
          sharingRequest("late", "incoming", { expiresAt: aMinuteAgo() }),
          sharingRequest("done", "outgoing", { status: "granted" }),
          sharingRequest("no", "outgoing", { status: "rejected" }),
        ],
      }),
    });

    expect(graph.nodes).toEqual([]);
  });

  it("says how a peer answered, and flags one whose identity could not be confirmed", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("up", { outboundGrant: grant("g1") }),
          peer("down", { outboundGrant: grant("g2"), connectionState: "Offline" }),
          peer("odd", { outboundGrant: grant("g3"), connectionState: "IdentityConflict" }),
          peer("quiet", { inboundGrant: grant("g4"), connectionState: "Unknown" }),
        ],
      }),
    });

    expect(node(graph, "peer:up")?.presence).toBe("online");
    expect(node(graph, "peer:down")?.presence).toBe("offline");
    expect(node(graph, "peer:odd")).toMatchObject({
      presence: "unknown",
      issues: ["identityConflict"],
    });
    // This device's reading of its library is what does not work.
    expect(edge(graph, "sharing:peer:odd")?.attention).toEqual({
      direction: "in",
      issue: "identityConflict",
    });
    expect(node(graph, "peer:quiet")?.presence).toBe("unknown");
  });
});

describe("device map model: management", () => {
  it("points from this device to the servers it manages", () => {
    const graph = buildDeviceGraph({
      servers: servers({ servers: [server("nas", { state: ManagedServerState.Offline })] }),
    });

    expect(directions(edge(graph, "management:server:nas"))).toEqual({ out: "active", in: "none" });
    expect(node(graph, "server:nas")).toMatchObject({ presence: "offline", appVersion: "2.4.0" });
  });

  it("points at this device from the devices that manage it, and from those asking to", () => {
    const graph = buildDeviceGraph({
      access: access({
        devices: [
          manager("d1", "Laptop"),
          manager("d2", "Phone", { platform: RemoteDevicePlatform.IOS }),
        ],
        pendingRequests: [managementRequestIn("r1", "Office PC")],
      }),
    });

    expect(directions(edge(graph, "management:manager:d1"))).toEqual({ out: "none", in: "active" });
    // Only desktop apps and phones pair: the platform says which.
    expect(node(graph, "manager:d1")).toMatchObject({
      kind: "desktop",
      platform: RemoteDevicePlatform.Windows,
    });
    expect(node(graph, "manager:d2")).toMatchObject({
      kind: "mobile",
      platform: RemoteDevicePlatform.IOS,
    });
    expect(directions(edge(graph, "management:manager-request:r1"))).toEqual({
      out: "none",
      in: "pending",
    });
    // A request says what it is; the map does not take its word for it.
    expect(node(graph, "manager-request:r1")).toMatchObject({ unverified: true, kind: "unknown" });
    expect(node(graph, "manager-request:r1")?.platform).toBeUndefined();
  });

  it("shows a request to manage another device while it waits, and the outcome once it ended", () => {
    const graph = buildDeviceGraph({
      servers: servers({
        requests: [
          managementRequestOut("live"),
          // Still waiting although the last claim failed: the app keeps asking.
          managementRequestOut("retrying", {
            address: "http://192.168.1.91:34567",
            serverName: "Den",
            outcome: ManagedServerOutcome.Unreachable,
          }),
          managementRequestOut("ended", {
            address: "http://192.168.1.92:34567",
            serverName: "Gone",
            outcome: ManagedServerOutcome.RequestRejected,
            active: false,
          }),
        ],
      }),
    });

    expect(directions(edge(graph, "management:server-request:live"))).toEqual({
      out: "pending",
      in: "none",
    });
    expect(directions(edge(graph, "management:server-request:retrying"))).toEqual({
      out: "pending",
      in: "none",
    });
    // Until it is dismissed, on a device of its own when it belongs to no other: no line,
    // but a mark that says it needs a look.
    expect(node(graph, "server-request:ended")).toMatchObject({
      name: "Gone",
      issues: ["requestEnded"],
      address: "http://192.168.1.92:34567",
    });
    expect(edge(graph, "management:server-request:ended")).toBeUndefined();
  });

  it("marks a server whose address answers as another, and never offers that address", () => {
    const graph = buildDeviceGraph({
      servers: servers({
        servers: [
          server("moved", {
            state: ManagedServerState.WrongServer,
            answeredBy: { serverId: "other", name: "Other", isThisDevice: false },
          }),
        ],
      }),
    });
    const moved = node(graph, "server:moved")!;

    expect(moved).toMatchObject({
      presence: "unknown",
      issues: ["wrongServer"],
      address: undefined,
    });
    expect(edge(graph, "management:server:moved")).toMatchObject({
      out: "active",
      attention: { direction: "out", issue: "wrongServer" },
    });
  });

  it("flags a revoked server and one that anyone on its network can manage", () => {
    const graph = buildDeviceGraph({
      servers: servers({
        servers: [
          server("gone", { state: ManagedServerState.Revoked }),
          server("open", { mode: RemoteAccessMode.Unrestricted }),
        ],
      }),
    });

    expect(node(graph, "server:gone")).toMatchObject({ presence: "online", issues: ["revoked"] });
    expect(edge(graph, "management:server:gone")?.attention).toEqual({
      direction: "out",
      issue: "revoked",
    });
    expect(node(graph, "server:open")?.issues).toEqual(["unrestricted"]);
    expect(edge(graph, "management:server:open")?.attention).toBeUndefined();
  });
});

describe("device map model: one device, several records", () => {
  it("joins a sharing peer, a managed server and its beacon by the install id they share", () => {
    // What real hosts answer: the sharing NodeId is the install's ServerId, while the names
    // and the way the address is written differ from one listing to the next.
    const install = "1433c80b9f6c4c1e8d6f0f1e2a3b4c5d";
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer(install, {
            label: "jaxs-Mac-mini",
            address: "http://127.0.0.1:45102",
            outboundGrant: grant("g1"),
          }),
        ],
      }),
      servers: servers({
        servers: [server(install, { name: "fixture-nas", address: "http://localhost:45102" })],
      }),
      discovery: {
        sharing: [{ nodeId: install, name: "jaxs-Mac-mini", address: "http://10.0.0.20:45102" }],
        management: [
          {
            serverId: install,
            name: "fixture-nas",
            address: "http://10.0.0.20:45102",
            appVersion: "2.4.0",
            alreadyManaged: true,
          },
        ],
      },
    });

    expect(graph.nodes.map((item) => item.id)).toEqual([`peer:${install}`]);
    const nas = graph.nodes[0];

    // The name its sharing handshake proved; the address management pairs with.
    expect(nas.name).toBe("jaxs-Mac-mini");
    expect(nas.address).toBe("http://localhost:45102");
    expect(nas.sources.server?.serverId).toBe(install);
    expect(nas.sources.managementCandidate?.serverId).toBe(install);
    expect(nas.sources.sharingCandidate?.nodeId).toBe(install);
    expect(graph.edges.map((item) => item.id)).toEqual([
      `sharing:peer:${install}`,
      `management:peer:${install}`,
    ]);
  });

  it("keeps two installs apart when their ids differ, even at one address or under one name", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("nas", {
            label: "Office PC",
            address: "192.168.1.5:34567",
            outboundGrant: grant("g1"),
          }),
        ],
      }),
      servers: servers({
        servers: [
          server("srv-nas", { name: "NAS box", address: "http://192.168.1.5:34567" }),
          server("srv-pc", {
            name: "office pc",
            address: "http://192.168.1.5:39999",
            state: ManagedServerState.Offline,
          }),
        ],
      }),
    });

    expect(graph.nodes.map((item) => item.id).sort()).toEqual([
      "peer:nas",
      "server:srv-nas",
      "server:srv-pc",
    ]);
  });

  it("joins a managed server to a peer by id even while its address answers as another", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [peer("nas", { address: "http://192.168.1.74:5000", outboundGrant: grant("g1") })],
      }),
      servers: servers({
        servers: [
          server("nas", {
            address: "http://192.168.1.54:5000",
            state: ManagedServerState.WrongServer,
          }),
        ],
      }),
    });

    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    // Where it is now, never the address another server answers at.
    expect(graph.nodes[0].address).toBe("http://192.168.1.74:5000");
    expect(graph.nodes[0].issues).toEqual(["wrongServer"]);
  });

  it("never joins by an address that answers as another server", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("b", {
            label: "B",
            address: "http://192.168.1.8:40001",
            outboundGrant: grant("g1"),
          }),
        ],
      }),
      servers: servers({
        servers: [
          server("a", {
            name: "A",
            address: "http://192.168.1.8:40001",
            state: ManagedServerState.WrongServer,
          }),
        ],
      }),
    });

    expect(graph.nodes.map((item) => item.id).sort()).toEqual(["peer:b", "server:a"]);
  });

  it("joins records by name only where neither carries an install id", () => {
    // A request filed to a server that did not say who it is, and a device that manages this
    // one: neither has an install id, and the name is one device's.
    const graph = buildDeviceGraph({
      servers: servers({
        requests: [
          managementRequestOut("r1", { serverName: "Laptop", address: "http://192.168.1.90:5000" }),
          // Asked again on the same host, at another port: the same name, still no id.
          managementRequestOut("r2", { serverName: "laptop", address: "http://192.168.1.90:5001" }),
        ],
      }),
      access: access({ devices: [manager("d1", "LAPTOP")] }),
    });

    expect(graph.nodes.map((item) => item.id)).toEqual(["server-request:r1"]);
    const lap = graph.nodes[0];

    expect(lap.sources.managers.map((item) => item.id)).toEqual(["d1"]);
    expect(lap.sources.managementRequestsOut.map((item) => item.requestId)).toEqual(["r1", "r2"]);
    expect(lap.sources.managersMatchedByName).toBe(true);
    expect(lap.namesakes).toEqual([]);
    // Only the desktop app manages: a device that manages this one says what it is.
    expect(lap.kind).toBe("desktop");
    expect(directions(edge(graph, "management:server-request:r1"))).toEqual({
      out: "pending",
      in: "active",
    });

    // Two devices of one name that manage this one: nothing to tell which the request is.
    const twoManagers = buildDeviceGraph({
      servers: servers({ requests: [managementRequestOut("r1", { serverName: "Laptop" })] }),
      access: access({ devices: [manager("d1", "Laptop"), manager("d2", "Laptop")] }),
    });

    expect(twoManagers.nodes.map((item) => item.id).sort()).toEqual([
      "manager:d1",
      "manager:d2",
      "server-request:r1",
    ]);
  });

  it("never joins by name a record that carries an install id, and says on both that they may be one", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("lap", {
            label: "Laptop",
            address: "http://192.168.1.31:34567",
            inboundGrant: grant("g1"),
          }),
        ],
      }),
      servers: servers({
        // Filed at another port of the laptop's host, to a server that did not say who it is.
        requests: [
          managementRequestOut("r1", { serverName: "Laptop", address: "http://192.168.1.31:5000" }),
        ],
      }),
      access: access({ devices: [manager("d1", "laptop")] }),
      discovery: {
        // Found nearby under the name, with an install id of its own.
        management: [
          {
            serverId: "srv-lap",
            name: "LAPTOP",
            address: "http://10.0.0.7:34567",
            appVersion: "2.4.0",
            alreadyManaged: false,
          },
        ],
      },
    });
    const ids = ["ghost:10.0.0.7:34567", "manager:d1", "peer:lap", "server-request:r1"];

    expect(graph.nodes.map((item) => item.id).sort()).toEqual(ids);
    for (const item of graph.nodes) {
      expect(item.sources.managersMatchedByName, item.id).toBe(false);
      // Each says every other one may be it — and none is drawn as it.
      expect(item.namesakes.map((other) => other.nodeId).sort(), item.id).toEqual(
        ids.filter((id) => id !== item.id),
      );
    }
    expect(node(graph, "peer:lap")!.namesakes).toContainEqual({
      nodeId: "manager:d1",
      name: "laptop",
    });
    expect(edge(graph, "management:peer:lap")).toBeUndefined();
    expect(directions(edge(graph, "management:manager:d1"))).toEqual({ out: "none", in: "active" });
    expect(directions(edge(graph, "management:server-request:r1"))).toEqual({
      out: "pending",
      in: "none",
    });
    // Two installs whose ids differ, under one name: two devices, which may be one machine.
    const reset = buildDeviceGraph({
      status: status({ peers: [peer("n1", { label: "Den", outboundGrant: grant("g") })] }),
      servers: servers({ servers: [server("s1", { name: "den" })] }),
    });

    expect(
      reset.nodes.map((item) => [item.id, item.namesakes.map((other) => other.nodeId)]),
    ).toEqual([
      ["peer:n1", ["server:s1"]],
      ["server:s1", ["peer:n1"]],
    ]);
  });

  it("never says a device may be one whose own word rules it out", () => {
    // On real hosts: the NAS and the laptop that manages this device share a machine name.
    const headless = buildDeviceGraph({
      status: status({
        peers: [
          peer("b", {
            label: "jaxs-Mac-mini",
            outboundGrant: grant("g1"),
            kind: ServerKind.Headless,
            platform: RemoteDevicePlatform.Linux,
          }),
        ],
      }),
      access: access({
        devices: [manager("c1", "jaxs-Mac-mini", { platform: RemoteDevicePlatform.MacOS })],
      }),
    });

    // A headless server never manages anything: two devices, and not a word that they are one.
    expect(headless.nodes.map((item) => item.id).sort()).toEqual(["manager:c1", "peer:b"]);
    expect(headless.nodes.map((item) => item.namesakes)).toEqual([[], []]);
    expect(node(headless, "peer:b")!.sources.managers).toEqual([]);
    expect(edge(headless, "management:peer:b")).toBeUndefined();
    expect(directions(edge(headless, "management:manager:c1"))).toEqual({
      out: "none",
      in: "active",
    });
    expect(node(headless, "manager:c1")).toMatchObject({
      kind: "desktop",
      platform: RemoteDevicePlatform.MacOS,
    });

    // As when a server managed from here says so, in its last answer as itself — even with no
    // platform on the pairing to go by.
    const managed = buildDeviceGraph({
      servers: servers({ servers: [server("nas", { name: "NAS", kind: ServerKind.Headless })] }),
      access: access({
        devices: [manager("d1", "NAS", { platform: RemoteDevicePlatform.Unknown })],
      }),
    });

    expect(managed.nodes.map((item) => item.id).sort()).toEqual(["manager:d1", "server:nas"]);
    expect(managed.nodes.map((item) => item.namesakes)).toEqual([[], []]);

    // A desktop app on another system is not that desktop app, and a phone is not one at all.
    const laptop = (platform: RemoteDevicePlatform) =>
      buildDeviceGraph({
        status: status({
          peers: [
            peer("lap", {
              label: "Laptop",
              inboundGrant: grant("g1"),
              kind: ServerKind.Desktop,
              platform: RemoteDevicePlatform.MacOS,
            }),
          ],
        }),
        access: access({ devices: [manager("d1", "Laptop", { platform })] }),
      });

    for (const platform of [RemoteDevicePlatform.Windows, RemoteDevicePlatform.Android]) {
      const graph = laptop(platform);

      expect(graph.nodes.map((item) => item.id).sort(), String(platform)).toEqual([
        "manager:d1",
        "peer:lap",
      ]);
      expect(
        graph.nodes.map((item) => item.namesakes),
        String(platform),
      ).toEqual([[], []]);
    }
    // The same app on the same system may well be the one device: said, never assumed.
    const same = laptop(RemoteDevicePlatform.MacOS);

    expect(same.nodes.map((item) => item.id).sort()).toEqual(["manager:d1", "peer:lap"]);
    expect(node(same, "peer:lap")!.namesakes).toEqual([{ nodeId: "manager:d1", name: "Laptop" }]);
    expect(node(same, "manager:d1")!.namesakes).toEqual([{ nodeId: "peer:lap", name: "Laptop" }]);
    expect(node(same, "peer:lap")!.sources.managersMatchedByName).toBe(false);
  });

  it("keeps a request to manage a moved server again on that server, live or ended", () => {
    const moved = (overrides: Parameters<typeof managementRequestOut>[1]) =>
      buildDeviceGraph({
        servers: servers({
          servers: [
            server("s4", {
              name: "Old NAS",
              address: "http://192.168.1.54:5000",
              state: ManagedServerState.WrongServer,
              answeredBy: { serverId: "other", name: "Other", isThisDevice: false },
            }),
          ],
          // Filed at the address it was found answering as itself: it names the server.
          requests: [
            managementRequestOut("r4", {
              serverId: "s4",
              address: "http://192.168.1.74:5000",
              serverName: "Old NAS",
              ...overrides,
            }),
          ],
        }),
      });
    const live = moved({});

    expect(live.nodes.map((item) => item.id)).toEqual(["server:s4"]);
    expect(
      node(live, "server:s4")!.sources.managementRequestsOut.map((item) => item.requestId),
    ).toEqual(["r4"]);
    expect(node(live, "server:s4")!.issues).toEqual(["wrongServer"]);

    const ended = moved({ active: false, outcome: ManagedServerOutcome.RequestRejected });

    // What became of it is the reader's to see and dismiss, server or not.
    expect(node(ended, "server:s4")!.issues).toEqual(["requestEnded", "wrongServer"]);
    expect(edge(ended, "management:server:s4")).toMatchObject({
      out: "active",
      attention: { direction: "out", issue: "wrongServer" },
    });
  });

  it("never shows as a device's address the place another server answers, however it is spelt", () => {
    const graph = buildDeviceGraph({
      status: status({
        reachableAddresses: ["http://192.168.1.2:34567"],
        peers: [
          peer("s4", {
            label: "NAS",
            address: "http://localhost:45202",
            outboundGrant: grant("g"),
          }),
          peer("s5", {
            label: "Den",
            address: "http://192.168.1.2:45205",
            outboundGrant: grant("h"),
          }),
        ],
      }),
      servers: servers({
        servers: [
          server("s4", {
            address: "http://127.0.0.1:45202",
            state: ManagedServerState.WrongServer,
            answeredBy: { serverId: "intruder", name: "Intruder", isThisDevice: false },
          }),
          server("s5", {
            address: "http://localhost:45205",
            state: ManagedServerState.WrongServer,
            answeredBy: { serverId: "intruder", name: "Intruder", isThisDevice: false },
          }),
        ],
      }),
    });

    expect(node(graph, "peer:s4")!.address).toBeUndefined();
    // This device's LAN address is this machine too.
    expect(node(graph, "peer:s5")!.address).toBeUndefined();
    // Where sharing reaches a server elsewhere stays, for the reader to see.
    const elsewhere = buildDeviceGraph({
      status: status({
        peers: [
          peer("s4", {
            label: "NAS",
            address: "http://192.168.1.80:5000",
            outboundGrant: grant("g"),
          }),
        ],
      }),
      servers: servers({
        servers: [
          server("s4", {
            address: "http://127.0.0.1:45202",
            state: ManagedServerState.WrongServer,
            answeredBy: { serverId: "intruder", name: "Intruder", isThisDevice: false },
          }),
        ],
      }),
    });

    expect(node(elsewhere, "peer:s4")!.address).toBe("http://192.168.1.80:5000");
  });

  it("draws two desktop apps that manage each other by name apart, each saying it may be the other", () => {
    const graph = buildDeviceGraph({
      servers: servers({ servers: [server("pc2", { name: "Den PC" })] }),
      access: access({
        devices: [manager("d1", "Den PC", { platform: RemoteDevicePlatform.MacOS })],
      }),
    });

    // The server carries an install id; the pairing that manages this one only a name.
    expect(graph.nodes.map((item) => item.id)).toEqual(["manager:d1", "server:pc2"]);
    expect(directions(edge(graph, "management:server:pc2"))).toEqual({ out: "active", in: "none" });
    expect(directions(edge(graph, "management:manager:d1"))).toEqual({ out: "none", in: "active" });
    expect(node(graph, "manager:d1")).toMatchObject({
      kind: "desktop",
      platform: RemoteDevicePlatform.MacOS,
      namesakes: [{ nodeId: "server:pc2", name: "Den PC" }],
    });
    expect(node(graph, "server:pc2")!.namesakes).toEqual([
      { nodeId: "manager:d1", name: "Den PC" },
    ]);
  });

  it("puts a request on the install it was filed with, by id before address", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          // The same install, reached by sharing at another address.
          peer("nas", { address: "http://10.0.0.5:34567", outboundGrant: grant("g1") }),
          // Another install at the address the second request went to.
          peer("pc", { address: "http://192.168.1.91:34567", outboundGrant: grant("g2") }),
        ],
      }),
      servers: servers({
        requests: [
          managementRequestOut("to-nas", { serverId: "nas" }),
          managementRequestOut("to-other", {
            serverId: "someone-else",
            address: "http://192.168.1.91:34567",
            serverName: "Other",
          }),
        ],
      }),
    });

    expect(directions(edge(graph, "management:peer:nas"))).toEqual({ out: "pending", in: "none" });
    expect(edge(graph, "management:peer:pc")).toBeUndefined();
    expect(directions(edge(graph, "management:server-request:to-other"))).toEqual({
      out: "pending",
      in: "none",
    });
  });

  it("puts a request to manage a device that already shares with this one on that device", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [peer("nas", { address: "192.168.1.90:34567", outboundGrant: grant("g1") })],
      }),
      servers: servers({ requests: [managementRequestOut("r1", { serverName: "whatever" })] }),
    });

    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    expect(directions(edge(graph, "management:peer:nas"))).toEqual({ out: "pending", in: "none" });
  });

  it("keeps an ended request on its device, to be dismissed there, without drawing it", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [peer("nas", { address: "192.168.1.90:34567", outboundGrant: grant("g1") })],
      }),
      servers: servers({
        requests: [
          managementRequestOut("r1", {
            active: false,
            outcome: ManagedServerOutcome.RequestRejected,
          }),
        ],
      }),
    });

    expect(node(graph, "peer:nas")?.sources.managementRequestsOut).toHaveLength(1);
    expect(edge(graph, "management:peer:nas")).toBeUndefined();
  });
});

describe("device map model: requests other devices filed", () => {
  // A request's name, id and platform are whatever the requester says. Someone at another
  // address can use a trusted device's name and id.
  const spoofed = () =>
    buildDeviceGraph({
      status: status({
        peers: [
          peer("n1", {
            label: "Living-room PC",
            address: "http://192.168.1.11:34567",
            outboundGrant: grant("g1"),
          }),
        ],
        requests: [
          sharingRequest("n1", "incoming", {
            requestId: "s-req",
            nodeName: "Living-room PC",
            remoteAddress: "192.168.1.66",
          }),
        ],
      }),
      access: access({
        pendingRequests: [
          managementRequestIn("m-req", "Living-room PC", {
            platform: RemoteDevicePlatform.Windows,
            remoteAddress: "192.168.1.66",
          }),
        ],
      }),
    });

  it("never draws a claim on the device it claims to be, nor lends it a kind", () => {
    const graph = spoofed();
    const trusted = node(graph, "peer:n1")!;

    expect(trusted).toMatchObject({ unverified: false, kind: "unknown" });
    expect(trusted.platform).toBeUndefined();
    expect(trusted.sources.sharingRequests).toEqual([]);
    expect(trusted.sources.managementRequestsIn).toEqual([]);
    expect(directions(edge(graph, "sharing:peer:n1"))).toEqual({ out: "none", in: "active" });
    expect(edge(graph, "management:peer:n1")).toBeUndefined();
  });

  it("draws the claims of one requester as one unverified device, which says whom it claims to be", () => {
    const graph = spoofed();
    const claimant = node(graph, "sharing-request:s-req")!;

    // Both came from 192.168.1.66 under the same name: one requester asking twice.
    expect(graph.nodes.map((item) => item.id).sort()).toEqual(["peer:n1", "sharing-request:s-req"]);
    expect(claimant).toMatchObject({
      unverified: true,
      kind: "unknown",
      claimsToBe: {
        nodeId: "peer:n1",
        name: "Living-room PC",
        address: "http://192.168.1.11:34567",
      },
    });
    expect(claimant.platform).toBeUndefined();
    expect(directions(edge(graph, "sharing:sharing-request:s-req"))).toEqual({
      out: "pending",
      in: "none",
    });
    expect(directions(edge(graph, "management:sharing-request:s-req"))).toEqual({
      out: "none",
      in: "pending",
    });
  });

  it("keeps claims from different places apart, even under one name", () => {
    const graph = buildDeviceGraph({
      access: access({
        pendingRequests: [
          managementRequestIn("a", "Tablet", { remoteAddress: "192.168.1.40" }),
          managementRequestIn("b", "Tablet", { remoteAddress: "192.168.1.41" }),
        ],
      }),
    });

    expect(graph.nodes.map((item) => item.id).sort()).toEqual([
      "manager-request:a",
      "manager-request:b",
    ]);
  });
});

describe("device map model: what each device says it is", () => {
  it("shows a desktop app or a headless server, and what it runs on, from what it reports", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("nas", {
            outboundGrant: grant("g1"),
            kind: ServerKind.Headless,
            platform: RemoteDevicePlatform.Linux,
          }),
        ],
      }),
      servers: servers({
        servers: [
          server("pc", { kind: ServerKind.Desktop, platform: RemoteDevicePlatform.Windows }),
          // From before servers said.
          server("old"),
        ],
      }),
      discovery: {
        management: [
          {
            serverId: "attic",
            name: "Attic",
            address: "http://192.168.1.40:34567",
            appVersion: "2.5.0",
            alreadyManaged: false,
            kind: ServerKind.Headless,
            platform: RemoteDevicePlatform.Linux,
          },
        ],
      },
    });

    expect(node(graph, "peer:nas")).toMatchObject({
      kind: "server",
      platform: RemoteDevicePlatform.Linux,
    });
    expect(node(graph, "server:pc")).toMatchObject({
      kind: "desktop",
      platform: RemoteDevicePlatform.Windows,
    });
    expect(node(graph, "server:old")).toMatchObject({ kind: "unknown", platform: undefined });
    expect(node(graph, "ghost:192.168.1.40:34567")).toMatchObject({
      kind: "server",
      platform: RemoteDevicePlatform.Linux,
    });
  });

  it("believes the device's own verified word over a match by name, and ignores what it cannot read", () => {
    const graph = buildDeviceGraph({
      status: status({
        peers: [
          peer("nas", {
            label: "NAS",
            inboundGrant: grant("g1"),
            kind: ServerKind.Headless,
            platform: RemoteDevicePlatform.Linux,
          }),
          peer("odd", {
            label: "Odd",
            inboundGrant: grant("g2"),
            kind: ServerKind.Unknown,
            platform: RemoteDevicePlatform.Unknown,
          }),
        ],
      }),
      // A pairing that manages this device, matched to the NAS only by its name.
      access: access({
        devices: [manager("d1", "NAS", { platform: RemoteDevicePlatform.Windows })],
      }),
    });

    expect(node(graph, "peer:nas")).toMatchObject({
      kind: "server",
      platform: RemoteDevicePlatform.Linux,
    });
    expect(node(graph, "peer:odd")).toMatchObject({ kind: "unknown", platform: undefined });
  });
});

describe("device map model: found nearby", () => {
  it("adds devices found nearby as ghosts, one per install, whichever way they were found", () => {
    const graph = buildDeviceGraph({
      status: status(),
      discovery: {
        sharing: [{ nodeId: "s1", name: "Attic", address: "http://192.168.1.40:34567" }],
        management: [
          {
            serverId: "s1",
            name: "Attic",
            address: "http://192.168.1.40:34567",
            appVersion: "2.4.1",
            alreadyManaged: false,
          },
          {
            serverId: "s2",
            name: "Garage",
            address: "http://192.168.1.41:34567",
            appVersion: "2.3.0",
            alreadyManaged: false,
          },
        ],
      },
    });

    expect(graph.nodes.map((item) => [item.id, item.ghost])).toEqual([
      ["ghost:192.168.1.40:34567", true],
      ["ghost:192.168.1.41:34567", true],
    ]);
    const attic = graph.nodes[0];

    expect(attic.sources.sharingCandidate?.nodeId).toBe("s1");
    expect(attic.sources.managementCandidate?.serverId).toBe("s1");
    expect(attic).toMatchObject({ presence: "online", appVersion: "2.4.1" });
    // A ghost has no relationship yet.
    expect(graph.edges).toEqual([]);
  });

  it("attaches what it found to devices already on the map instead of adding them again", () => {
    const graph = buildDeviceGraph({
      status: status({ peers: [peer("nas", { outboundGrant: grant("g1") })] }),
      servers: servers({ servers: [server("srv", { name: "Den" })] }),
      access: access({ devices: [manager("d1", "Laptop")] }),
      discovery: {
        sharing: [{ nodeId: "nas", name: "NAS", address: "http://10.0.0.9:34567" }],
        management: [
          {
            serverId: "srv",
            name: "Den",
            address: "http://10.0.0.8:34567",
            appVersion: "2.4.0",
            alreadyManaged: true,
          },
          // Named like the device that manages this one — which has no address, nor an
          // install id to match this one's by.
          {
            serverId: "lap",
            name: "Laptop",
            address: "http://10.0.0.7:34567",
            appVersion: "2.4.0",
            alreadyManaged: false,
          },
        ],
      },
    });

    expect(graph.nodes.filter((item) => item.ghost).map((item) => item.id)).toEqual([
      "ghost:10.0.0.7:34567",
    ]);
    expect(node(graph, "peer:nas")?.sources.sharingCandidate?.nodeId).toBe("nas");
    expect(node(graph, "server:srv")?.sources.managementCandidate?.serverId).toBe("srv");
    // A name alone never gives the device that manages this one an address.
    expect(node(graph, "manager:d1")?.address).toBeUndefined();
    expect(node(graph, "manager:d1")?.namesakes).toEqual([
      { nodeId: "ghost:10.0.0.7:34567", name: "Laptop" },
    ]);
  });
});

describe("device map model: order and extension", () => {
  const input = () => ({
    status: status({
      peers: [
        peer("zeta", { label: "Zeta", outboundGrant: grant("g1") }),
        peer("alpha", { label: "alpha", inboundGrant: grant("g2") }),
        peer("idle", { label: "Idle" }),
      ],
      requests: [sharingRequest("beta", "incoming")],
    }),
    servers: servers({ servers: [server("mid", { name: "Mid" })] }),
    access: access({ devices: [manager("d1", "Zeta"), manager("d2", "Omega")] }),
    discovery: { sharing: [{ nodeId: "far", name: "Aardvark", address: "http://10.1.1.1:34567" }] },
  });

  it("lists connected devices by name, then unconnected ones, then those found nearby", () => {
    const graph = buildDeviceGraph(input());

    // The device named Zeta that manages this one is not the peer Zeta: a name is no match
    // for its install id.
    expect(graph.nodes.map((item) => item.id)).toEqual([
      "peer:alpha",
      "sharing-request:incoming-beta",
      "server:mid",
      "manager:d2",
      "manager:d1",
      "peer:zeta",
      "peer:idle",
      "ghost:10.1.1.1:34567",
    ]);
  });

  it("does not depend on the order the listings come in", () => {
    const base = input();
    const reordered = {
      ...base,
      status: {
        ...base.status,
        peers: shuffled(base.status.peers),
        requests: shuffled(base.status.requests),
      },
      access: { ...base.access, devices: shuffled(base.access.devices, 3) },
    };

    expect(buildDeviceGraph(reordered)).toEqual(buildDeviceGraph(base));
  });

  it("draws a future relationship kind between devices it knows, and nothing it does not", () => {
    const sync: MapEdge = {
      id: "sync:peer:zeta",
      kind: "sync",
      nodeId: "peer:zeta",
      out: "active",
      in: "active",
    };
    const stray: MapEdge = { ...sync, id: "sync:peer:nobody", nodeId: "peer:nobody" };
    const graph = buildDeviceGraph({ ...input(), extraEdges: [sync, stray] });

    expect(graph.edges.filter((item) => item.kind === "sync")).toEqual([sync]);
    // Without data sync's own records, nothing produces it.
    expect(buildDeviceGraph(input()).edges.some((item) => item.kind === "sync")).toBe(false);
  });
});

describe("device map model: data sync", () => {
  const view = (patch: Partial<DataSyncMapView> = {}): DataSyncMapView => ({
    sharingEnabled: true,
    remoteAccessMode: RemoteAccessMode.Enabled,
    peers: [],
    requests: [],
    outgoing: [],
    ...patch,
  });
  const link = (nodeId: string, name: string, patch: Partial<DataSyncMapPeer> = {}) =>
    ({
      nodeId,
      name,
      linkId: 1,
      mode: DataSyncLinkMode.TwoWay,
      lastMode: DataSyncLinkMode.TwoWay,
      state: DataSyncLinkState.Active,
      receiving: true,
      receivingPending: false,
      peerMayRead: true,
      peerMode: "twoWay",
      openItems: 0,
      readBackDeclined: false,
      kinds: ["customProperty", "extensionGroup"],
      excludedCount: 0,
      heldCount: 0,
      missingAtPeerCount: 0,
      fullReconciliationRunning: false,
      ...patch,
    }) satisfies DataSyncMapPeer;

  it("draws a line to the device the link names by its install id, with its mode", () => {
    const graph = buildDeviceGraph({
      status: status({ peers: [peer("nas", { label: "NAS", outboundGrant: grant("g") })] }),
      dataSync: view({ peers: [link("nas", "NAS", { mode: DataSyncLinkMode.Follow })] }),
    });

    expect(edge(graph, "sync:peer:nas")).toMatchObject({
      kind: "sync",
      in: "active",
      out: "active",
      mode: "follow",
    });
    // The same device: its sharing line and its sync line.
    expect(graph.nodes.map((item) => item.id)).toEqual(["peer:nas"]);
    expect(graph.edges.map((item) => item.id)).toEqual(["sharing:peer:nas", "sync:peer:nas"]);
  });

  it("draws this device's request on a device of its own, and a claim on its own unverified one", () => {
    const graph = buildDeviceGraph({
      status: status(),
      dataSync: view({
        outgoing: [
          {
            linkId: 4,
            nodeId: "garage",
            nodeName: "Garage",
            address: "http://192.168.1.70:34567",
            state: DataSyncLinkState.AwaitingAccess,
            outcome: "awaitingApproval",
            expiresAt: inTenMinutes(),
          },
        ],
        requests: [
          {
            requestId: "r1",
            nodeId: "garage",
            nodeName: "Garage",
            remoteAddress: "192.168.1.70",
            intent: DataSyncRequestIntent.Follow,
            expiresAt: inTenMinutes(),
            claimsKnownDevice: true,
          },
        ],
      }),
    });

    expect(node(graph, "peer:garage")).toMatchObject({ unverified: false, name: "Garage" });
    expect(directions(edge(graph, "sync:peer:garage"))).toEqual({ out: "none", in: "pending" });
    // Its request claims the device this one asked: set against it, never merged into it.
    expect(node(graph, "sync-request:r1")).toMatchObject({
      unverified: true,
      claimsToBe: { nodeId: "peer:garage" },
    });
    expect(directions(edge(graph, "sync:sync-request:r1"))).toEqual({
      out: "pending",
      in: "none",
    });
  });

  it("puts what needs a decision on the device's card", () => {
    const graph = buildDeviceGraph({
      dataSync: view({
        peers: [
          link("nas", "NAS", {
            state: DataSyncLinkState.Paused,
            receiving: false,
            openItems: 2,
            attention: {
              headless: true,
              openDecisions: 1,
              pausedLinks: 0,
              restorePending: false,
              awaitingReview: 0,
            },
          }),
        ],
      }),
    });

    expect(node(graph, "peer:nas")?.issues).toEqual([
      "syncNeedsYou",
      "syncNeedsYouThere",
      "syncPaused",
    ]);
    expect(edge(graph, "sync:peer:nas")?.attention).toEqual({
      direction: "in",
      issue: "syncPaused",
    });
  });
});

describe("device map model: addresses", () => {
  it("compares addresses written with or without a scheme", () => {
    expect(addressKey("192.168.1.5:34567")).toBe("192.168.1.5:34567");
    expect(addressKey("http://192.168.1.5:34567/")).toBe("192.168.1.5:34567");
    expect(addressKey("HTTP://NAS.local")).toBe("nas.local:80");
    expect(addressKey("https://nas.local")).toBe("nas.local:443");
    expect(addressKey("")).toBeUndefined();
    expect(hostKey("http://[::1]:5000")).toBe("[::1]");
    expect(withScheme(" 192.168.1.5:34567 ")).toBe("http://192.168.1.5:34567");
    expect(withScheme("https://nas")).toBe("https://nas");
  });

  it("knows one machine's addresses however they are spelt, and never mixes two machines", () => {
    const own = ownHostsOf(
      status({ reachableAddresses: ["http://192.168.1.2:34567"] }),
      access({ addresses: [{ url: "http://10.8.0.2:34567", interfaceName: "vpn" }] }),
    );

    expect(sameMachine("http://127.0.0.1:45202", "localhost:45202")).toBe(true);
    expect(sameMachine("http://[::1]:45202", "http://127.0.0.1:45202")).toBe(true);
    expect(sameMachine("http://127.0.0.2:45202", "http://LOCALHOST:45202/")).toBe(true);
    // This device's own addresses are this machine too.
    expect(sameMachine("http://192.168.1.2:45202", "http://localhost:45202", own)).toBe(true);
    expect(sameMachine("http://10.8.0.2:45202", "http://127.0.0.1:45202", own)).toBe(true);
    // …only when known to be.
    expect(sameMachine("http://192.168.1.2:45202", "http://localhost:45202")).toBe(false);
    expect(sameMachine("http://127.0.0.1:45202", "http://127.0.0.1:45205")).toBe(false);
    expect(sameMachine("http://192.168.1.9:5000", "http://192.168.1.2:5000", own)).toBe(false);
    expect(sameMachine("NAS.local.:80", "http://nas.local")).toBe(true);
    expect(sameMachine("", "")).toBe(false);
  });
});
