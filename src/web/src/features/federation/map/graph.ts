import type {
  BakabaseServiceModelsViewRemoteAccessDeviceViewModel as PairedDevice,
  BakabaseServiceModelsViewRemoteAccessPendingRequestViewModel as ManagementRequest,
  BakabaseServiceModelsViewRemoteAccessSettingsViewModel as RemoteAccessSettings,
} from "@/sdk/Api";
import type {
  FederationStatus,
  ManagedServer,
  ManagedServerCandidate,
  ManagedServerPendingRequest,
  ManagedServersView,
  PairingRequest,
  Peer,
  SharingCandidate,
} from "../types";

import {
  ManagedServerState,
  RemoteAccessMode,
  RemoteDevicePlatform,
  ServerKind,
} from "@/sdk/constants";
import { parseServerTime } from "@/core/serverTime";

/*
 * The device map's model: every device this one knows of, and every relationship between
 * this device and another, built from the three listings the multi-device pages already
 * read. Pure, so the picture can be tested apart from how it is drawn.
 *
 * Every relationship this device knows of has this device at one end — its own sharing
 * grants, the servers it manages, the devices that manage it — so the map is a star: this
 * device in the middle, one spoke per relationship kind to each other device.
 *
 * The listings name devices by several identities. Library sharing's NodeId is the install's
 * remote-access ServerId unless the node was deliberately reset (`FederationNodeIdSource`), so
 * a sharing peer, a managed server and a server found by its beacon that carry the same id are
 * the same install, and are one device on the map. Records are merged only on evidence:
 * - the same install id, first and always;
 * - where one side carries no install id at all — a device that manages this one, a request
 *   filed before servers said who they are — the same address (never one that answers as
 *   another server);
 * - a name, only where neither side carries an install id: the same name on the same host,
 *   or — for a device that manages this one, which carries no address either — a name exactly
 *   one other device has, and the panel says it was recognised by name.
 * Two records whose ids are both known and differ are two devices, wherever they answer. A
 * name is never enough against an id: two devices of one name that are not merged each say
 * they may be the same device as the other, unless what they said of themselves rules it out.
 *
 * A request someone else filed — to browse this library, to manage this device — carries only
 * the requester's claims (a name, an id). It is never merged into a device this one trusts and
 * lends it nothing: it gets its own node, marked unverified, which says whom it claims to be.
 *
 * Merging only decides where a relationship is drawn. Every action still goes to the record
 * it belongs to, by that record's own identity.
 */

/** What another device is, as far as this device can tell. `unknown` is never guessed at. */
export const mapNodeKinds = ["desktop", "server", "mobile", "unknown"] as const;
export type MapNodeKind = (typeof mapNodeKinds)[number];

export const mapPresences = ["online", "offline", "unknown"] as const;
export type MapPresence = (typeof mapPresences)[number];

/** Something about a device the reader should look at. */
export const mapIssues = [
  "wrongServer",
  "revoked",
  "identityConflict",
  "unauthorized",
  "incompatible",
  "unrestricted",
  "requestEnded",
] as const;
export type MapIssue = (typeof mapIssues)[number];

/**
 * - `sharing` — read-only library sharing (federation grants).
 * - `management` — full control through remote-access pairing.
 * - `sync` — reserved for data sync of user-defined definitions. Nothing produces it yet: the
 *   renderer and legend know how to draw it so it can be added without redrawing the map.
 */
export const mapEdgeKinds = ["sharing", "management", "sync"] as const;
export type MapEdgeKind = (typeof mapEdgeKinds)[number];

export type MapDirectionStatus = "none" | "active" | "pending";

export type { SharingCandidate };

