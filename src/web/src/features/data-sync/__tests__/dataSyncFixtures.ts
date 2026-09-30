import type { TFunction } from "i18next";
import type {
  DataSyncFirstSyncPreview,
  DataSyncHistoryEntry,
  DataSyncInboxItemView,
  DataSyncMapPeer,
  DataSyncMapRequest,
  DataSyncMapView,
  DataSyncOverview,
  DataSyncPeerCandidate,
  DataSyncPreviewEntry,
  DataSyncReaderView,
  DataSyncStatusView,
} from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { BTask } from "@/core/models/BTask";
import type { BTaskStatus, DataSyncHistoryKind, DataSyncPreviewOutcome } from "@/sdk/constants";

import { vi } from "vitest";

import {
  BTaskResourceType,
  BTaskType,
  DataSyncFieldResolution,
  DataSyncInboxAction,
  DataSyncInboxItemOrigin,
  DataSyncInboxItemType,
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncRequestIntent,
  DataSyncStatusLevel,
  DataSyncUndoState,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * Records the way `/data-sync` answers them: every time UTC, written as the
 * server writes it — naked digits, no zone.
 */

export const NOW = Date.UTC(2026, 8, 1, 8, 0, 0);
const MINUTE = 60_000;

/** A time the way Newtonsoft writes a UTC `DateTime`: no `T`, no zone. */
export const naked = (at: number) => new Date(at).toISOString().replace("T", " ").replace("Z", "");
export const minutesAgo = (minutes: number) => naked(NOW - minutes * MINUTE);
export const minutesAhead = (minutes: number) => naked(NOW + minutes * MINUTE);

export const allKinds = ["customProperty", "extensionGroup"];

export const status = (patch: Partial<DataSyncStatusView> = {}): DataSyncStatusView => ({
  level: DataSyncStatusLevel.InStep,
  openItems: 0,
  links: 1,
  linksInStep: 1,
  peersNeedingDecisions: 0,
  lastSyncedAt: minutesAgo(5),
  pendingRequests: 0,
  readers: 0,
  linksToReview: 0,
  linksWaiting: 0,
  ...patch,
});

export const overview = (patch: Partial<DataSyncOverview> = {}): DataSyncOverview => ({
  deviceName: "This PC",
  nodeId: "node-self",
  isHeadless: false,
  sharingEnabled: true,
  remoteAccessMode: RemoteAccessMode.Enabled,
  canManageSharing: true,
  newDefinitionsStayLocal: false,
  allPaused: false,
  kinds: [
    { kind: "extensionGroup", count: 8 },
    { kind: "customProperty", count: 100 },
  ],
  status: status(),
  restorePending: false,
  openInboxItems: 0,
  pendingRequests: 0,
  databaseBytes: 52_428_800,
  reachableAddresses: ["http://192.168.1.10:34567"],
  ...patch,
});

type DataSyncLinkView = NonNullable<DataSyncMapPeer["link"]>;
type DataSyncOwnRequest = NonNullable<DataSyncMapPeer["request"]>;

export const link = (
  id: number,
  peerNodeId: string,
  peerName: string,
  patch: Partial<DataSyncLinkView> = {},
): DataSyncLinkView => ({
  id,
  peerNodeId,
  peerName,
  peerAddress: `192.168.1.${id * 10}:34567`,
  mode: DataSyncLinkMode.TwoWay,
  lastMode: DataSyncLinkMode.TwoWay,
  state: DataSyncLinkState.Active,
  initiator: DataSyncLinkInitiator.ThisDevice,
  kinds: allKinds,
  peerKinds: allKinds,
  lastSyncedAt: minutesAgo(5),
  openItems: 0,
  pendingCount: 0,
  peerAppVersion: "2.5.0-beta.10",
  peerContractVersion: 1,
  peerMayReadUs: true,
  readBackDeclined: false,
  peerModeTowardsUs: "twoWay",
  peerLastReadAt: minutesAgo(6),
  excludedCount: 0,
  heldCount: 0,
  missingAtPeerCount: 0,
  fullReconciliationRunning: false,
  ...patch,
});

/**
 * One device as the map view has it: its link (`null` for none) built from `linkPatch`, its grant
 * to read this device while the link says it may, and no request of this device's own.
 */
export const mapPeer = (
  nodeId: string,
  name: string,
  linkPatch: Partial<DataSyncLinkView> | null = {},
  patch: Partial<DataSyncMapPeer> = {},
): DataSyncMapPeer => {
  const view = linkPatch ? link(linkPatch.id ?? 1, nodeId, name, linkPatch) : undefined;

  return {
    nodeId,
    name,
    link: view,
    reader: !view || view.peerMayReadUs ? reader(nodeId, name) : undefined,
    ...patch,
  };
};

/** This device's own request to a device, as its map record carries it: waiting, by default. */
export const ownRequest = (patch: Partial<DataSyncOwnRequest> = {}): DataSyncOwnRequest => ({
  requestId: "req-out-1",
  outcome: "awaitingApproval",
  expiresAt: minutesAhead(60),
  address: "192.168.1.20:34567",
  ...patch,
});

export const mapView = (patch: Partial<DataSyncMapView> = {}): DataSyncMapView => ({
  sharingEnabled: true,
  remoteAccessMode: RemoteAccessMode.Enabled,
  peers: [],
  requests: [],
  ...patch,
});

/** A request to read this device's definitions, as the map view carries it: a claim. */
export const mapRequest = (
  requestId: string,
  nodeId: string,
  nodeName: string,
  patch: Partial<DataSyncMapRequest> = {},
): DataSyncMapRequest => ({
  requestId,
  nodeId,
  nodeName,
  remoteAddress: "192.168.1.40",
  intent: DataSyncRequestIntent.Follow,
  expiresAt: minutesAhead(30),
  claimsKnownDevice: false,
  replacesExistingAccess: false,
  ...patch,
});

export const reader = (
  nodeId: string,
  name: string,
  patch: Partial<DataSyncReaderView> = {},
): DataSyncReaderView => ({
  nodeId,
  name,
  lastReadAt: minutesAgo(6),
  mode: "twoWay",
  // As a reader declares it (spec §7.5.6): ok, awaitingReview, waitingForPeerReview,
  // paused:{reason} or needsYou:{n}.
  state: "ok",
  upToDate: true,
  ...patch,
});

export const candidate = (
  nodeId: string,
  name: string,
  patch: Partial<DataSyncPeerCandidate> = {},
): DataSyncPeerCandidate => ({
  nodeId,
  name,
  address: "192.168.1.40:34567",
  known: true,
  discovered: true,
  contractVersion: 1,
  sharesDefinitions: true,
  weMayRead: false,
  theyMayRead: false,
  ...patch,
});

// ---- the first sync ----------------------------------------------------------------------------

export const previewEntry = (
  key: string,
  outcome: DataSyncPreviewOutcome,
  name: string,
  patch: Partial<DataSyncPreviewEntry> = {},
): DataSyncPreviewEntry => ({
  kind: "customProperty",
  key,
  name,
  subtype: "SingleChoice",
  outcome,
  ...patch,
});

export const firstSyncPreview = (
  entries: DataSyncPreviewEntry[],
  patch: Partial<DataSyncFirstSyncPreview> = {},
): DataSyncFirstSyncPreview => ({
  linkId: 3,
  copyOnce: false,
  mode: DataSyncLinkMode.Follow,
  state: DataSyncLinkState.AwaitingReview,
  source: {
    nodeId: "node-laptop",
    name: "Laptop",
    appVersion: "2.5.0-beta.10",
    fetchedAt: minutesAgo(12),
    kinds: [{ kind: "customProperty", count: entries.length }],
  },
  entries,
  ...patch,
});

// ---- Needs you ---------------------------------------------------------------------------------

export const inboxPayload = (
  patch: Partial<DataSyncInboxItemView["payload"]> = {},
): DataSyncInboxItemView["payload"] => ({
  entityName: "Genre",
  subtype: "MultipleChoice",
  peerName: "NAS",
  remoteEditor: { nodeId: "node-nas", name: "NAS", actorId: "actor-nas-1" },
  originName: "This PC",
  fields: [],
  childrenTotal: 0,
  ...patch,
});

export const inboxItem = (
  id: number,
  type: DataSyncInboxItemType,
  actions: DataSyncInboxAction[],
  patch: Partial<DataSyncInboxItemView> = {},
): DataSyncInboxItemView => ({
  id,
  linkId: 1,
  peerNodeId: "node-nas",
  peerName: "NAS",
  kind: "customProperty",
  localKey: "12",
  type,
  origin: DataSyncInboxItemOrigin.Merger,
  subjectPath: "",
  payload: inboxPayload(),
  allowedActions: actions,
  token: `token-${id}`,
  createdAt: minutesAgo(60),
  updatedAt: minutesAgo(10),
  ...patch,
});

/** A rename both devices made to the definition's name: this device 作者, the NAS Artists. */
export const nameConflict = (
  id: number,
  patch: Partial<DataSyncInboxItemView> = {},
  remote = "Artists",
) =>
  inboxItem(
    id,
    DataSyncInboxItemType.FieldConflict,
    [
      DataSyncInboxAction.KeepLocal,
      DataSyncInboxAction.UseRemote,
      DataSyncInboxAction.UseCustom,
      DataSyncInboxAction.Detach,
    ],
    {
      subjectPath: "name",
      payload: inboxPayload({
        entityName: "作者",
        fields: [
          {
            path: "name",
            resolution: DataSyncFieldResolution.Conflict,
            base: { text: "Artist" },
            local: { text: "作者" },
            remote: { text: remote },
          },
        ],
      }),
      ...patch,
    },
  );

// ---- the history --------------------------------------------------------------------------------

export const historyCountsOf = (
  patch: Partial<DataSyncHistoryEntry["counts"]> = {},
): DataSyncHistoryEntry["counts"] => ({
  created: 0,
  updated: 0,
  linked: 0,
  unchanged: 0,
  skipped: 0,
  changedSinceReview: 0,
  changedDuringApply: 0,
  held: 0,
  deleted: 0,
  typeChanged: 0,
  reordered: 0,
  resolved: 0,
  ...patch,
});

export const historyEntry = (
  id: number,
  kind: DataSyncHistoryKind,
  patch: Partial<DataSyncHistoryEntry> = {},
): DataSyncHistoryEntry => ({
  id,
  appliedAt: minutesAgo(id * 60),
  kind,
  linkId: 1,
  peerNodeId: "node-nas",
  peerName: "NAS",
  counts: historyCountsOf({ created: 1, updated: 2 }),
  undoState: DataSyncUndoState.Available,
  ...patch,
});

/** A translation function that says the key and the values it was given. */
export const keyT = ((key: string, options?: Record<string, unknown>) =>
  options
    ? [
        key,
        ...Object.entries(options)
          .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
          .map(([, value]) => String(value)),
      ].join(" ")
    : key) as unknown as TFunction;

/** A task as the task list pushes it: `createdAt` tells one run under an id from another. */
export const bTask = (
  id: string,
  status: BTaskStatus,
  createdAt: string,
  patch: Partial<BTask> = {},
): BTask => ({
  id,
  name: id,
  status,
  createdAt,
  isPersistent: true,
  type: BTaskType.Any,
  resourceType: BTaskResourceType.Any,
  ...patch,
});

/** A host's actions, recorded: `run` runs the operation, `confirm` only records. */
export const recordingActions = (busy = false) => {
  const confirmations: Parameters<DataSyncPanelActions["confirm"]>[0][] = [];
  const actions = {
    busy,
    mounted: { current: true },
    setNotice: vi.fn(),
    run: vi.fn(async (operation: () => Promise<unknown>) => {
      await operation();

      return true;
    }),
    confirm: vi.fn((confirmation) => {
      confirmations.push(confirmation);
    }),
  } satisfies DataSyncPanelActions;

  return { actions, confirmations };
};
