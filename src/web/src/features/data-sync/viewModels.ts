import type { TFunction } from "i18next";
import type {
  DataSyncAccessRequestView,
  DataSyncEntityStatusView,
  DataSyncLinkView,
  DataSyncMapOutgoing,
  DataSyncMapPeer,
  DataSyncMapView,
  DataSyncReaderView,
  DataSyncSourceAttention,
  DataSyncStatusView,
} from "./api";

import { hasPassed, serverTime, timeAgo } from "./times";

import {
  DataSyncEntitySyncState,
  DataSyncHeldReason,
  DataSyncKinds,
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncPauseReasonLabel,
  DataSyncPeerErrorCode,
  DataSyncPeerErrorCodeLabel,
  DataSyncRequestDirection,
  DataSyncStatusLevel,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * What the data sync page and its drawings show, worked out from what the server answers —
 * pure, so every rule is tested without rendering: which way each arrow points and how it is
 * drawn, what the receive arrow, the mode badge and the mode buttons say (always the same
 * thing), and which line of the status catalogue (spec §11.6) a link reads as.
 */

type T = TFunction;

// ---- kinds -------------------------------------------------------------------------------------

/** Every kind this build syncs, in the order the page lists them: properties first. */
export const dataSyncKinds: readonly string[] = [
  ...["customProperty", "extensionGroup"].filter((kind) => DataSyncKinds.includes(kind)),
  ...DataSyncKinds.filter((kind) => kind !== "customProperty" && kind !== "extensionGroup"),
];

/** Kinds in the page's order; a kind this build does not know keeps its place at the end. */
export const orderKinds = (kinds: readonly string[]) => {
  const known = dataSyncKinds.filter((kind) => kinds.includes(kind));

  return [...known, ...kinds.filter((kind) => !dataSyncKinds.includes(kind))];
};

/**
 * The kinds with `kind` switched: on when it was off, off when it was on. Null when switching
 * it off would leave none — a link always syncs at least one kind.
 */
export const toggleKind = (kinds: readonly string[], kind: string): string[] | null => {
  if (!kinds.includes(kind)) return orderKinds([...kinds, kind]);
  if (kinds.length <= 1) return null;

  return orderKinds(kinds.filter((item) => item !== kind));
};

// ---- modes -------------------------------------------------------------------------------------

export type ModeName = "off" | "follow" | "twoWay";

export const modeName = (mode?: DataSyncLinkMode | number | null): ModeName =>
  mode === DataSyncLinkMode.TwoWay ? "twoWay" : mode === DataSyncLinkMode.Follow ? "follow" : "off";

export const modeValue = (name: ModeName): DataSyncLinkMode =>
  name === "twoWay"
    ? DataSyncLinkMode.TwoWay
    : name === "follow"
      ? DataSyncLinkMode.Follow
      : DataSyncLinkMode.Off;

/** A peer's mode towards this device as the server words it (`"follow"`, `"twoWay"`). */
const peerModeName = (mode?: string | null): ModeName | undefined => {
  const value = mode?.trim().toLowerCase();

  if (value === "twoway") return "twoWay";
  if (value === "follow") return "follow";

  return undefined;
};

// ---- one peer ----------------------------------------------------------------------------------

/** How a direction is drawn: working, waiting, or not there. */
export type LaneStatus = "active" | "pending" | "none";

/** What does not work on the receive direction right now — the map's issue names. */
export type SyncIssue = "syncPaused" | "syncFailed" | "syncUpdateNeeded" | "syncAccessLost";

/** How a status reads at a glance: its dot, its colour. */
export type Tone = "success" | "primary" | "warning" | "danger" | "default";

/** An ended request of this device's own: rejected or expired. */
export type SyncOutcome = "awaitingApproval" | "rejected" | "expired";

/**
 * One other device as data sync sees it: this device's link to it, what it reads of this
 * device, and what it says about itself. Built from a link's full view on the page, and from
 * the map view's slimmer record where only that is at hand (the device map).
 */
export interface SyncPeer {
  nodeId: string;
  name: string;
  address?: string;
  linkId?: number;
  mode: DataSyncLinkMode;
  lastMode: DataSyncLinkMode;
  state?: DataSyncLinkState;
  pausedReason?: DataSyncPauseReason;
  pausedDetail?: string;
  initiator?: DataSyncLinkInitiator;
  /** The kinds this device receives from it. */
  kinds: string[];
  /** The kinds it receives from this device, as its own link says. */
  peerKinds?: string[];
  /** It may read this device's definitions (this device's grant). */
  peerMayRead: boolean;
  /**
   * This device may read its definitions already, so linking to it sends no request. Unknown
   * (undefined) where nothing said — then a new link is taken to send one.
   */
  weMayRead?: boolean;
  /** Its own link to this device: `follow`, `twoWay`, or none. */
  peerMode?: ModeName;
  peerLastReadAt?: string;
  lastSyncedAt?: string;
  nextAttemptAt?: string;
  lastErrorCode?: string;
  lastErrorDetail?: string;
  openItems: number;
  attention?: DataSyncSourceAttention;
  readBackDeclined: boolean;
  /** Undefined where nothing said (the map view carries no presence). */
  online?: boolean;
  receivingFlag?: boolean;
  receivingPendingFlag?: boolean;
  reviewId?: string;
  pendingCount: number;
  heldCount: number;
  excludedCount: number;
  missingAtPeerCount: number;
  peerAppVersion?: string;
  /** This device's own request to it, when it is waiting or has ended. */
  outcome?: SyncOutcome;
  outcomeExpiresAt?: string;
  /** What reads this device's definitions there, when it has no link of this device's. */
  reader?: DataSyncReaderView;
  /**
   * Waiting for its first review: from when this device may start without it ([Start anyway],
   * spec §8.3).
   */
  startAnywayAt?: string;
  /** A full reconciliation with it is being fetched, waits to be applied, or is being applied (§8.8). */
  fullReconciliationRunning?: boolean;
}

const asOutcome = (value?: string | null): SyncOutcome | undefined =>
  value === "awaitingApproval" || value === "rejected" || value === "expired" ? value : undefined;

const outcomeOfError = (code?: string | null): SyncOutcome | undefined =>
  code === "AccessRejected" ? "rejected" : code === "AccessExpired" ? "expired" : undefined;

export const syncPeerFromLink = (link: DataSyncLinkView): SyncPeer => ({
  nodeId: link.peerNodeId,
  name: link.peerName,
  address: link.peerAddress ?? undefined,
  linkId: link.id,
  mode: link.mode,
  lastMode: link.lastMode,
  state: link.state,
  pausedReason: link.pausedReason ?? undefined,
  pausedDetail: link.pausedDetail ?? undefined,
  initiator: link.initiator,
  kinds: orderKinds(link.kinds ?? []),
  peerKinds: link.peerKinds ? orderKinds(link.peerKinds) : undefined,
  peerMayRead: link.peerMayReadUs,
  peerMode: peerModeName(link.peerModeTowardsUs),
  peerLastReadAt: link.peerLastReadAt ?? undefined,
  lastSyncedAt: link.lastSyncedAt ?? undefined,
  nextAttemptAt: link.nextAttemptAt ?? undefined,
  lastErrorCode: link.lastErrorCode ?? undefined,
  lastErrorDetail: link.lastErrorDetail ?? undefined,
  openItems: link.openItems,
  attention: link.peerAttention ?? undefined,
  readBackDeclined: link.readBackDeclined,
  online: link.peerOnline,
  reviewId: link.reviewId ?? undefined,
  pendingCount: link.pendingCount,
  heldCount: link.heldCount,
  excludedCount: link.excludedCount,
  missingAtPeerCount: link.missingAtPeerCount,
  peerAppVersion: link.peerAppVersion ?? undefined,
  startAnywayAt: link.startAnywayAt ?? undefined,
  fullReconciliationRunning: link.fullReconciliationRunning,
  outcome:
    link.state === DataSyncLinkState.AwaitingAccess && link.initiator !== DataSyncLinkInitiator.Peer
      ? "awaitingApproval"
      : link.state === DataSyncLinkState.Stopped
        ? outcomeOfError(link.lastErrorCode)
        : undefined,
});

export const syncPeerFromMapPeer = (peer: DataSyncMapPeer): SyncPeer => ({
  nodeId: peer.nodeId,
  name: peer.name,
  linkId: peer.linkId ?? undefined,
  mode: peer.mode,
  lastMode: peer.lastMode,
  state: peer.state ?? undefined,
  pausedReason: peer.pausedReason ?? undefined,
  initiator: peer.initiator ?? undefined,
  kinds: orderKinds(peer.kinds ?? []),
  peerKinds: peer.peerKinds ? orderKinds(peer.peerKinds) : undefined,
  peerMayRead: peer.peerMayRead,
  peerMode: peerModeName(peer.peerMode),
  peerLastReadAt: peer.peerLastReadAt ?? undefined,
  lastSyncedAt: peer.lastSyncedAt ?? undefined,
  lastErrorCode: peer.lastErrorCode ?? undefined,
  openItems: peer.openItems,
  attention: peer.attention ?? undefined,
  readBackDeclined: peer.readBackDeclined,
  receivingFlag: peer.receiving,
  receivingPendingFlag: peer.receivingPending,
  pendingCount: 0,
  heldCount: peer.heldCount,
  excludedCount: peer.excludedCount,
  missingAtPeerCount: peer.missingAtPeerCount,
  startAnywayAt: peer.startAnywayAt ?? undefined,
  fullReconciliationRunning: peer.fullReconciliationRunning,
  outcome:
    peer.state === DataSyncLinkState.Stopped ? outcomeOfError(peer.lastErrorCode) : undefined,
});

const withOutgoing = (peer: SyncPeer, outgoing: DataSyncMapOutgoing): SyncPeer => ({
  ...peer,
  address: peer.address ?? outgoing.address ?? undefined,
  linkId: peer.linkId ?? outgoing.linkId,
  state: peer.state ?? outgoing.state,
  outcome: asOutcome(outgoing.outcome) ?? peer.outcome,
  outcomeExpiresAt: outgoing.expiresAt ?? undefined,
});

const fromOutgoing = (outgoing: DataSyncMapOutgoing): SyncPeer =>
  withOutgoing(
    {
      nodeId: outgoing.nodeId,
      name: outgoing.nodeName,
      mode: DataSyncLinkMode.Off,
      lastMode: DataSyncLinkMode.Off,
      kinds: [...dataSyncKinds],
      peerMayRead: false,
      openItems: 0,
      readBackDeclined: false,
      pendingCount: 0,
      heldCount: 0,
      excludedCount: 0,
      missingAtPeerCount: 0,
    },
    outgoing,
  );

/**
 * A device data sync has nothing to do with yet, to start syncing with: no link, no grant.
 * `address` is where a request to it goes, when it is not a device the server knows by its id.
 */
export const newSyncPeer = (nodeId: string, name: string, address?: string): SyncPeer => ({
  nodeId,
  name,
  address,
  mode: DataSyncLinkMode.Off,
  lastMode: DataSyncLinkMode.Off,
  kinds: [...dataSyncKinds],
  peerMayRead: false,
  openItems: 0,
  readBackDeclined: false,
  pendingCount: 0,
  heldCount: 0,
  excludedCount: 0,
  missingAtPeerCount: 0,
});

/**
 * One device from the map view's records about it alone — its link or grant, and this device's
 * own request to it — as the device map has them; none when it has neither.
 */
export const syncPeerOfRecords = (
  peer?: DataSyncMapPeer,
  outgoing?: DataSyncMapOutgoing,
): SyncPeer | undefined => {
  const known = peer ? syncPeerFromMapPeer(peer) : undefined;

  if (!outgoing) return known;

  return known ? withOutgoing(known, outgoing) : fromOutgoing(outgoing);
};

const fromReader = (reader: DataSyncReaderView): SyncPeer => ({
  nodeId: reader.nodeId,
  name: reader.name,
  mode: DataSyncLinkMode.Off,
  lastMode: DataSyncLinkMode.Off,
  kinds: [...dataSyncKinds],
  peerMayRead: true,
  peerMode: peerModeName(reader.mode),
  peerLastReadAt: reader.lastReadAt ?? undefined,
  openItems: 0,
  readBackDeclined: false,
  pendingCount: 0,
  heldCount: 0,
  excludedCount: 0,
  missingAtPeerCount: 0,
  reader,
});

/**
 * Every device data sync has anything to do with, once each: this device's links (their full
 * view where the page has it), the map view's other records, its own requests that wait or
 * ended — never dropped while the reader has not dismissed them — and the devices that only
 * read this one. Sorted by name, so the drawing keeps its order between reads.
 */
export function syncPeersOf({
  links,
  map,
  readers,
}: {
  links?: DataSyncLinkView[];
  map?: DataSyncMapView;
  readers?: DataSyncReaderView[];
}): SyncPeer[] {
  const peers = new Map<string, SyncPeer>();

  for (const link of links ?? []) peers.set(link.peerNodeId, syncPeerFromLink(link));
  for (const peer of map?.peers ?? []) {
    const known = peers.get(peer.nodeId);

    if (!known) peers.set(peer.nodeId, syncPeerFromMapPeer(peer));
    else if (!known.attention && peer.attention) known.attention = peer.attention;
  }
  for (const outgoing of map?.outgoing ?? []) {
    const known = peers.get(outgoing.nodeId);

    peers.set(outgoing.nodeId, known ? withOutgoing(known, outgoing) : fromOutgoing(outgoing));
  }
  for (const reader of readers ?? []) {
    const known = peers.get(reader.nodeId);

    if (!known) peers.set(reader.nodeId, fromReader(reader));
    else known.reader = reader;
  }

  return Array.from(peers.values()).sort(
    (a, b) => a.name.localeCompare(b.name) || a.nodeId.localeCompare(b.nodeId),
  );
}

const waitingStates = new Set<DataSyncLinkState>([
  DataSyncLinkState.AwaitingAccess,
  DataSyncLinkState.AwaitingReview,
  DataSyncLinkState.WaitingForPeerReview,
]);

/**
 * What the candidates list (`GET /data-sync/peers`) says each device allows: whether this device
 * may read it already. A device this device reads over a link already needs no request either.
 */
export const withCandidates = (
  peers: SyncPeer[],
  candidates: { nodeId: string; weMayRead: boolean }[] | undefined,
): SyncPeer[] => {
  if (!candidates?.length) return peers;
  const reads = new Map(candidates.map((candidate) => [candidate.nodeId, candidate.weMayRead]));

  return peers.map((peer) =>
    reads.has(peer.nodeId) ? { ...peer, weMayRead: reads.get(peer.nodeId) } : peer,
  );
};

/** Errors that say only that the other device could not be reached: shown grey, never as failures. */
const offlineErrors = new Set([DataSyncPeerErrorCodeLabel[DataSyncPeerErrorCode.Unreachable]]);

/** Codes a link carries that have a line of their own rather than "Sync failed". */
const ownLineErrors = new Set([
  "AccessRejected",
  "AccessExpired",
  "PeerTooOld",
  "ThisTooOld",
  "AccessRevoked",
  "PeerSharingOff",
  "PeerRemoteAccessOff",
  "PeerRestorePending",
]);

/** Whether the other device answered the last time; undefined where nothing said. */
export const isOffline = (peer: SyncPeer) =>
  peer.online === false || (!!peer.lastErrorCode && offlineErrors.has(peer.lastErrorCode));

/** An error on a working link that is neither "offline" nor a state of its own. */
const failure = (peer: SyncPeer) =>
  peer.state === DataSyncLinkState.Active &&
  !!peer.lastErrorCode &&
  !offlineErrors.has(peer.lastErrorCode) &&
  !ownLineErrors.has(peer.lastErrorCode) &&
  peer.online !== false
    ? peer.lastErrorCode
    : undefined;

/**
 * Keeping in step both ways was approved here, but reading the other device back failed (spec
 * §7.2.4): the link waits for access nobody is asked for, with the failure on it. Both the link
 * view and the map view say who started the link.
 */
export const readBackFailed = (peer: SyncPeer) =>
  peer.state === DataSyncLinkState.AwaitingAccess &&
  !!peer.lastErrorCode &&
  peer.initiator === DataSyncLinkInitiator.Peer;

/** What does not work on the receive direction right now, if anything. */
export const syncIssueOf = (peer: SyncPeer): SyncIssue | undefined => {
  if (readBackFailed(peer)) return "syncFailed";
  switch (peer.state) {
    case DataSyncLinkState.Paused:
      return "syncPaused";
    case DataSyncLinkState.PeerTooOld:
    case DataSyncLinkState.ThisTooOld:
      return "syncUpdateNeeded";
    case DataSyncLinkState.AccessRevoked:
    case DataSyncLinkState.PeerSharingOff:
    case DataSyncLinkState.PeerRemoteAccessOff:
      return "syncAccessLost";
    default:
      return failure(peer) ? "syncFailed" : undefined;
  }
};

/** How the receive direction (the other device → this device) is drawn. */
export const receiveLane = (peer: SyncPeer): LaneStatus => {
  if (peer.state !== undefined && waitingStates.has(peer.state)) return "pending";
  if (peer.state === DataSyncLinkState.Stopped) return "none";
  if (peer.mode !== DataSyncLinkMode.Off) return "active";
  if (peer.receivingFlag) return "active";

  return peer.receivingPendingFlag ? "pending" : "none";
};

/** How the may-read direction (this device → the other device) is drawn. */
export const readLane = (peer: SyncPeer): LaneStatus => (peer.peerMayRead ? "active" : "none");

/** Each receives from the other: two Follow links that together work as keeping in step (§8.1). */
export const isMutualFollow = (peer: SyncPeer) =>
  peer.mode === DataSyncLinkMode.Follow && peer.peerMode === "follow";

/**
 * The mode a drawing's line shows on its badge: this link's mode — "both ways" also when both
 * devices follow each other, which works as that — or none when this device does not receive.
 */
export const lineMode = (peer: SyncPeer): "follow" | "twoWay" | undefined => {
  if (peer.mode === DataSyncLinkMode.TwoWay || isMutualFollow(peer)) return "twoWay";
  if (peer.mode === DataSyncLinkMode.Follow) return "follow";

  return undefined;
};

// ---- the rule editor ---------------------------------------------------------------------------

/**
 * What the rule editor shows. The receive arrow, its mode badge and the mode buttons always
 * say the same thing: the arrow is pressed exactly when the mode is not Off, and the badge and
 * the buttons name that mode.
 */
export interface LinkEditor {
  /** The mode buttons' value: this link's own mode. */
  mode: ModeName;
  /** Whether the receive arrow is pressed. */
  receiving: boolean;
  receive: LaneStatus;
  read: LaneStatus;
  /** The mode badge on the receive arrow; none while it is off. */
  badge?: "follow" | "twoWay";
  /** What the receive arrow turns on: the link's last mode, both ways for a new one. */
  lastMode: "follow" | "twoWay";
  mutualFollow: boolean;
  kinds: string[];
  /** The one kind that cannot be switched off, because it is the last. */
  lockedKind?: string;
}

export const linkEditor = (peer: SyncPeer): LinkEditor => {
  const mode = modeName(peer.mode);
  const last = modeName(peer.lastMode);
  const kinds = peer.kinds.length ? peer.kinds : [...dataSyncKinds];

  return {
    mode,
    receiving: mode !== "off",
    receive: receiveLane(peer),
    read: readLane(peer),
    badge: mode === "off" ? undefined : mode,
    lastMode: last === "off" ? "twoWay" : last,
    mutualFollow: isMutualFollow(peer),
    kinds,
    lockedKind: kinds.length === 1 ? kinds[0] : undefined,
  };
};

/** What pressing the receive arrow turns the link to: off when on, else its last mode. */
export const receiveToggleTarget = (editor: LinkEditor): ModeName =>
  editor.receiving ? "off" : editor.lastMode;

/** This device's own side, as keeping in step both ways needs it. */
export interface OwnSharing {
  sharingEnabled: boolean;
  remoteAccessMode: RemoteAccessMode | number;
}

/** Whether the other device can read this one only once sharing, or remote access, is turned on here. */
export const sharingNeeded = (own: OwnSharing) =>
  !own.sharingEnabled || own.remoteAccessMode === RemoteAccessMode.Disabled;

/**
 * The confirmation for keeping in step both ways with a device (spec §11.1, §7.2.4), worded the
 * same wherever it is offered: the consent, and what this device turns on so the other can read
 * it — sharing only where it is off, remote access only where it is off. `turnsOn` is false where
 * nothing is turned on (the other device reads this one already).
 */
export const twoWayConfirmation = (t: T, name: string, own: OwnSharing, turnsOn = true) => ({
  title: t("dataSync.twoWay.title", { name }),
  description: t("dataSync.twoWay.consent", { name }),
  warning: turnsOn ? turnsOnWarning(t, own) : undefined,
});

/**
 * What making this device readable turns on here, said only for what is off: sharing, remote
 * access (with pairing required). Undefined when both are on.
 */
export const turnsOnWarning = (t: T, own: OwnSharing) =>
  [
    own.sharingEnabled ? undefined : t("dataSync.twoWay.turnsOnSharing"),
    own.remoteAccessMode === RemoteAccessMode.Disabled
      ? t("dataSync.sharing.remoteAccess")
      : undefined,
  ]
    .filter(Boolean)
    .join(" ") || undefined;

/**
 * Whether "[Ask {{name}} to keep in step]" is offered (spec §7.2.3): a two-way link, working, whose
 * device took this one's access but does not read it back. The runtime answers the resume action
 * `AskAccessAgain` on exactly such a link with an ordinary two-way request, leaving the link's
 * state alone; on a paused or stopped link that action means something else, so it is not offered.
 */
export const offersAskToKeepInStep = (peer: SyncPeer) =>
  peer.readBackDeclined &&
  peer.linkId !== undefined &&
  peer.mode === DataSyncLinkMode.TwoWay &&
  peer.state !== DataSyncLinkState.Paused &&
  peer.state !== DataSyncLinkState.Stopped;

/**
 * Receiving from it is off while it still reads this device: the rule editor offers to stop that
 * too ([Also stop it reading], spec §11.1).
 */
export const stillReadsWhileOff = (peer: SyncPeer) =>
  peer.linkId !== undefined &&
  peer.state === DataSyncLinkState.Stopped &&
  peer.mode === DataSyncLinkMode.Off &&
  !peer.outcome &&
  peer.peerMayRead;

/**
 * Whether a link that has waited for its peer's first review may start without it now ([Start
 * anyway], spec §8.3): once the runtime says from when.
 */
export const canStartAnyway = (peer: SyncPeer, now: number = Date.now()) => {
  if (peer.state !== DataSyncLinkState.WaitingForPeerReview || !peer.startAnywayAt) return false;
  const at = serverTime(peer.startAnywayAt);

  return at !== null && at.getTime() <= now;
};

// ---- the status catalogue (§11.6) ----------------------------------------------------------------

/**
 * One line of the status catalogue. `code` names the entry (`InStep`, `Paused.MassDeletion`,
 * …); `text` is what it says.
 */
export interface StatusLine {
  code: string;
  text: string;
  tone: Tone;
}

const line = (code: string, tone: Tone, text: string): StatusLine => ({ code, text, tone });

/** The words for why a sync failed, from the peer error code the link carries. */
export const failureReason = (t: T, code?: string) =>
  code && Object.values(DataSyncPeerErrorCodeLabel).includes(code)
    ? t(`dataSync.peerError.${code}`)
    : code
      ? t("dataSync.peerError.other", { code })
      : t("dataSync.peerError.other", { code: "?" });

/** Counts a pause detail carries (`"deletions=182;kind=customProperty"`). */
export const pauseDetail = (detail?: string) => {
  const values: Record<string, string> = {};

  for (const part of (detail ?? "").split(";")) {
    const at = part.indexOf("=");

    if (at > 0) values[part.slice(0, at).trim()] = part.slice(at + 1).trim();
    else if (part.trim()) values[part.trim()] = "true";
  }

  return values;
};

const kindWords = (t: T, kind?: string) =>
  kind ? t(`dataSync.kind.${kind}`, { defaultValue: kind }) : t("dataSync.kind.any");

/** Why a link is paused, as the catalogue says it. */
export const pausedLine = (t: T, peer: SyncPeer): StatusLine => {
  const name = peer.name;
  const reason = peer.pausedReason ?? DataSyncPauseReason.ByUser;
  const detail = pauseDetail(peer.pausedDetail);
  const label = DataSyncPauseReasonLabel[reason] ?? "ByUser";

  switch (reason) {
    case DataSyncPauseReason.PeerReset:
      return detail.restored
        ? line(
            "Paused.PeerResetRestored",
            "warning",
            t("dataSync.status.paused.PeerResetRestored", { name }),
          )
        : line("Paused.PeerReset", "warning", t("dataSync.status.paused.PeerReset", { name }));
    case DataSyncPauseReason.MassDeletion:
      return line(
        "Paused.MassDeletion",
        "warning",
        t("dataSync.status.paused.MassDeletion", {
          name,
          count: Number(detail.deletions ?? detail.count ?? 0),
        }),
      );
    case DataSyncPauseReason.KindEmptied:
      return line(
        "Paused.KindEmptied",
        "warning",
        t("dataSync.status.paused.KindEmptied", { name, kind: kindWords(t, detail.kind) }),
      );
    case DataSyncPauseReason.AllPaused:
      return line("Paused.AllPaused", "default", t("dataSync.status.paused.ByUser"));
    case DataSyncPauseReason.ByUser:
      return line("Paused.ByUser", "default", t("dataSync.status.paused.ByUser"));
    default:
      return line(`Paused.${label}`, "warning", t(`dataSync.status.paused.${label}`, { name }));
  }
};

/**
 * The line a link reads as: the first of the catalogue that holds. A failure is never shown
 * as "nothing to sync".
 */
export function linkStatus(t: T, peer: SyncPeer, now: number = Date.now()): StatusLine {
  const name = peer.name;

  if (peer.outcome === "rejected" || peer.outcome === "expired")
    return line(
      peer.outcome === "rejected" ? "AccessRejected" : "AccessExpired",
      "danger",
      t("dataSync.status.AccessRejected", { name }),
    );
  if (peer.linkId === undefined)
    return peer.peerMayRead
      ? line("ReaderOnly", "default", t("dataSync.status.ReaderOnly", { name }))
      : line("NotLinked", "default", t("dataSync.status.NotLinked", { name }));

  switch (peer.state) {
    case DataSyncLinkState.AwaitingAccess:
      // Nothing waits for an approval there: reading it back failed, and says why.
      return readBackFailed(peer)
        ? line(
            "ReadBackFailed",
            "danger",
            t("dataSync.status.ReadBackFailed", {
              name,
              reason: failureReason(t, peer.lastErrorCode),
            }),
          )
        : line("AwaitingAccess", "primary", t("dataSync.status.AwaitingAccess", { name }));
    case DataSyncLinkState.AwaitingReview:
      return line("AwaitingReview", "primary", t("dataSync.status.AwaitingReview"));
    case DataSyncLinkState.WaitingForPeerReview:
      return line(
        "WaitingForPeerReview",
        "primary",
        t("dataSync.status.WaitingForPeerReview", { name }),
      );
    case DataSyncLinkState.Paused:
      return pausedLine(t, peer);
    case DataSyncLinkState.Stopped:
      return line("Stopped", "default", t("dataSync.status.Stopped", { name }));
    case DataSyncLinkState.PeerTooOld:
      return line("PeerTooOld", "warning", t("dataSync.status.PeerTooOld", { name }));
    case DataSyncLinkState.ThisTooOld:
      return peer.heldCount > 0
        ? line("ThisTooOld", "warning", t("dataSync.status.ThisTooOld", { count: peer.heldCount }))
        : line("ThisTooOld", "warning", t("dataSync.status.ThisTooOldNoCount", { name }));
    case DataSyncLinkState.AccessRevoked:
    case DataSyncLinkState.PeerSharingOff:
      return line("AccessRevoked", "danger", t("dataSync.status.AccessRevoked", { name }));
    case DataSyncLinkState.PeerRemoteAccessOff:
      return line(
        "PeerRemoteAccessOff",
        "danger",
        t("dataSync.status.PeerRemoteAccessOff", { name }),
      );
    default:
      break;
  }

  if (peer.lastErrorCode === "PeerRestorePending")
    return line("PeerRestorePending", "warning", t("dataSync.status.PeerRestorePending", { name }));
  if (peer.lastErrorCode === DataSyncPeerErrorCodeLabel[DataSyncPeerErrorCode.TooLarge])
    return line("TooLarge", "danger", t("dataSync.status.TooLarge", { name }));
  if (isOffline(peer))
    return line(
      "Offline",
      "default",
      t("dataSync.status.Offline", { name, time: timeAgo(t, peer.lastSyncedAt, now) }),
    );
  const failed = failure(peer);

  if (failed)
    return line(
      "Failed",
      "danger",
      t("dataSync.status.Failed", { reason: failureReason(t, failed) }),
    );
  if (peer.openItems > 0)
    return line("NeedsYou", "warning", t("dataSync.status.NeedsYou", { count: peer.openItems }));
  // Working, but never synced yet (§8.1 reaches Active before the first pull): not "in step".
  if (!peer.lastSyncedAt) return line("Syncing", "primary", t("dataSync.status.Syncing"));
  if (peer.fullReconciliationRunning)
    return line("FullReconciliation", "primary", t("dataSync.status.FullReconciliation", { name }));

  return line(
    "InStep",
    "success",
    t("dataSync.status.InStep", { time: timeAgo(t, peer.lastSyncedAt, now) }),
  );
}

/**
 * What a link's details add under its status line: mutual Follow, a read-back that was
 * declined, decisions and a restore waiting on the other device, changes held for a newer
 * version here, and how the other device reads this one.
 */
export function linkNotes(t: T, peer: SyncPeer, now: number = Date.now()): StatusLine[] {
  const name = peer.name;
  const notes: StatusLine[] = [];

  if (isMutualFollow(peer))
    notes.push(line("MutualFollow", "primary", t("dataSync.status.MutualFollow")));
  if (peer.readBackDeclined)
    notes.push(
      line("ReadBackDeclined", "warning", t("dataSync.status.ReadBackDeclined", { name })),
    );
  if (peer.attention?.openDecisions)
    notes.push(
      line(
        "NeedsYouThere",
        "warning",
        t("dataSync.status.NeedsYouThere", { name, count: peer.attention.openDecisions }),
      ),
    );
  if (peer.attention?.restorePending && peer.lastErrorCode !== "PeerRestorePending")
    notes.push(
      line("PeerRestorePending", "warning", t("dataSync.status.PeerRestorePending", { name })),
    );
  if (peer.heldCount > 0 && peer.state !== DataSyncLinkState.ThisTooOld)
    notes.push(line("Held", "warning", t("dataSync.status.ThisTooOld", { count: peer.heldCount })));
  if (peer.peerMayRead && peer.peerMode)
    notes.push(
      line(
        "PeerReads",
        "default",
        t(
          peer.peerMode === "twoWay"
            ? "dataSync.link.peerKeepsInStep"
            : "dataSync.link.peerReceives",
          {
            name,
            time: timeAgo(t, peer.peerLastReadAt, now),
          },
        ),
      ),
    );

  return notes;
}

/** A card's line under its name on the drawing: the status, short. */
export const peerCardLine = (t: T, peer: SyncPeer, now: number = Date.now()) => {
  const status = linkStatus(t, peer, now);

  return {
    ...status,
    text: t(`dataSync.diagram.card.${cardKey(status.code)}`, cardValues(t, peer, now)),
  };
};

const cardKey = (code: string) => {
  if (code.startsWith("Paused.")) return "paused";
  switch (code) {
    case "AccessRejected":
    case "AccessExpired":
      return "notApproved";
    case "InStep":
      return "inStep";
    case "Syncing":
      return "syncing";
    case "Offline":
      return "offline";
    case "NeedsYou":
      return "needsYou";
    case "AwaitingAccess":
      return "awaitingAccess";
    case "AwaitingReview":
      return "awaitingReview";
    case "WaitingForPeerReview":
      return "waitingForPeerReview";
    case "Stopped":
      return "off";
    case "ReaderOnly":
      return "readsHere";
    case "NotLinked":
      return "off";
    case "PeerTooOld":
    case "ThisTooOld":
      return "updateNeeded";
    case "AccessRevoked":
    case "PeerRemoteAccessOff":
      return "accessLost";
    case "PeerRestorePending":
      return "restorePending";
    default:
      return "failed";
  }
};

const cardValues = (t: T, peer: SyncPeer, now: number) => ({
  time: timeAgo(t, peer.lastSyncedAt, now),
  count: peer.openItems,
});

// ---- this device, as a whole ---------------------------------------------------------------------

/**
 * The status indicator's line for the whole device, or none while data sync is off (no links,
 * no readers, no requests) — the indicator is hidden then.
 */
export function overallStatus(
  t: T,
  status: DataSyncStatusView | undefined,
  now: number = Date.now(),
): StatusLine | undefined {
  if (!status || status.level === DataSyncStatusLevel.Off) return undefined;
  switch (status.level) {
    case DataSyncStatusLevel.InStep:
      // Nothing synced yet: not "in step · never".
      return status.lastSyncedAt
        ? line(
            "InStep",
            "success",
            t("dataSync.status.InStep", { time: timeAgo(t, status.lastSyncedAt, now) }),
          )
        : line("Syncing", "primary", t("dataSync.status.Syncing"));
    case DataSyncStatusLevel.Syncing:
      return line("Syncing", "primary", t("dataSync.status.Syncing"));
    case DataSyncStatusLevel.NeedsYou:
      return line(
        "NeedsYou",
        "warning",
        t("dataSync.status.NeedsYou", { count: status.openItems }),
      );
    case DataSyncStatusLevel.Paused:
      return line("Paused", "warning", t("dataSync.status.paused.ByUser"));
    case DataSyncStatusLevel.Offline:
      return line(
        "Offline",
        "default",
        t("dataSync.status.level.Offline", { time: timeAgo(t, status.lastSyncedAt, now) }),
      );
    case DataSyncStatusLevel.Failed:
      return line(
        "Failed",
        "danger",
        t("dataSync.status.Failed", {
          reason: failureReason(t, status.lastErrorCode ?? undefined),
        }),
      );
    case DataSyncStatusLevel.UpdateNeeded:
      return line("UpdateNeeded", "warning", t("dataSync.status.level.UpdateNeeded"));
    default:
      return line("Unknown", "default", t("dataSync.title"));
  }
}

/** The indicator's second line: sources that hold decisions nobody has taken there. */
export const waitingElsewhereLine = (t: T, status: DataSyncStatusView | undefined) =>
  status?.peersNeedingDecisions
    ? t("dataSync.status.peersNeedingDecisions", { count: status.peersNeedingDecisions })
    : undefined;

/**
 * "Waiting on other devices": one line per source whose attention shows open decisions, so
 * the reader knows where to go to decide them.
 */
export const elsewhereLines = (t: T, peers: SyncPeer[]) =>
  peers
    .filter((peer) => (peer.attention?.openDecisions ?? 0) > 0)
    .map((peer) => ({
      nodeId: peer.nodeId,
      name: peer.name,
      count: peer.attention!.openDecisions,
      headless: peer.attention!.headless,
      text: t("dataSync.status.NeedsYouThere", {
        name: peer.name,
        count: peer.attention!.openDecisions,
      }),
    }));

/**
 * A request still waiting for its answer. `GET /data-sync/requests` also lists the ones already
 * approved or rejected, and ones whose time ran out unanswered: none of them asks anything.
 */
export const isPendingRequest = (request: DataSyncAccessRequestView, now: number = Date.now()) =>
  (request.status === "awaitingApproval" || request.status === "pending") &&
  !hasPassed(request.expiresAt, now);

/**
 * This device's own request to read a device's definitions, as `GET /data-sync/requests` lists
 * it, while it waits: what [Cancel] withdraws. Never the link: cancelling a request keeps the
 * link's state.
 */
export const outgoingRequestIdOf = (
  requests: readonly DataSyncAccessRequestView[] | undefined,
  nodeId: string,
  now: number = Date.now(),
) =>
  requests?.find(
    (request) =>
      request.nodeId === nodeId &&
      request.direction === DataSyncRequestDirection.Outgoing &&
      isPendingRequest(request, now),
  )?.requestId;

/** Whether anything data sync shows is waiting on someone right now: read again more often. */
export const isLive = ({
  peers,
  pendingRequests,
  activeTaskId,
}: {
  peers: SyncPeer[];
  pendingRequests: number;
  activeTaskId?: string | null;
}) =>
  !!activeTaskId ||
  pendingRequests > 0 ||
  peers.some(
    (peer) =>
      peer.outcome === "awaitingApproval" ||
      (peer.state !== undefined && waitingStates.has(peer.state)),
  );

// ---- one definition --------------------------------------------------------------------------------

export type EntityBadgeCode =
  | "needsYou"
  | "localOnly"
  | "detached"
  | "heldAtSource"
  | "tooLarge"
  | "unreadable"
  | "differs"
  | "definitionOnly"
  | "synced"
  | "syncedFrom";

/**
 * Why this device holds its own definition back, in the badge's words: too large to travel whole,
 * unreadable here — neither of which an update would help — or written by a newer version.
 */
const heldBadge = (reason: DataSyncHeldReason): EntityBadgeCode => {
  switch (reason) {
    case DataSyncHeldReason.TooLarge:
      return "tooLarge";
    case DataSyncHeldReason.LocalUnreadable:
    case DataSyncHeldReason.Invalid:
      return "unreadable";
    default:
      return "heldAtSource";
  }
};

/** What a definition's sync badge says (Properties and Extension groups pages, the page's list). */
export const entityBadge = (
  entity: DataSyncEntityStatusView,
): { code: EntityBadgeCode; tone: Tone; values: Record<string, unknown> } => {
  if (entity.openItems > 0)
    return { code: "needsYou", tone: "warning", values: { count: entity.openItems } };
  if (entity.state === DataSyncEntitySyncState.LocalOnly)
    return { code: "localOnly", tone: "default", values: {} };
  if (entity.state === DataSyncEntitySyncState.Detached)
    return { code: "detached", tone: "default", values: {} };
  if (entity.heldAtSource != null)
    return {
      code: heldBadge(entity.heldAtSource),
      tone: "warning",
      values: { reason: entity.heldAtSource },
    };
  if (entity.differsFromSource)
    return { code: "differs", tone: "warning", values: { name: entity.originName ?? "" } };
  if (entity.childrenLocal) return { code: "definitionOnly", tone: "primary", values: {} };
  if (entity.originName)
    return { code: "syncedFrom", tone: "success", values: { name: entity.originName } };

  return { code: "synced", tone: "success", values: {} };
};

/** A definition too large to travel whole: offer to sync its definition only. */
export const isTooLargeToSync = (entity: DataSyncEntityStatusView) =>
  entity.heldAtSource === DataSyncHeldReason.TooLarge;

export type EntityMenuAction =
  | "keepLocal"
  | "detach"
  | "rejoin"
  | "definitionOnlyOn"
  | "definitionOnlyOff";

/** What a definition's sync menu offers, in its order. */
export const entityMenu = (
  entity: DataSyncEntityStatusView,
  offersDefinitionOnly: boolean,
): EntityMenuAction[] => {
  if (entity.state !== DataSyncEntitySyncState.Synced) return ["rejoin"];
  const actions: EntityMenuAction[] = ["keepLocal"];

  if (offersDefinitionOnly)
    actions.push(entity.childrenLocal ? "definitionOnlyOff" : "definitionOnlyOn");
  actions.push("detach");

  return actions;
};

// ---- "sync with another device" --------------------------------------------------------------

/**
 * What a device found for the wizard can do, as the list says it under its name: already
 * linked, too old for data sync, not sharing its definitions, readable already, or to be asked.
 * `manageElsewhere`: asking it would create access, which this window may not (§7.1.5).
 */
export type CandidateStatus =
  | "linked"
  | "tooOld"
  | "notSharing"
  | "readable"
  | "asks"
  | "manageElsewhere";

export const candidateStatus = (
  candidate: {
    linkId?: number | null;
    discovered: boolean;
    contractVersion?: number | null;
    sharesDefinitions?: boolean | null;
    weMayRead: boolean;
  },
  canManage: boolean,
): CandidateStatus => {
  if (candidate.linkId != null) return "linked";
  if (
    (candidate.contractVersion != null && candidate.contractVersion < 1) ||
    (candidate.discovered && candidate.contractVersion == null)
  )
    return "tooOld";
  if (candidate.sharesDefinitions === false) return "notSharing";
  if (candidate.weMayRead) return "readable";

  return canManage ? "asks" : "manageElsewhere";
};

/** Whether the wizard lets the reader pick it. */
export const isPickable = (status: CandidateStatus) => status === "readable" || status === "asks";
