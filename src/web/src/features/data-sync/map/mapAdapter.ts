import type {
  DataSyncMapPeer,
  DataSyncMapRequest,
  DataSyncMapView,
  DataSyncSourceAttention,
} from "../api";
import type { SyncPeer } from "../viewModels";
import type { MapAttention, MapEdge, MapIssue, MapNode } from "@/features/federation/map/graph";

import {
  lineMode,
  readLane,
  receiveLane,
  receiveWaitsForReview,
  requestEnded,
  syncIssueOf,
  syncPeerOf,
} from "../viewModels";

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

/** One device the map view names, with what the device is known by. */
export interface SyncNodeRecord {
  /** The install id the record names: the device is found, or created, by it. */
  nodeId: string;
  name: string;
  /** Where this device reached it, when a request of its own went there. */
  address?: string;
  /** Identity keys the device carries from the record (see `MapNode.keys`). */
  keys: string[];
  peer: DataSyncMapPeer;
}

/**
 * Step 3 of the map, "this device's own records": every device with a link, a grant or a
 * request this device filed — waiting, or ended until dismissed — in id order so a later listing
 * never decides where an earlier one went. The map finds each device by the id, or creates it as
 * `peer:{id}`, so a device is always there for a link to one that is no library-sharing peer yet.
 */
export const buildSyncOutgoingNodes = (
  view: DataSyncMapView | undefined,
  selfNodeId?: string,
): SyncNodeRecord[] =>
  (view?.peers ?? [])
    .filter((peer) => peer.nodeId && peer.nodeId !== selfNodeId)
    .sort((a, b) => a.nodeId.localeCompare(b.nodeId))
    .map((peer) => {
      const address = peer.request?.address ?? undefined;
      const addressKey = identityKey.address(address);

      return {
        nodeId: peer.nodeId,
        name: peer.name,
        address,
        keys: [identityKey.install(peer.nodeId), ...(addressKey ? [addressKey] : [])],
        peer,
      };
    });

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

/** What a node's record says about data sync with it, as one device (see `SyncPeer`). */
export const syncPeerOfSources = (sources: { sync?: DataSyncMapPeer }): SyncPeer | undefined =>
  sources.sync ? syncPeerOf(sources.sync) : undefined;

/** The install id a device is known by on the map, when it has one (see `MapNode.keys`). */
export const installIdOf = (keys: readonly string[]) => {
  const prefix = identityKey.install("");

  return keys
    .find((key) => key.startsWith(prefix) && key.length > prefix.length)
    ?.slice(prefix.length);
};

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
export function syncIssues(sources: { sync?: DataSyncMapPeer }): MapIssue[] {
  const peer = syncPeerOfSources(sources);

  if (!peer) return [];
  const issues: MapIssue[] = [];

  if (requestEnded(peer)) issues.push("syncRequestEnded");
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
    ...(receiveWaitsForReview(peer) ? { inReview: true } : {}),
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

  for (const record of view.peers ?? []) {
    const node = trusted.find((item) => item.keys.includes(identityKey.install(record.nodeId)));

    if (!node) continue;
    const edge = peerEdge(node, syncPeerOf(record));

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
    (view.peers ?? []).some(
      (peer) =>
        peer.link?.state === DataSyncLinkState.AwaitingAccess ||
        peer.request?.outcome === "awaitingApproval",
    ));
