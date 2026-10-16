import type { DeviceGraph, MapEdge } from "../map/graph";

import { describe, expect, it } from "vitest";

import {
  addressKey,
  buildDeviceGraph,
  canManageFromHere,
  hostKey,
  ownHostsOf,
  sameMachine,
  SELF_ID,
  withScheme,
} from "../map/graph";

import {
  access,
  inTenMinutes,
  manager,
  managementRequestIn,
  managementRequestOut,
  server,
  servers,
  status,
} from "./deviceMapFixtures";

import { mapPeer, mapView } from "@/features/data-sync/__tests__/dataSyncFixtures";
import {
  ManagedServerOutcome,
  ManagedServerState,
  RemoteAccessMode,
  RemoteDevicePlatform,
} from "@/sdk/constants";

const edge = (graph: DeviceGraph, id: string) => graph.edges.find((item) => item.id === id);
const node = (graph: DeviceGraph, id: string) => graph.nodes.find((item) => item.id === id);
const directions = (item?: MapEdge) => item && { out: item.out, in: item.in };

describe("device map model: this device", () => {
  it("reads the desktop capability independently from management availability", () => {
    expect(buildDeviceGraph({ servers: servers({ available: true }) }).self.kind).toBe("desktop");
    expect(buildDeviceGraph({ servers: servers({ available: false }) }).self.kind).toBe("server");
    const headlessManager = buildDeviceGraph({
      servers: servers({ available: true, canOpenDesktopView: false }),
    });

    expect(headlessManager.self.kind).toBe("server");
    expect(canManageFromHere(headlessManager)).toBe(true);
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
});

describe("device map model: requests other devices filed", () => {
  // A request's name, id and platform are whatever the requester says. Someone at another
  // address can use a trusted device's name and id.
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

describe("device map model: order and extension", () => {
  const input = () => ({
    status: status(),
    servers: servers({
      servers: [
        server("zeta", { name: "Zeta" }),
        server("alpha", { name: "alpha" }),
        server("mid", { name: "Mid" }),
      ],
    }),
    access: access({ devices: [manager("d1", "Zeta"), manager("d2", "Omega")] }),
    discovery: {
      management: [
        {
          serverId: "far",
          name: "Aardvark",
          address: "http://10.1.1.1:34567",
          appVersion: "2.4.0",
          alreadyManaged: false,
        },
      ],
    },
  });

  it("lists connected devices by name, then unconnected ones, then those found nearby", () => {
    const graph = buildDeviceGraph(input());

    // The device named Zeta that manages this one is not the peer Zeta: a name is no match
    // for its install id.
    expect(graph.nodes.map((item) => item.id)).toEqual([
      "server:alpha",
      "server:mid",
      "manager:d2",
      "manager:d1",
      "server:zeta",
      "ghost:10.1.1.1:34567",
    ]);
  });

  it("draws a future relationship kind between devices it knows, and nothing it does not", () => {
    const sync: MapEdge = {
      id: "sync:server:zeta",
      kind: "sync",
      nodeId: "server:zeta",
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

describe("unified management and definitions synchronization", () => {
  it("joins a managed device, its definition link and discovery by identity without adding permissions", () => {
    const graph = buildDeviceGraph({
      status: status(),
      servers: servers({ servers: [server("nas", { name: "NAS" })] }),
      dataSync: mapView({ peers: [mapPeer("nas", "NAS")] }),
      discovery: {
        management: [
          {
            serverId: "nas",
            name: "NAS",
            address: "http://nas:34567",
            appVersion: "2.4.0",
            alreadyManaged: true,
          },
        ],
      },
    });

    expect(graph.nodes).toHaveLength(1);
    expect(graph.nodes[0].id).toBe("server:nas");
    expect(graph.nodes[0].sources.server?.serverId).toBe("nas");
    expect(graph.nodes[0].sources.sync?.nodeId).toBe("nas");
    expect(new Set(graph.edges.map((edge) => edge.kind))).toEqual(new Set(["management", "sync"]));
  });

  it("never grants management to a device that only synchronizes definitions", () => {
    const graph = buildDeviceGraph({
      status: status(),
      dataSync: mapView({ peers: [mapPeer("nas", "NAS")] }),
    });

    expect(graph.nodes).toHaveLength(1);
    expect(graph.nodes[0].sources.server).toBeUndefined();
    expect(graph.edges.every((edge) => edge.kind === "sync")).toBe(true);
  });

  it("keeps different install IDs apart even when both advertise the same name and address", () => {
    const graph = buildDeviceGraph({
      servers: servers({ servers: [server("a", { name: "NAS", address: "http://nas:34567" })] }),
      discovery: {
        management: [
          {
            serverId: "b",
            name: "NAS",
            address: "http://nas:34567",
            appVersion: "2.4.0",
            alreadyManaged: false,
          },
        ],
      },
    });

    expect(graph.nodes).toHaveLength(2);
    expect(graph.nodes.filter((node) => node.ghost)).toHaveLength(1);
    expect(graph.edges).toHaveLength(1);
  });

  it("keeps an unverified definitions request off the managed device it claims to be", () => {
    const graph = buildDeviceGraph({
      servers: servers({ servers: [server("nas", { name: "NAS", address: "http://nas:34567" })] }),
      dataSync: mapView({
        requests: [
          {
            requestId: "spoof",
            nodeId: "nas",
            nodeName: "NAS",
            remoteAddress: "192.168.1.77",
            expiresAt: inTenMinutes(),
            replacesExistingAccess: false,
            offersReciprocalAccess: false,
            claimsKnownDevice: true,
          },
        ] as never,
      }),
    });
    const known = graph.nodes.find((node) => node.id === "server:nas")!;
    const claim = graph.nodes.find((node) => node.unverified)!;

    expect(known.sources.syncRequests).toHaveLength(0);
    expect(claim.sources.server).toBeUndefined();
    expect(claim.claimsToBe?.nodeId).toBe(known.id);
    expect(claim.kind).toBe("unknown");
  });
});
