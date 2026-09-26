import type { TFunction } from "i18next";
import type {
  DataSyncAccessRequestView,
  DataSyncFieldChange,
  DataSyncHistoryEntry,
  DataSyncInboxItemView,
  DataSyncLinkView,
  DataSyncMapOutgoing,
  DataSyncMapPeer,
  DataSyncMapRequest,
  DataSyncMapView,
  DataSyncOverview,
  DataSyncPeerCandidate,
  DataSyncPlan,
  DataSyncPlanCandidate,
  DataSyncPlanItem,
  DataSyncReaderView,
  DataSyncReviewResult,
  DataSyncStatusView,
} from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { BTask } from "@/core/models/BTask";
import type { BTaskStatus, DataSyncHistoryKind } from "@/sdk/constants";

import { vi } from "vitest";

import {
  BTaskResourceType,
  BTaskType,
  DataSyncFieldChangeKind,
  DataSyncFieldResolution,
  DataSyncInboxAction,
  DataSyncInboxItemOrigin,
  DataSyncInboxItemType,
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncNaturalMatch,
  DataSyncPlanItemType,
  DataSyncPlanResolution,
  DataSyncRequestDirection,
  DataSyncRequestIntent,
  DataSyncReviewState,
  DataSyncStatusLevel,
  DataSyncUndoState,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * Records the way `/data-sync` answers them, after the service's canned data
 * (`Bakabase.Tests/DataSync/Api/FakeDataSyncService.cs`): every time UTC, written as the
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
  nextAttemptAt: minutesAhead(1),
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
  peerOnline: true,
  fullReconciliationRunning: false,
  ...patch,
});

export const mapPeer = (
  nodeId: string,
  name: string,
  patch: Partial<DataSyncMapPeer> = {},
): DataSyncMapPeer => ({
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
  peerKinds: allKinds,
  peerLastReadAt: minutesAgo(6),
  lastSyncedAt: minutesAgo(5),
  openItems: 0,
  readBackDeclined: false,
  kinds: allKinds,
  excludedCount: 0,
  heldCount: 0,
  missingAtPeerCount: 0,
  initiator: DataSyncLinkInitiator.ThisDevice,
  fullReconciliationRunning: false,
  ...patch,
});

export const outgoing = (
  linkId: number,
  nodeId: string,
  nodeName: string,
  patch: Partial<DataSyncMapOutgoing> = {},
): DataSyncMapOutgoing => ({
  linkId,
  nodeId,
  nodeName,
  address: `192.168.1.${linkId * 10}:34567`,
  state: DataSyncLinkState.AwaitingAccess,
  outcome: "awaitingApproval",
  expiresAt: minutesAhead(60),
  ...patch,
});

export const mapView = (patch: Partial<DataSyncMapView> = {}): DataSyncMapView => ({
  sharingEnabled: true,
  remoteAccessMode: RemoteAccessMode.Enabled,
  peers: [],
  requests: [],
  outgoing: [],
  ...patch,
});

