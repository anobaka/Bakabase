import type {
  DataSyncMapOutgoing,
  DataSyncMapPeer,
  DataSyncMapRequest,
  DataSyncMapView,
  DataSyncSourceAttention,
} from "../api";
import type { SyncPeer } from "../viewModels";
import type { MapAttention, MapEdge, MapIssue, MapNode } from "@/features/federation/map/graph";

import { lineMode, readLane, receiveLane, syncIssueOf, syncPeerOfRecords } from "../viewModels";

import { identityKey } from "@/features/federation/map/graph";
import { parseServerTime } from "@/core/serverTime";
import { DataSyncLinkState, DataSyncRequestIntent } from "@/sdk/constants";

/*
 * Data sync on the device map (spec §11.1): which devices its records are drawn on, the line
 * each gets and what each says about itself. Pure, so the map's rules for it are tested apart
 * from how the map is drawn; `map/graph.ts` calls it at the steps it builds nodes and lines in.
 *
 * The map's own rules hold here too:
 * - a record is put on a device only by the install id it names, never by a name;
 * - a request another device filed is a claim: it stays on its own unverified node and is
 *   never merged into a device this one trusts;
 * - nothing this device filed vanishes silently: an ended request stays on its device, with its
 *   outcome, until it is dismissed;
 * - what a device is — a headless hub whose decisions wait there — is what it reported
 *   (`DataSyncSourceAttention.headless`), never guessed.
 */

/** What a node needs to carry for data sync to find it: the map's `MapNode` fits it. */
export type SyncMapNode = Pick<MapNode, "id" | "self" | "ghost" | "unverified" | "keys">;

/**
 * One of this device's own records about another device — its link or its grant, and its
 * request to read that device's definitions — with what the device is known by.
 */
export interface SyncNodeRecord {
  /** The install id the record names: the device is found, or created, by it. */
  nodeId: string;
  name: string;
  /** Where this device reached it, when a request of its own went there. */
  address?: string;
  /** Identity keys the device carries from these records (see `MapNode.keys`). */
  keys: string[];
  peer?: DataSyncMapPeer;
  outgoing?: DataSyncMapOutgoing;
}

/**
 * Step 3 of the map, "this device's own records": every link and grant, and every request this
 * device filed — waiting, or ended until dismissed — once per install id, in id order so a
 * later listing never decides where an earlier one went. The map finds each device by the id,
 * or creates it as `peer:{id}`, so a device is always there for a link to one that is no
 * library-sharing peer yet.
 */
export function buildSyncOutgoingNodes(
  view: DataSyncMapView | undefined,
  selfNodeId?: string,
): SyncNodeRecord[] {
  if (!view) return [];
  const records = new Map<string, SyncNodeRecord>();
  const recordOf = (nodeId: string, name: string) => {
    let record = records.get(nodeId);

    if (!record) {
      record = { nodeId, name, keys: [identityKey.install(nodeId)] };
      records.set(nodeId, record);
    }

    return record;
  };

  for (const peer of view.peers ?? []) {
    if (!peer.nodeId || peer.nodeId === selfNodeId) continue;
    recordOf(peer.nodeId, peer.name).peer = peer;
  }
  for (const outgoing of view.outgoing ?? []) {
    if (!outgoing.nodeId || outgoing.nodeId === selfNodeId) continue;
    const record = recordOf(outgoing.nodeId, outgoing.nodeName);
    const address = identityKey.address(outgoing.address);

    record.outgoing = outgoing;
    record.name ||= outgoing.nodeName;
    record.address = outgoing.address ?? undefined;
    if (address && !record.keys.includes(address)) record.keys.push(address);
  }

  return [...records.values()].sort((a, b) => a.nodeId.localeCompare(b.nodeId));
}

/**
 * Whether a request still waits. A request's own time is UTC with no zone; an unreadable one is
 * no reason to hide a request somebody may be waiting on (the map's own rule).
 */
const isLive = (expiresAt: string | undefined | null, now: number) => {
  const at = parseServerTime(expiresAt);

  return at === null || at.getTime() > now;
};

/**
 * Step 5, "requests other devices filed": each request to read this device's definitions that
 * still waits, in id order — a claim the map puts on a `sync-request:{id}` node of its own,
 * with the other claims from the same address under the same name.
 */
export const syncClaims = (
  view: DataSyncMapView | undefined,
  now: number = Date.now(),
  selfNodeId?: string,
): DataSyncMapRequest[] =>
  [...(view?.requests ?? [])]
    .filter((request) => request.nodeId !== selfNodeId && isLive(request.expiresAt, now))
    .sort((a, b) => a.requestId.localeCompare(b.requestId));

/** What a node's own records say about data sync with it, as one device (see `SyncPeer`). */
export const syncPeerOfSources = (sources: {
  sync?: DataSyncMapPeer;
  syncOutgoing: readonly DataSyncMapOutgoing[];
}): SyncPeer | undefined =>
  syncPeerOfRecords(sources.sync, sources.syncOutgoing[sources.syncOutgoing.length - 1]);

/** The install id a device is known by on the map, when it has one (see `MapNode.keys`). */
export const installIdOf = (keys: readonly string[]) => {
  const prefix = identityKey.install("");

  return keys
    .find((key) => key.startsWith(prefix) && key.length > prefix.length)
    ?.slice(prefix.length);
};