export interface MapNodeSources {
  /** Its library-sharing record here. */
  peer?: Peer;
  /** This device manages it. */
  server?: ManagedServer;
  /** It manages this device: its pairing(s) in this device's remote-access list. */
  managers: PairedDevice[];
  /** Its managers were matched to this device by name only. */
  managersMatchedByName: boolean;
  /**
   * Pending library-sharing requests between the two: this device's own, on a device it
   * knows; another device's, on that request's own unverified node.
   */
  sharingRequests: PairingRequest[];
  /** Its pending requests to manage this device — on the request's own unverified node. */
  managementRequestsIn: ManagementRequest[];
  /** This device's requests to manage it — live ones, and ones that ended, until dismissed. */
  managementRequestsOut: ManagedServerPendingRequest[];
  /** Seen by sharing discovery. */
  sharingCandidate?: SharingCandidate;
  /** Seen by the remote-access beacons. */
  managementCandidate?: ManagedServerCandidate;
}

/** A device this one knows, which an unverified request claims to be. */
export interface MapClaim {
  /** The known device's node. */
  nodeId: string;
  name: string;
  /** Where this device knows it, to set against where the request came from. */
  address?: string;
}

/** Another device with the same name, which nothing else ties to this one. */
export interface MapNamesake {
  nodeId: string;
  name: string;
}

export interface MapNode {
  /** Stable across refreshes: the identity of the record the node was first built from. */
  id: string;
  name: string;
  self: boolean;
  /** Found nearby, not connected in any way. */
  ghost: boolean;
  kind: MapNodeKind;
  platform?: RemoteDevicePlatform;
  presence: MapPresence;
  issues: MapIssue[];
  /** The address to pair with, when one is known. */
  address?: string;
  appVersion?: string;
  /** Known only by a request someone filed, whose name and identity are that requester's claims. */
  unverified: boolean;
  /** An unverified device that claims to be one this device already knows. */
  claimsToBe?: MapClaim;
  /**
   * Other devices of the same name that were not merged into this one — an install id on
   * either side tells them apart, or nothing but the name ties them together: each may be
   * the same device as this one, which is said, never assumed.
   */
  namesakes: MapNamesake[];
  /**
   * What identifies it beyond its node id — install ids, addresses, request and pairing ids —
   * so a selection can follow a device when an action turns one record into another (a
   * device found nearby into a request, a request into a managed server).
   */
  keys: string[];
  sources: MapNodeSources;
}

/** A direction of a relationship that exists but does not work right now. */
export interface MapAttention {
  direction: "in" | "out";
  issue: MapIssue;
}

export interface MapEdge {
  /** `${kind}:${nodeId}` */
  id: string;
  kind: MapEdgeKind;
  /** The other end. The first end is always this device. */
  nodeId: string;
  /**
   * Towards the other device. Sharing: it may browse this device's library. Management:
   * this device manages it.
   */
  out: MapDirectionStatus;
  /**
   * Towards this device. Sharing: this device may browse its library. Management: it
   * manages this device.
   */
  in: MapDirectionStatus;
  /** A direction that exists but does not work right now (revoked, another server answers…). */
  attention?: MapAttention;
}

export interface DeviceGraph {
  self: MapNode;
  /** Every other device, connected ones first, each group by name. */
  nodes: MapNode[];
  edges: MapEdge[];
}

export interface DeviceGraphInput {
  /** Library sharing; absent when it could not be read. */
  status?: FederationStatus;
  /** Servers this device manages; absent when they could not be read. */
  servers?: ManagedServersView;
  /** Who may manage this device; absent when it could not be read. */
  access?: RemoteAccessSettings;
  discovery?: {
    sharing?: SharingCandidate[];
    management?: ManagedServerCandidate[];
  };
  /** This device's name when library sharing could not be read. */
  selfName?: string;
  selfPlatform?: RemoteDevicePlatform;
  /** Future relationship kinds, drawn as given. Nothing supplies these yet. */
  extraEdges?: MapEdge[];
}

export const SELF_ID = "self";

/** `host:port`, lower-cased, for comparing addresses written with or without a scheme. */
export const addressKey = (address?: string | null) => {
  const url = parseAddress(address);

  if (!url) return undefined;

  return `${url.hostname.toLowerCase()}:${url.port || (url.protocol === "https:" ? "443" : "80")}`;
};

