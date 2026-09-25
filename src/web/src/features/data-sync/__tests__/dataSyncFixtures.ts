import type { TFunction } from "i18next";
import type {
  DataSyncAccessRequestView,
  DataSyncLinkView,
  DataSyncMapOutgoing,
  DataSyncMapPeer,
  DataSyncMapView,
  DataSyncOverview,
  DataSyncPeerCandidate,
  DataSyncReaderView,
  DataSyncStatusView,
} from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";

import { vi } from "vitest";

import {
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncRequestDirection,
  DataSyncRequestIntent,
  DataSyncStatusLevel,
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
  status: "pending",
  expiresAt: minutesAhead(30),
  remoteAddress: "192.168.1.40",
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
  state: "inStep",
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