/** A request this device filed that ended without an answer it can use: rejected or expired. */
export const isEnded = (outgoing: DataSyncMapOutgoing) =>
  outgoing.state === DataSyncLinkState.Stopped ||
  outgoing.outcome === "rejected" ||
  outgoing.outcome === "expired";

/** Receiving from it does not work right now: why, on the receive direction. */
export const syncAttention = (peer: SyncPeer): MapAttention | undefined => {
  const issue = syncIssueOf(peer);

  return issue ? { direction: "in", issue } : undefined;
};

/**
 * Decisions a source reported it holds nobody has taken there — a headless hub, which holds back
 * what it has not decided, so the reader must go there (spec §9.1 N). Only what the source
 * reported: whether it is headless is its own word.
 */
export const waitsThere = (attention?: DataSyncSourceAttention | null) =>
  !!attention?.headless &&
  (attention.openDecisions > 0 || attention.pausedLinks > 0 || attention.restorePending);

/**
 * What a device's card says about data sync with it: a request of this device's own that ended
 * and waits to be dismissed — as the map marks an ended request to manage a device — what does
 * not work on the receive direction, changes that need this device, and decisions that wait on it.
 */
export function syncIssues(sources: {
  sync?: DataSyncMapPeer;
  syncOutgoing: readonly DataSyncMapOutgoing[];
}): MapIssue[] {
  const peer = syncPeerOfSources(sources);

  if (!peer) return [];
  const issues: MapIssue[] = [];
  const latest = sources.syncOutgoing[sources.syncOutgoing.length - 1];

  if (latest && isEnded(latest)) issues.push("syncRequestEnded");
  const attention = syncAttention(peer);

  if (attention) issues.push(attention.issue);
  if (peer.openItems > 0) issues.push("syncNeedsYou");
  if (waitsThere(peer.attention)) issues.push("syncNeedsYouThere");

  return issues;
}

/** The line to a device this device syncs with, from what its records say. */
const peerEdge = (node: SyncMapNode, peer: SyncPeer): MapEdge => {
  const mode = lineMode(peer);
  const attention = syncAttention(peer);

  return {
    id: `sync:${node.id}`,
    kind: "sync",
    nodeId: node.id,
    // A link that is set up but does not work right now is still drawn — dotted, with the
    // warning mark — as the map draws every such direction; the page's drawing says the same.
    in: receiveLane(peer),
    out: readLane(peer),
    ...(mode ? { mode } : {}),
    ...(attention ? { attention } : {}),
  };
};

/**
 * Step 8, "relationships": a data sync line for every device the map view names.
 *
 * - A link or a grant, and this device's own request that waits, go to the device that carries
 *   the install id they name (`identityKey.install`): receiving from it (`in`), working, waiting
 *   for access or review, or set up but not working right now; it reading this device (`out`);
 *   the mode on the badge — both ways also when each receives from the other; and what does not
 *   work, on the receive direction.
 * - An ended request of this device's own is kept on its device to be read and dismissed, and
 *   draws nothing, like an ended request to manage one.
 * - A request another device filed goes to its own unverified node, found by the request's id:
 *   it asks to read this device (`out` pending) and, asking to keep in step both ways, offers
 *   its own definitions back (`in` pending) — as a library request offering access back does.
 */
export function buildSyncEdges(
  view: DataSyncMapView | undefined,
  nodes: readonly SyncMapNode[],
): MapEdge[] {
  if (!view) return [];
  const edges: MapEdge[] = [];
  const trusted = nodes.filter((node) => !node.self && !node.ghost && !node.unverified);

  for (const record of buildSyncOutgoingNodes(view)) {
    const node = trusted.find((item) => item.keys.includes(identityKey.install(record.nodeId)));
    const peer = syncPeerOfRecords(record.peer, record.outgoing);

    if (!node || !peer) continue;
    const edge = peerEdge(node, peer);

    if (edge.in !== "none" || edge.out !== "none") edges.push(edge);
  }

  for (const node of nodes) {
    if (!node.unverified) continue;
    const claims = (view.requests ?? []).filter((request) =>
      node.keys.includes(identityKey.request(request.requestId)),
    );

    if (!claims.length) continue;
    edges.push({
      id: `sync:${node.id}`,
      kind: "sync",
      nodeId: node.id,
      out: "pending",
      in: claims.some((request) => request.intent === DataSyncRequestIntent.TwoWay)
        ? "pending"
        : "none",
    });
  }

  return edges;
}

/**
 * Whether anything on the map view waits on someone right now — a request to this device, or
 * this device's own waiting for an approval — so the map reads it again sooner.
 */
export const isSyncMapLive = (view: DataSyncMapView | undefined, now: number = Date.now()) =>
  !!view &&
  ((view.requests ?? []).some((request) => isLive(request.expiresAt, now)) ||
    (view.peers ?? []).some((peer) => peer.state === DataSyncLinkState.AwaitingAccess) ||
    (view.outgoing ?? []).some(
      (outgoing) =>
        outgoing.state === DataSyncLinkState.AwaitingAccess ||
        outgoing.outcome === "awaitingApproval",
    ));