/** The host alone: the same machine, whichever port it answers on now. */
export const hostKey = (address?: string | null) => parseAddress(address)?.hostname.toLowerCase();

const parseAddress = (address?: string | null) => {
  const trimmed = address?.trim();

  if (!trimmed) return undefined;
  try {
    return new URL(/^[a-z][a-z\d+.-]*:\/\//i.test(trimmed) ? trimmed : `http://${trimmed}`);
  } catch {
    return undefined;
  }
};

const LOOPBACK_HOSTS = new Set(["localhost", "[::1]", "::1", "0.0.0.0", "[::]"]);

/**
 * `host:port` for the machine an address reaches: every spelling of loopback — and each host
 * this device answers on, given in `ownHosts` — is this machine, so `localhost:5000`,
 * `127.0.0.1:5000` and this device's LAN address on port 5000 are one place.
 */
export const machineKey = (address?: string | null, ownHosts?: ReadonlySet<string>) => {
  const url = parseAddress(address);

  if (!url) return undefined;
  const host = url.hostname.toLowerCase().replace(/\.$/, "");
  const here =
    LOOPBACK_HOSTS.has(host) ||
    /^127\./.test(host) ||
    /^\[::ffff:127\./.test(host) ||
    host.endsWith(".localhost") ||
    !!ownHosts?.has(host);

  return `${here ? "this-machine" : host}:${url.port || (url.protocol === "https:" ? "443" : "80")}`;
};

/** Whether two addresses, however spelt, reach the same place. */
export const sameMachine = (
  a?: string | null,
  b?: string | null,
  ownHosts?: ReadonlySet<string>,
) => {
  const key = machineKey(a, ownHosts);

  return key !== undefined && key === machineKey(b, ownHosts);
};

/** The hosts this device answers on, by both of its listings. */
export const ownHostsOf = (status?: FederationStatus, access?: RemoteAccessSettings) =>
  new Set(
    [...(status?.reachableAddresses ?? []), ...(access?.addresses ?? []).map((item) => item.url)]
      .map((address) => hostKey(address))
      .filter((host): host is string => !!host),
  );

/** An address the remote-access pairing accepts: the scheme is required there. */
export const withScheme = (address: string) =>
  /^[a-z][a-z\d+.-]*:\/\//i.test(address.trim()) ? address.trim() : `http://${address.trim()}`;

const nameKey = (name?: string | null) => name?.trim().toLocaleLowerCase() || undefined;

/** The identity keys a node carries (see {@link MapNode.keys}). */
export const identityKey = {
  install: (id: string) => `id:${id}`,
  address: (address?: string | null) => {
    const key = addressKey(address);

    return key ? `address:${key}` : undefined;
  },
  request: (id: string) => `request:${id}`,
  device: (id: string) => `device:${id}`,
};

const desktopPlatforms = new Set([
  RemoteDevicePlatform.Windows,
  RemoteDevicePlatform.MacOS,
  RemoteDevicePlatform.Linux,
]);
const mobilePlatforms = new Set([RemoteDevicePlatform.Android, RemoteDevicePlatform.IOS]);

/** What an install says it is, in the map's words. */
const kindOfServer = (kind?: ServerKind | null): MapNodeKind | undefined =>
  kind === ServerKind.Desktop ? "desktop" : kind === ServerKind.Headless ? "server" : undefined;

const knownPlatform = (platform?: RemoteDevicePlatform | null) =>
  platform !== undefined && platform !== null && platform !== RemoteDevicePlatform.Unknown
    ? platform
    : undefined;

/** What a paired device's platform says it is: only desktop apps and phones pair. */
const kindOfPlatform = (platform?: RemoteDevicePlatform): MapNodeKind =>
  platform === undefined
    ? "unknown"
    : desktopPlatforms.has(platform)
      ? "desktop"
      : mobilePlatforms.has(platform)
        ? "mobile"
        : "unknown";

const peerPresence = (state: string): MapPresence =>
  state === "Online" ? "online" : state === "Offline" ? "offline" : "unknown";

const peerIssue = (state: string): MapIssue | undefined =>
  state === "IdentityConflict"
    ? "identityConflict"
    : state === "Unauthorized"
      ? "unauthorized"
      : state === "Incompatible"
        ? "incompatible"
        : undefined;

const serverPresence = (state: ManagedServerState): MapPresence =>
  // Revoked answered — it just no longer knows this device.
  state === ManagedServerState.Online || state === ManagedServerState.Revoked
    ? "online"
    : state === ManagedServerState.Offline
      ? "offline"
      : "unknown";

const serverIssue = (state: ManagedServerState): MapIssue | undefined =>
  state === ManagedServerState.WrongServer
    ? "wrongServer"
    : state === ManagedServerState.Revoked
      ? "revoked"
      : undefined;

const combinePresence = (values: MapPresence[]): MapPresence =>
  values.includes("online") ? "online" : values.includes("offline") ? "offline" : "unknown";

/**
 * Whether two devices of one name could be one install, by what each said of itself: one kind,
 * one system — a headless server never manages anything, a phone is not a desktop app, and
 * another OS is another machine. What neither said leaves it open.
 */
const couldBeOne = (a: MapNode, b: MapNode) => {
  const manages = (node: MapNode) => node.sources.managers.length > 0;

  if ((manages(a) && b.kind === "server") || (manages(b) && a.kind === "server")) return false;
  if (a.kind !== "unknown" && b.kind !== "unknown" && a.kind !== b.kind) return false;

  return a.platform === undefined || b.platform === undefined || a.platform === b.platform;
};

/**
 * Whether a request has time left. Remote access writes its times with no zone (UTC by
 * meaning), which `Date.parse` would read as local time: east of UTC every request would
 * look hours expired. `parseServerTime` reads both that and a zoned time correctly.
 */
const isLive = (expiresAt: string, now: number) => {
  const at = parseServerTime(expiresAt);

  // An unreadable time is not a reason to hide a request somebody may be waiting on.
  return at === null || at.getTime() > now;
};

interface Draft {
  id: string;
  name?: string;
  ghost: boolean;
  address?: string;
  appVersion?: string;
  unverified: boolean;
  presences: MapPresence[];
  issues: Set<MapIssue>;
  /** Install ids (NodeId, ServerId) its trusted records carry. */
  ids: Set<string>;
  keys: Set<string>;
  /** Unverified only: the ids its requests claim, and the host they came from. */
  claimedIds: Set<string>;
  from?: string;
  sources: MapNodeSources;
}

const emptySources = (): MapNodeSources => ({
  managers: [],
  managersMatchedByName: false,
  sharingRequests: [],
  managementRequestsIn: [],
  managementRequestsOut: [],
});

/** Builds the map's devices and relationships. Independent of the order the lists come in. */
export function buildDeviceGraph(input: DeviceGraphInput, now = Date.now()): DeviceGraph {
  const { status, servers, access, discovery } = input;
  const selfNodeId = status?.identity.nodeId;
  const ownHosts = ownHostsOf(status, access);
  const drafts = new Map<string, Draft>();
  const draftList = () => [...drafts.values()];

  const create = (id: string, patch: Partial<Draft> = {}) => {
    const draft: Draft = {
      id,
      ghost: false,
      unverified: false,
      presences: [],
      issues: new Set(),
      ids: new Set(),
      keys: new Set(),
      claimedIds: new Set(),
      sources: emptySources(),
      ...patch,
    };

    drafts.set(id, draft);

    return draft;
  };
  const addKey = (draft: Draft, key?: string) => {
    if (key) draft.keys.add(key);
  };
  const addInstall = (draft: Draft, id?: string | null) => {
    if (!id) return;
    draft.ids.add(id);
    draft.keys.add(identityKey.install(id));
  };
  /** A trusted record carrying this install id. */
  const byId = (id?: string | null) =>
    id ? draftList().find((draft) => !draft.unverified && draft.ids.has(id)) : undefined;
  /**
   * Which trusted drafts a record may join on weaker evidence than an id: any, when the record
   * carries no id itself; otherwise only those that carry none — two known ids that differ are
   * two installs.
   */
  const joinable = (id?: string | null, exclude?: (draft: Draft) => boolean) => (draft: Draft) =>
    !draft.unverified && (!id || draft.ids.size === 0) && !exclude?.(draft);
  const byAddress = (address: string | null | undefined, eligible: (draft: Draft) => boolean) => {
    const key = identityKey.address(address);

    return key ? draftList().find((draft) => eligible(draft) && draft.keys.has(key)) : undefined;
  };
  /**
   * The one trusted device with this name (on this host, when given), for a record that
   * carries no install id either: a name is never enough against an id. None when two share
   * the name, or when the one that has it carries an id.
   */
  const byNameOnly = (
    name: string | null | undefined,
    exclude?: (draft: Draft) => boolean,
    host?: string,
  ) => {
    const key = nameKey(name);

    if (!key) return undefined;
    const found = draftList().filter(
      (draft) =>
        !draft.unverified &&
        !exclude?.(draft) &&
        nameKey(draft.name) === key &&
        (host === undefined || hostKey(draft.address) === host),
    );

    return found.length === 1 && found[0].ids.size === 0 ? found[0] : undefined;
  };
  /** The one device with this name — none when two share it. */
  const byUniqueName = (
    name: string | null | undefined,
    eligible: (draft: Draft) => boolean,
    host?: string,
  ) => {
    const key = nameKey(name);

    if (!key) return undefined;
    const found = draftList().filter(
      (draft) =>
        eligible(draft) &&
        nameKey(draft.name) === key &&
        (host === undefined || hostKey(draft.address) === host),
    );

    return found.length === 1 ? found[0] : undefined;
  };
  /** Another claim from the same place under the same name: one requester, asking twice. */
  const sameClaimant = (name: string | null | undefined, from: string | undefined) =>
    from === undefined
      ? undefined
      : draftList().find(
          (draft) =>
            draft.unverified && draft.from === from && nameKey(draft.name) === nameKey(name),
        );

  // Sorted by identity first, so which record a later one merges into never depends on the
  // order a listing happened to come in.
  const sortBy = <T>(items: T[] | undefined, key: (item: T) => string) =>
    [...(items ?? [])].sort((a, b) => key(a).localeCompare(key(b)));

  // 1. Library sharing peers: identities their handshake proved.
  for (const peer of sortBy(status?.peers, (p) => p.nodeId)) {
    if (peer.nodeId === selfNodeId) continue;
    const draft = create(`peer:${peer.nodeId}`, {
      name: peer.label,
      address: peer.address ?? undefined,
    });

    addInstall(draft, peer.nodeId);
    addKey(draft, identityKey.address(peer.address));
    draft.sources.peer = peer;
    draft.presences.push(peerPresence(peer.connectionState));
    const issue = peerIssue(peer.connectionState);

    if (issue) draft.issues.add(issue);
  }

  // 2. Servers this device manages: joined to a peer only by install id.
  for (const server of sortBy(servers?.servers, (s) => s.serverId)) {
    const answersAsItself = server.state !== ManagedServerState.WrongServer;
    // A merged device keeps the name its sharing handshake proved.
    const draft =
      byId(server.serverId) ??
      create(`server:${server.serverId}`, { name: server.name || server.address });

    addInstall(draft, server.serverId);
    draft.sources.server = server;
    draft.appVersion = server.appVersion ?? draft.appVersion;
    // An address that answers as another server says nothing about this one: never offered
    // as somewhere to pair, never evidence that another record is this server — nor shown as
    // where it is, when its sharing record names the same place in another spelling.
    // Otherwise it is the address management pairs with.
    if (answersAsItself) {
      draft.address = server.address;
      addKey(draft, identityKey.address(server.address));
    } else if (sameMachine(draft.address, server.address, ownHosts)) draft.address = undefined;
    draft.presences.push(serverPresence(server.state));
    const issue = serverIssue(server.state);

    if (issue) draft.issues.add(issue);
    if (server.mode === RemoteAccessMode.Unrestricted) draft.issues.add("unrestricted");
  }

  // 3. This device's own requests, to addresses it chose.
  for (const request of sortBy(status?.requests, (r) => r.requestId)) {
    if (request.direction !== "outgoing" || request.nodeId === selfNodeId) continue;
    if (request.status !== "awaitingApproval" || !isLive(request.expiresAt, now)) continue;
    const draft =
      byId(request.nodeId) ?? create(`peer:${request.nodeId}`, { name: request.nodeName });

    addInstall(draft, request.nodeId);
    addKey(draft, identityKey.request(request.requestId));
    draft.sources.sharingRequests.push(request);
  }
  for (const request of sortBy(servers?.requests, (r) => r.requestId)) {
    const serverId = request.serverId ?? undefined;
    const eligible = joinable(serverId);
    const match =
      byId(serverId) ??
      byAddress(request.address, eligible) ??
      (serverId ? undefined : byNameOnly(request.serverName, undefined, hostKey(request.address)));
    // Kept when it has ended too, until it is dismissed: the outcome is the only word the
    // user gets on a request filed to a device the map shows nowhere else.
    const draft =
      match ??
      create(`server-request:${request.requestId}`, {
        name: request.serverName || request.address,
        address: request.address,
      });

    addInstall(draft, serverId);
    addKey(draft, identityKey.request(request.requestId));
    addKey(draft, identityKey.address(request.address));
    draft.sources.managementRequestsOut.push(request);
  }

  // 4. Devices that manage this one. Paired, but with no install id or address: by name only
  // into a record with no install id either, and only when the name is unique — otherwise a
  // device of its own, which says whom it may be (step 9).
  const managerNames = new Map<string, number>();

  for (const device of access?.devices ?? []) {
    const key = nameKey(device.name);

    if (key) managerNames.set(key, (managerNames.get(key) ?? 0) + 1);
  }
  for (const device of sortBy(access?.devices, (d) => d.id)) {
    const key = nameKey(device.name);
    const match =
      key && managerNames.get(key) === 1
        ? byNameOnly(device.name, (draft) => draft.id.startsWith("manager:"))
        : undefined;
    const draft = match ?? create(`manager:${device.id}`, { name: device.name });

    addKey(draft, identityKey.device(device.id));
    draft.sources.managers.push(device);
    if (match) draft.sources.managersMatchedByName = true;
  }

  // 5. Requests other devices filed: claims, each on its own unverified node. Two from the
  // same address under the same name are one requester.
  for (const request of sortBy(status?.requests, (r) => r.requestId)) {
    if (request.direction !== "incoming" || request.nodeId === selfNodeId) continue;
    if (request.status !== "awaitingApproval" || !isLive(request.expiresAt, now)) continue;
    const from = hostKey(request.remoteAddress);
    const draft =
      sameClaimant(request.nodeName, from) ??
      create(`sharing-request:${request.requestId}`, {
        name: request.nodeName,
        unverified: true,
        from,
      });

    draft.claimedIds.add(request.nodeId);
    addKey(draft, identityKey.request(request.requestId));
    draft.sources.sharingRequests.push(request);
  }
  for (const request of sortBy(access?.pendingRequests, (r) => r.id)) {
    if (!isLive(request.expiresAt, now)) continue;
    const from = hostKey(request.remoteAddress);
    const draft =
      sameClaimant(request.deviceName, from) ??
      create(`manager-request:${request.id}`, {
        name: request.deviceName,
        unverified: true,
        from,
      });

    addKey(draft, identityKey.request(request.id));
    draft.sources.managementRequestsIn.push(request);
  }

  // 6. Found nearby: attached to a device already known, a ghost otherwise. A beacon's id is
  // the install's own claim, like its name; it is enough to say where it was seen.
  const ghost = (installId: string, name: string, address: string) => {
    const base = `ghost:${addressKey(address) ?? installId}`;

    return create(drafts.has(base) ? `${base}:${installId}` : base, {
      name,
      address,
      ghost: true,
    });
  };

  // Each carries an install id, so a name never joins it to anything (step 9 says whom it
  // may be).
  for (const candidate of sortBy(discovery?.sharing, (c) => c.nodeId)) {
    if (candidate.nodeId === selfNodeId) continue;
    const draft =
      byId(candidate.nodeId) ??
      byAddress(candidate.address, joinable(candidate.nodeId)) ??
      ghost(candidate.nodeId, candidate.name, candidate.address);

    addInstall(draft, candidate.nodeId);
    addKey(draft, identityKey.address(candidate.address));
    draft.sources.sharingCandidate = candidate;
    draft.address ??= candidate.address;
    draft.presences.push("online");
  }
  for (const candidate of sortBy(discovery?.management, (c) => c.serverId)) {
    const draft =
      byId(candidate.serverId) ??
      byAddress(candidate.address, joinable(candidate.serverId)) ??
      ghost(candidate.serverId, candidate.name, candidate.address);

    addInstall(draft, candidate.serverId);
    addKey(draft, identityKey.address(candidate.address));
    draft.sources.managementCandidate = candidate;
    draft.address ??= candidate.address;
    draft.appVersion ??= candidate.appVersion;
    draft.presences.push("online");
  }

  // 7. Whom an unverified request claims to be, when that is a device this one knows: the
  // panel sets where the request came from against where that device is known.
  const claimOf = (draft: Draft): MapClaim | undefined => {
    const known =
      [...draft.claimedIds].map((id) => byId(id)).find(Boolean) ??
      byUniqueName(
        draft.name,
        joinable(undefined, (other) => other.ghost),
      );

    return known
      ? {
          nodeId: known.id,
          name: known.name?.trim() || known.address || known.id,
          address: known.address,
        }
      : undefined;
  };

  // 8. Relationships.
  const edges: MapEdge[] = [];
  const nodes: MapNode[] = [];

  for (const draft of drafts.values()) {
    const { sources } = draft;

    // Each request this device filed, on its own: one that ended is there to be read and
    // dismissed, beside a live one, and beside a server it already manages — asking again to
    // manage a server that moved joins the request to that server, by its id.
    if (sources.managementRequestsOut.some((request) => !request.active))
      draft.issues.add("requestEnded");
    // What it says it is, most trusted first: its sharing handshake (verified), the server
    // this device paired with, then what it answered nearby; a device that manages this one
    // only pairs as a desktop app or a phone, which its pairing's platform tells. Never a
    // request, whose word is all it has.
    const reported = [
      sources.peer,
      sources.server,
      sources.managementCandidate,
      sources.sharingCandidate,
    ];
    const managerPlatform = knownPlatform(sources.managers[0]?.platform);
    const kind =
      reported.map((source) => kindOfServer(source?.kind)).find(Boolean) ??
      kindOfPlatform(managerPlatform);
    const platform =
      reported.map((source) => knownPlatform(source?.platform)).find(Boolean) ?? managerPlatform;
    const node: MapNode = {
      id: draft.id,
      name: draft.name?.trim() || draft.address || draft.id,
      self: false,
      ghost: draft.ghost,
      kind,
      platform,
      presence: combinePresence(draft.presences),
      issues: [...draft.issues].sort(),
      address: draft.address,
      appVersion: draft.appVersion,
      unverified: draft.unverified,
      claimsToBe: draft.unverified ? claimOf(draft) : undefined,
      namesakes: [],
      keys: [...draft.keys].sort(),
      sources,
    };

    nodes.push(node);
    if (node.ghost) continue;

    const incoming = sources.sharingRequests.filter((r) => r.direction === "incoming");
    const outgoing = sources.sharingRequests.filter((r) => r.direction === "outgoing");
    const sharingIssue = sources.peer?.outboundGrant
      ? peerIssue(sources.peer.connectionState)
      : undefined;
    const managementIssue = sources.server ? serverIssue(sources.server.state) : undefined;
    const sharing: MapEdge = {
      id: `sharing:${node.id}`,
      kind: "sharing",
      nodeId: node.id,
      out: sources.peer?.inboundGrant ? "active" : incoming.length ? "pending" : "none",
      in: sources.peer?.outboundGrant
        ? "active"
        : outgoing.length || incoming.some((r) => r.offersReciprocalAccess)
          ? "pending"
          : "none",
      // This device's reading of its library is what fails.
      attention: sharingIssue ? { direction: "in", issue: sharingIssue } : undefined,
    };
    const management: MapEdge = {
      id: `management:${node.id}`,
      kind: "management",
      nodeId: node.id,
      out: sources.server
        ? "active"
        : sources.managementRequestsOut.some((r) => r.active)
          ? "pending"
          : "none",
      in: sources.managers.length
        ? "active"
        : sources.managementRequestsIn.length
          ? "pending"
          : "none",
      attention: managementIssue ? { direction: "out", issue: managementIssue } : undefined,
    };

    for (const edge of [sharing, management])
      if (edge.out !== "none" || edge.in !== "none") edges.push(edge);
  }

  // A future kind (data sync) is drawn as given, between devices the map knows.
  const known = new Set(nodes.filter((node) => !node.ghost).map((node) => node.id));

  for (const edge of input.extraEdges ?? []) {
    if (known.has(edge.nodeId) && !edges.some((existing) => existing.id === edge.id))
      edges.push(edge);
  }

  const connected = new Set(edges.map((edge) => edge.nodeId));
  const rank = (node: MapNode) => (node.ghost ? 2 : connected.has(node.id) ? 0 : 1);

  nodes.sort(
    (a, b) =>
      rank(a) - rank(b) ||
      a.name.localeCompare(b.name, undefined, { sensitivity: "base" }) ||
      a.id.localeCompare(b.id),
  );
  edges.sort(
    (a, b) =>
      mapEdgeKinds.indexOf(a.kind) - mapEdgeKinds.indexOf(b.kind) ||
      a.nodeId.localeCompare(b.nodeId),
  );

  // 9. Devices of one name that stayed apart — an install id tells them apart, or the name is
  // all that ties them — each say whom they may be, unless what they said of themselves rules
  // it out. Never a claim: an unverified request says whom it claims to be (step 7).
  const nameOf = new Map(nodes.map((node) => [node.id, nameKey(drafts.get(node.id)?.name)]));
  const named = nodes.filter((node) => !node.unverified && nameOf.get(node.id));

  for (const node of named)
    node.namesakes = named
      .filter(
        (other) =>
          other !== node && nameOf.get(other.id) === nameOf.get(node.id) && couldBeOne(node, other),
      )
      .map((other) => ({ nodeId: other.id, name: other.name }));

  const self: MapNode = {
    id: SELF_ID,
    name: status?.identity.name?.trim() || input.selfName?.trim() || "",
    self: true,
    ghost: false,
    // The desktop app can manage other servers; a headless server cannot.
    kind: servers ? (servers.available ? "desktop" : "server") : "unknown",
    platform: input.selfPlatform,
    presence: "online",
    issues: [],
    unverified: false,
    namesakes: [],
    keys: [],
    sources: emptySources(),
  };

  return { self, nodes, edges };
}

/** The relationships drawn to one device, in kind order. */
export const edgesOf = (graph: DeviceGraph, nodeId: string) =>
  graph.edges.filter((edge) => edge.nodeId === nodeId);

/** Whether this device's own UI can offer to manage a device: only the desktop app manages. */
export const canManageFromHere = (graph: DeviceGraph) => graph.self.kind === "desktop";

/**
 * The node that now stands for a device known by these keys — the same device after an
 * action turned one of its records into another. The first key that any node carries wins,
 * so callers list the most specific first.
 */
export const followKeys = (graph: DeviceGraph, keys: readonly string[]) => {
  for (const key of keys) {
    const node = graph.nodes.find((item) => item.keys.includes(key));

    if (node) return node;
  }

  return undefined;
};