export const request = (
  requestId: string,
  nodeId: string,
  nodeName: string,
  patch: Partial<DataSyncAccessRequestView> = {},
): DataSyncAccessRequestView => ({
  requestId,
  direction: DataSyncRequestDirection.Incoming,
  nodeId,
  nodeName,
  intent: DataSyncRequestIntent.TwoWay,
  status: "awaitingApproval",
  expiresAt: minutesAhead(30),
  remoteAddress: "192.168.1.40",
  claimsKnownDevice: false,
  replacesExistingAccess: false,
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
  grantedAt: minutesAgo(60 * 24),
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

// ---- the first sync review ---------------------------------------------------------------------

export const changeCounts = (
  patch: Partial<DataSyncPlanItem["changeCounts"]> = {},
): DataSyncPlanItem["changeCounts"] => {
  const counts = { set: 0, add: 0, rename: 0, recolor: 0, ...patch };

  return { total: counts.set + counts.add + counts.rename + counts.recolor, ...counts, ...patch };
};

export const fieldChange = (
  changeId: string,
  kind: DataSyncFieldChangeKind,
  path: string,
  to: DataSyncFieldChange["to"],
  patch: Partial<DataSyncFieldChange> = {},
): DataSyncFieldChange => ({ changeId, kind, path, to, ...patch });

/** Counts that match a list of changes, the way the planner counts them. */
export const countsOf = (changes: DataSyncFieldChange[]) =>
  changeCounts({
    set: changes.filter((change) => change.kind === DataSyncFieldChangeKind.Set).length,
    add: changes.filter((change) => change.kind === DataSyncFieldChangeKind.AddChild).length,
    rename: changes.filter((change) => change.kind === DataSyncFieldChangeKind.RenameChild).length,
    recolor: changes.filter((change) => change.kind === DataSyncFieldChangeKind.RecolorChild)
      .length,
  });

export const planCandidate = (
  localKey: string,
  name: string,
  patch: Partial<DataSyncPlanCandidate> = {},
): DataSyncPlanCandidate => ({
  localKey,
  name,
  subtype: "SingleChoice",
  match: DataSyncNaturalMatch.Exact,
  changes: [],
  changeCounts: changeCounts(),
  changesTruncated: false,
  warnings: [],
  warningCounts: [],
  warningsTruncated: false,
  unchangedChildren: 0,
  localOnlyChildren: 0,
  recordsNewKeys: true,
  reviewToken: `token-candidate-${localKey}`,
  ...patch,
});

const defaultsOf: Record<
  DataSyncPlanItemType,
  Pick<DataSyncPlanItem, "allowedResolutions" | "defaultResolution" | "requiresConfirmation">
> = {
  [DataSyncPlanItemType.Create]: {
    allowedResolutions: [DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip],
    defaultResolution: DataSyncPlanResolution.Create,
    requiresConfirmation: false,
  },
  [DataSyncPlanItemType.Update]: {
    allowedResolutions: [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip],
    defaultResolution: DataSyncPlanResolution.Update,
    requiresConfirmation: false,
  },
  [DataSyncPlanItemType.Unchanged]: {
    allowedResolutions: [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip],
    defaultResolution: DataSyncPlanResolution.Update,
    requiresConfirmation: false,
  },
  [DataSyncPlanItemType.Link]: {
    allowedResolutions: [
      DataSyncPlanResolution.Link,
      DataSyncPlanResolution.CreateSeparate,
      DataSyncPlanResolution.Skip,
    ],
    defaultResolution: DataSyncPlanResolution.Link,
    requiresConfirmation: true,
  },
  [DataSyncPlanItemType.NeedsDecision]: {
    allowedResolutions: [
      DataSyncPlanResolution.Link,
      DataSyncPlanResolution.CreateSeparate,
      DataSyncPlanResolution.Skip,
    ],
    defaultResolution: undefined,
    requiresConfirmation: true,
  },
  [DataSyncPlanItemType.Held]: {
    allowedResolutions: [],
    defaultResolution: undefined,
    requiresConfirmation: false,
  },
};

/** One item of a plan, with the defaults the planner gives its type (v3.1 §7.3). */
export const planItem = (
  key: string,
  type: DataSyncPlanItemType,
  name: string,
  patch: Partial<DataSyncPlanItem> = {},
): DataSyncPlanItem => {
  const kind = patch.kind ?? "customProperty";
  const local =
    type === DataSyncPlanItemType.Update || type === DataSyncPlanItemType.Unchanged
      ? { localKey: `local-${key}`, name, subtype: "SingleChoice", position: 0, childCount: 3 }
      : undefined;
  const changes = patch.changes ?? [];

  return {
    itemId: `${kind}/k/${key}`,
    kind,
    type,
    incoming: { name, subtype: "SingleChoice", position: 0, childCount: 3 },
    local,
    candidates: [],
    changes,
    changeCounts: countsOf(changes),
    changesTruncated: false,
    unchangedChildren: 0,
    localOnlyChildren: 0,
    ...defaultsOf[type],
    defaultTargetLocalKey:
      type === DataSyncPlanItemType.Link
        ? patch.candidates?.[0]?.localKey
        : (local?.localKey ?? undefined),
    bulkLinkEligible: false,
    offersSeparateName: type === DataSyncPlanItemType.NeedsDecision,
    recordsNewKeys: false,
    reviewToken: `token-${key}`,
    warnings: [],
    warningCounts: [],
    warningsTruncated: false,
    ...patch,
  };
};

/** A plan of the given items, with the summary the planner would give it. */
export const plan = (
  items: DataSyncPlanItem[],
  patch: Partial<DataSyncPlan> = {},
): DataSyncPlan => {
  const kinds = Array.from(new Set(items.map((item) => item.kind)));
  const counts = kinds.flatMap((kind) =>
    Array.from(new Set(items.filter((item) => item.kind === kind).map((item) => item.type))).map(
      (type) => ({
        kind,
        type,
        count: items.filter((item) => item.kind === kind && item.type === type).length,
      }),
    ),
  );

  return {
    planId: "0123456789abcdef",
    snapshotContentHash: "e".repeat(64),
    kinds: kinds.map((kind) => ({
      kind,
      schemaVersion: 1,
      supported: true,
      items: items.filter((item) => item.kind === kind),
      localOnlyCount: 0,
    })),
    summary: {
      counts,
      pendingCount: items.filter((item) => item.requiresConfirmation).length,
      bulkLinkEligibleCount: items.filter((item) => item.bulkLinkEligible).length,
      heldCount: items.filter((item) => item.type === DataSyncPlanItemType.Held).length,
    },
    warnings: [],
    ...patch,
  };
};

export const reviewResult = (
  items: DataSyncPlanItem[],
  patch: Partial<DataSyncReviewResult> = {},
): DataSyncReviewResult => ({
  reviewId: "review-1",
  linkId: 3,
  copyOnce: false,
  linkMode: DataSyncLinkMode.Follow,
  state: DataSyncReviewState.Staged,
  source: {
    nodeId: "node-laptop",
    name: "Laptop",
    appVersion: "2.5.0-beta.10",
    fetchedAt: minutesAgo(12),
    kinds: [{ kind: "customProperty", count: items.length }],
  },
  plan: plan(items),
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
