import type {
  DataSyncAccessRequestView,
  DataSyncEntityStatusView,
  DataSyncPlanWarning,
} from "../api";

import { describe, expect, it } from "vitest";

import {
  canStartAnyway,
  candidateStatus,
  elsewhereLines,
  entityBadge,
  entityMenu,
  isLive,
  isMutualFollow,
  isPendingRequest,
  lineMode,
  linkEditor,
  linkNotes,
  linkStatus,
  offersAskToKeepInStep,
  orderKinds,
  outgoingRequestIdOf,
  overallStatus,
  pauseDetail,
  peerCardLine,
  pendingRequestsLine,
  readLane,
  receiveLane,
  receivePhrase,
  receiveToggleTarget,
  sharingNeeded,
  stillReadsWhileOff,
  syncIssueOf,
  syncPeerFromLink,
  syncPeerFromMapPeer,
  syncPeerOfRecords,
  syncPeersOf,
  toggleKind,
  twoWayConfirmation,
  waitingElsewhereLine,
  withCandidates,
} from "../viewModels";
import {
  bulkCreateSeparate,
  bulkLinkExact,
  bulkSkipPending,
  closeExclusions,
  footerCounts,
  foldApplies,
  groupByType,
  initialDecisions,
  isPending,
  rebaseDecisions,
  toApplyInput,
  toggleGroup,
} from "../reviewModels";
import {
  conflictBatch,
  conflictDecided,
  filterCards,
  groupInbox,
  inboxBulks,
  inboxChoices,
  recentlyResolved,
} from "../inboxModels";
import { historySources, isUndoable, undoGroups } from "../historyModels";

import {
  candidate,
  changeCounts,
  fieldChange,
  historyEntry,
  inboxItem,
  inboxPayload,
  keyT,
  link,
  mapPeer,
  mapView,
  minutesAgo,
  nameConflict,
  NOW,
  outgoing,
  plan,
  planCandidate,
  planItem,
  reader,
  request,
  status,
} from "./dataSyncFixtures";

import {
  DataSyncEntitySyncState,
  DataSyncFieldChangeKind,
  DataSyncHeldReason,
  DataSyncHistoryKind,
  DataSyncInboxAction,
  DataSyncInboxClosure,
  DataSyncInboxItemType,
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncNaturalMatch,
  DataSyncPauseReason,
  DataSyncPlanItemReason,
  DataSyncPlanItemType,
  DataSyncPlanResolution,
  DataSyncRequestDirection,
  DataSyncStatusLevel,
  DataSyncUndoAction,
  DataSyncUndoBlock,
  DataSyncUndoState,
  DataSyncWarningCode,
  RemoteAccessMode,
} from "@/sdk/constants";

const peerOf = (patch: Parameters<typeof link>[3] = {}) =>
  syncPeerFromLink(link(1, "node-nas", "NAS", patch));
const statusOf = (patch: Parameters<typeof link>[3] = {}) => linkStatus(keyT, peerOf(patch), NOW);

describe("kinds", () => {
  it("lists properties first and keeps kinds this build does not know", () => {
    expect(orderKinds(["extensionGroup", "future", "customProperty"])).toEqual([
      "customProperty",
      "extensionGroup",
      "future",
    ]);
  });

  it("switches a kind, and never the last one off", () => {
    expect(toggleKind(["customProperty", "extensionGroup"], "customProperty")).toEqual([
      "extensionGroup",
    ]);
    expect(toggleKind(["extensionGroup"], "customProperty")).toEqual([
      "customProperty",
      "extensionGroup",
    ]);
    expect(toggleKind(["extensionGroup"], "extensionGroup")).toBeNull();
  });
});

describe("the rule editor", () => {
  it("presses the receive arrow exactly when the mode is on, and names the same mode everywhere", () => {
    for (const mode of [DataSyncLinkMode.Off, DataSyncLinkMode.Follow, DataSyncLinkMode.TwoWay]) {
      const editor = linkEditor(
        peerOf({
          mode,
          lastMode: DataSyncLinkMode.Follow,
          state:
            mode === DataSyncLinkMode.Off ? DataSyncLinkState.Stopped : DataSyncLinkState.Active,
        }),
      );

      expect(editor.receiving).toBe(mode !== DataSyncLinkMode.Off);
      // The badge and the mode buttons say the same; no badge while it is off.
      expect(editor.badge ?? "off").toBe(editor.mode);
    }
  });

  it("toggles the receive arrow between Off and the link's last mode", () => {
    const on = linkEditor(
      peerOf({ mode: DataSyncLinkMode.Follow, lastMode: DataSyncLinkMode.Follow }),
    );

    expect(receiveToggleTarget(on)).toBe("off");
    const off = linkEditor(
      peerOf({
        mode: DataSyncLinkMode.Off,
        lastMode: DataSyncLinkMode.Follow,
        state: DataSyncLinkState.Stopped,
      }),
    );

    expect(receiveToggleTarget(off)).toBe("follow");
  });

  it("turns a new link on both ways", () => {
    const fresh = linkEditor(
      syncPeerFromMapPeer(
        mapPeer("node-new", "New PC", {
          linkId: undefined,
          mode: DataSyncLinkMode.Off,
          lastMode: DataSyncLinkMode.Off,
          state: undefined,
          receiving: false,
          peerMayRead: false,
        }),
      ),
    );

    expect(fresh.receiving).toBe(false);
    expect(receiveToggleTarget(fresh)).toBe("twoWay");
  });

  it("locks the last kind", () => {
    expect(linkEditor(peerOf({ kinds: ["extensionGroup"] })).lockedKind).toBe("extensionGroup");
    expect(linkEditor(peerOf()).lockedKind).toBeUndefined();
  });

  it("reads mutual Follow as both ways on the line, and as Receive only in the editor", () => {
    const peer = peerOf({
      mode: DataSyncLinkMode.Follow,
      lastMode: DataSyncLinkMode.Follow,
      peerModeTowardsUs: "follow",
    });

    expect(isMutualFollow(peer)).toBe(true);
    expect(lineMode(peer)).toBe("twoWay");
    expect(linkEditor(peer).mode).toBe("follow");
    expect(linkEditor(peer).badge).toBe("follow");
    expect(linkEditor(peer).mutualFollow).toBe(true);
  });

  it("words keeping in step both ways the same everywhere, naming only what it turns on", () => {
    const on = { sharingEnabled: true, remoteAccessMode: RemoteAccessMode.Enabled };

    expect(twoWayConfirmation(keyT, "NAS", on)).toEqual({
      title: "dataSync.twoWay.title NAS",
      description: "dataSync.twoWay.consent NAS",
      warning: undefined,
    });
    expect(
      twoWayConfirmation(keyT, "NAS", { ...on, remoteAccessMode: RemoteAccessMode.Disabled })
        .warning,
    ).toBe("dataSync.sharing.remoteAccess");
    expect(twoWayConfirmation(keyT, "NAS", { ...on, sharingEnabled: false }).warning).toBe(
      "dataSync.twoWay.turnsOnSharing",
    );
    const bothOff = { sharingEnabled: false, remoteAccessMode: RemoteAccessMode.Disabled };

    expect(twoWayConfirmation(keyT, "NAS", bothOff).warning).toBe(
      "dataSync.twoWay.turnsOnSharing dataSync.sharing.remoteAccess",
    );
    // Where nothing is turned on — the other device reads this one already — nothing is said.
    expect(twoWayConfirmation(keyT, "NAS", bothOff, false).warning).toBeUndefined();
    expect(sharingNeeded(on)).toBe(false);
    expect(sharingNeeded(bothOff)).toBe(true);
  });

  it("asks a device to keep in step only on a working two-way link it does not read back", () => {
    expect(offersAskToKeepInStep(peerOf({ readBackDeclined: true }))).toBe(true);
    expect(offersAskToKeepInStep(peerOf({ readBackDeclined: false }))).toBe(false);
    expect(
      offersAskToKeepInStep(
        peerOf({
          readBackDeclined: true,
          state: DataSyncLinkState.Paused,
          pausedReason: DataSyncPauseReason.PeerReset,
        }),
      ),
    ).toBe(false);
    expect(
      offersAskToKeepInStep(peerOf({ readBackDeclined: true, mode: DataSyncLinkMode.Follow })),
    ).toBe(false);
  });

  it("offers to stop the other reading only where receiving is off and it still reads", () => {
    const off = { mode: DataSyncLinkMode.Off, state: DataSyncLinkState.Stopped };

    expect(stillReadsWhileOff(peerOf(off))).toBe(true);
    expect(stillReadsWhileOff(peerOf({ ...off, peerMayReadUs: false }))).toBe(false);
    expect(stillReadsWhileOff(peerOf())).toBe(false);
    // An ended request of this device's own is dismissed, not stopped.
    expect(stillReadsWhileOff(peerOf({ ...off, lastErrorCode: "AccessRejected" }))).toBe(false);
  });

  it("offers to start without the other device's review once the runtime says it may", () => {
    const waiting = { state: DataSyncLinkState.WaitingForPeerReview };

    expect(canStartAnyway(peerOf({ ...waiting, startAnywayAt: minutesAgo(1) }), NOW)).toBe(true);
    expect(canStartAnyway(peerOf({ ...waiting, startAnywayAt: minutesAgo(-60) }), NOW)).toBe(false);
    expect(canStartAnyway(peerOf(waiting), NOW)).toBe(false);
    expect(canStartAnyway(peerOf({ startAnywayAt: minutesAgo(1) }), NOW)).toBe(false);
    // The map view says it as the link's full view does.
    const onMap = mapPeer("node-nas", "NAS", {
      state: DataSyncLinkState.WaitingForPeerReview,
      startAnywayAt: minutesAgo(1),
    });

    expect(canStartAnyway(syncPeerFromMapPeer(onMap), NOW)).toBe(true);
  });

  it("takes the skipped, withheld and missing counts and who started the link from the map too", () => {
    const peer = syncPeerFromMapPeer(
      mapPeer("node-nas", "NAS", {
        excludedCount: 3,
        heldCount: 2,
        missingAtPeerCount: 1,
        initiator: DataSyncLinkInitiator.Peer,
      }),
    );

    expect([peer.excludedCount, peer.heldCount, peer.missingAtPeerCount]).toEqual([3, 2, 1]);
    expect(peer.initiator).toBe(DataSyncLinkInitiator.Peer);
    const reader = mapPeer("node-r", "Reader", { linkId: undefined, initiator: undefined });

    expect(syncPeerFromMapPeer(reader).initiator).toBeUndefined();
  });

  it("finds this device's own request to a device, never another's", () => {
    const requests = [
      request("in-1", "node-nas", "NAS"),
      request("out-1", "node-pc", "PC", { direction: DataSyncRequestDirection.Outgoing }),
      request("out-2", "node-nas", "NAS", { direction: DataSyncRequestDirection.Outgoing }),
    ];

    expect(outgoingRequestIdOf(requests, "node-nas", NOW)).toBe("out-2");
    expect(outgoingRequestIdOf(requests, "node-other", NOW)).toBeUndefined();
    expect(outgoingRequestIdOf(undefined, "node-nas", NOW)).toBeUndefined();
  });

  it("takes only a request that still waits: the listing keeps decided and expired ones too", () => {
    const out = (patch: Partial<DataSyncAccessRequestView>) =>
      request("out", "node-nas", "NAS", { direction: DataSyncRequestDirection.Outgoing, ...patch });

    expect(isPendingRequest(out({}), NOW)).toBe(true);
    expect(isPendingRequest(out({ status: "pending" }), NOW)).toBe(true);
    expect(isPendingRequest(out({ status: "granted" }), NOW)).toBe(false);
    expect(isPendingRequest(out({ status: "rejected" }), NOW)).toBe(false);
    expect(isPendingRequest(out({ expiresAt: minutesAgo(1) }), NOW)).toBe(false);
    expect(outgoingRequestIdOf([out({ status: "granted" })], "node-nas", NOW)).toBeUndefined();
  });
});

describe("the lines a link is drawn with", () => {
  it("draws the receive direction working, waiting or not at all", () => {
    expect(receiveLane(peerOf())).toBe("active");
    for (const state of [
      DataSyncLinkState.AwaitingAccess,
      DataSyncLinkState.AwaitingReview,
      DataSyncLinkState.WaitingForPeerReview,
    ])
      expect(receiveLane(peerOf({ state })), String(state)).toBe("pending");
    expect(
      receiveLane(peerOf({ mode: DataSyncLinkMode.Off, state: DataSyncLinkState.Stopped })),
    ).toBe("none");
    // A copy once waiting for access is a pending line although its mode is Off.
    expect(
      receiveLane(peerOf({ mode: DataSyncLinkMode.Off, state: DataSyncLinkState.AwaitingAccess })),
    ).toBe("pending");
  });

  it("says what the waiting receive direction waits for: access, or the first review", () => {
    expect(receivePhrase(keyT, peerOf({ state: DataSyncLinkState.AwaitingAccess }))).toBe(
      "federation.map.direction.sync.in.pending NAS",
    );
    for (const state of [DataSyncLinkState.AwaitingReview, DataSyncLinkState.WaitingForPeerReview])
      expect(receivePhrase(keyT, peerOf({ state })), String(state)).toBe(
        "federation.map.direction.sync.in.review NAS",
      );
    expect(receivePhrase(keyT, peerOf())).toBe("federation.map.direction.sync.in.active NAS");
    expect(
      receivePhrase(keyT, peerOf({ mode: DataSyncLinkMode.Off, state: DataSyncLinkState.Stopped })),
    ).toBe("dataSync.arrow.receive.off NAS");
  });

  it("draws the may-read direction from this device's grant", () => {
    expect(readLane(peerOf())).toBe("active");
    expect(readLane(peerOf({ peerMayReadUs: false }))).toBe("none");
  });

  it("marks what does not work, and never an offline device as failed", () => {
    expect(syncIssueOf(peerOf({ state: DataSyncLinkState.Paused }))).toBe("syncPaused");
    expect(syncIssueOf(peerOf({ state: DataSyncLinkState.PeerTooOld }))).toBe("syncUpdateNeeded");
    expect(syncIssueOf(peerOf({ state: DataSyncLinkState.ThisTooOld }))).toBe("syncUpdateNeeded");
    for (const state of [
      DataSyncLinkState.AccessRevoked,
      DataSyncLinkState.PeerSharingOff,
      DataSyncLinkState.PeerRemoteAccessOff,
    ])
      expect(syncIssueOf(peerOf({ state }))).toBe("syncAccessLost");
    // As the server sends a failure: every failure counts, so the peer is never "online" with one.
    for (const lastErrorCode of ["ApplyFailed", "FetchFailed", "InvalidResponse", "TooLarge"])
      expect(syncIssueOf(peerOf({ peerOnline: false, lastErrorCode })), lastErrorCode).toBe(
        "syncFailed",
      );
    // Away or busy is nothing that does not work: tried again within minutes.
    for (const lastErrorCode of ["Unreachable", "Busy"])
      expect(syncIssueOf(peerOf({ peerOnline: false, lastErrorCode })), lastErrorCode).toBe(
        undefined,
      );
    // After a restart, until the first answer: nothing is wrong.
    expect(syncIssueOf(peerOf({ peerOnline: false }))).toBeUndefined();
    expect(syncIssueOf(peerOf())).toBeUndefined();
  });
});

describe("the status catalogue", () => {
  it("says a link in step, and when it last synced", () => {
    expect(statusOf()).toMatchObject({
      code: "InStep",
      tone: "success",
      text: "dataSync.status.InStep dataSync.time.minutes 5",
    });
  });

  it("says what needs you before saying it is in step", () => {
    expect(statusOf({ openItems: 3 })).toMatchObject({
      code: "NeedsYou",
      text: "dataSync.status.NeedsYou 3",
    });
  });

  it("says an offline device grey, never as a failure", () => {
    expect(statusOf({ peerOnline: false, lastErrorCode: "Unreachable" })).toMatchObject({
      code: "Offline",
      tone: "default",
      text: "dataSync.status.Offline NAS dataSync.time.minutes 5",
    });
  });

  it("says a device that answered busy as syncing, never offline or failed", () => {
    // It answered (or this device was still reading it), and is tried again within minutes.
    expect(statusOf({ lastErrorCode: "Busy" })).toMatchObject({
      code: "Syncing",
      tone: "primary",
      text: "dataSync.status.Syncing",
    });
    // What needs a person still comes first.
    expect(statusOf({ lastErrorCode: "Busy", openItems: 2 })).toMatchObject({ code: "NeedsYou" });
  });

  it("says a failure as one, with its reason, as the server sends it", () => {
    for (const lastErrorCode of ["ApplyFailed", "FetchFailed", "InvalidResponse"])
      expect(statusOf({ peerOnline: false, lastErrorCode }), lastErrorCode).toMatchObject({
        code: "Failed",
        tone: "danger",
        text: `dataSync.status.Failed dataSync.peerError.${lastErrorCode}`,
      });
    // Words of its own, never the exception text the detail carries.
    expect(
      statusOf({ peerOnline: false, lastErrorCode: "ApplyFailed", lastErrorDetail: "disk full" })
        .text,
    ).toBe("dataSync.status.Failed dataSync.peerError.ApplyFailed");
  });

  it("says a link in step after a restart, before its first answer", () => {
    // No head answered in this process yet: `peerOnline` is false with no error.
    expect(statusOf({ peerOnline: false })).toMatchObject({
      code: "InStep",
      text: "dataSync.status.InStep dataSync.time.minutes 5",
    });
  });

  it.each([
    [DataSyncLinkState.AwaitingAccess, "AwaitingAccess"],
    [DataSyncLinkState.AwaitingReview, "AwaitingReview"],
    [DataSyncLinkState.WaitingForPeerReview, "WaitingForPeerReview"],
    [DataSyncLinkState.Stopped, "Stopped"],
    [DataSyncLinkState.PeerTooOld, "PeerTooOld"],
    [DataSyncLinkState.ThisTooOld, "ThisTooOld"],
    [DataSyncLinkState.AccessRevoked, "AccessRevoked"],
    [DataSyncLinkState.PeerSharingOff, "AccessRevoked"],
    [DataSyncLinkState.PeerRemoteAccessOff, "PeerRemoteAccessOff"],
  ])("reads state %s as %s", (state, code) => {
    expect(statusOf({ state }).code).toBe(code);
  });

  it("says why a link is paused", () => {
    const paused = (pausedReason: DataSyncPauseReason, pausedDetail?: string) =>
      statusOf({ state: DataSyncLinkState.Paused, pausedReason, pausedDetail });

    expect(paused(DataSyncPauseReason.ByUser).code).toBe("Paused.ByUser");
    expect(paused(DataSyncPauseReason.AllPaused).code).toBe("Paused.AllPaused");
    expect(paused(DataSyncPauseReason.PeerReset).code).toBe("Paused.PeerReset");
    expect(paused(DataSyncPauseReason.PeerReset, "restored").code).toBe("Paused.PeerResetRestored");
    expect(paused(DataSyncPauseReason.MassDeletion, "deletions=182;kind=customProperty").text).toBe(
      "dataSync.status.paused.MassDeletion NAS 182",
    );
    expect(paused(DataSyncPauseReason.KindEmptied, "kind=extensionGroup").text).toBe(
      "dataSync.status.paused.KindEmptied NAS dataSync.kind.extensionGroup",
    );
    expect(paused(DataSyncPauseReason.LocalRestoreSuspected).code).toBe(
      "Paused.LocalRestoreSuspected",
    );
    expect(paused(DataSyncPauseReason.TooManyDecisions).code).toBe("Paused.TooManyDecisions");
  });

  it("never says in step before the first sync", () => {
    expect(statusOf({ lastSyncedAt: undefined })).toMatchObject({
      code: "Syncing",
      tone: "primary",
      text: "dataSync.status.Syncing",
    });
    expect(peerCardLine(keyT, peerOf({ lastSyncedAt: undefined }), NOW).text).toContain(
      "dataSync.diagram.card.syncing",
    );
    // What needs you, or a failure, is still said first.
    expect(statusOf({ lastSyncedAt: undefined, openItems: 2 }).code).toBe("NeedsYou");
    expect(statusOf({ lastSyncedAt: undefined, lastErrorCode: "InvalidResponse" }).code).toBe(
      "Failed",
    );
  });

  it("says reading the other device back failed, and why — never that it waits for an approval", () => {
    // As the server writes it: the code, and the peer error code that says why as its detail.
    const failed = {
      state: DataSyncLinkState.AwaitingAccess,
      mode: DataSyncLinkMode.TwoWay,
      initiator: DataSyncLinkInitiator.Peer,
      lastErrorCode: "ReadBackFailed",
      lastErrorDetail: "Unreachable",
      peerOnline: false,
    };

    expect(statusOf(failed)).toMatchObject({
      code: "ReadBackFailed",
      tone: "danger",
      text: "dataSync.status.ReadBackFailed NAS dataSync.peerError.Unreachable",
    });
    expect(statusOf({ ...failed, lastErrorDetail: "InvitationInvalid" }).text).toBe(
      "dataSync.status.ReadBackFailed NAS dataSync.peerError.InvitationInvalid",
    );
    // A detail that says nothing known: the general words, never the code the link carries.
    expect(statusOf({ ...failed, lastErrorDetail: undefined }).text).toBe(
      "dataSync.status.ReadBackFailed NAS dataSync.peerError.other ?",
    );
    expect(syncIssueOf(peerOf(failed))).toBe("syncFailed");
    expect(peerOf(failed).outcome).toBeUndefined();
    // Waiting for its own request: the ordinary line.
    expect(statusOf({ ...failed, initiator: DataSyncLinkInitiator.ThisDevice }).code).toBe(
      "AwaitingAccess",
    );
    expect(statusOf({ ...failed, lastErrorCode: undefined }).code).toBe("AwaitingAccess");
    // The map view says who started the link too.
    const onMap = syncPeerOfRecords(
      mapPeer("node-nas", "NAS", {
        state: DataSyncLinkState.AwaitingAccess,
        mode: DataSyncLinkMode.TwoWay,
        initiator: DataSyncLinkInitiator.Peer,
        lastErrorCode: "ReadBackFailed",
        lastErrorDetail: "Unreachable",
      }),
    )!;

    expect(linkStatus(keyT, onMap, NOW)).toMatchObject({
      code: "ReadBackFailed",
      text: "dataSync.status.ReadBackFailed NAS dataSync.peerError.Unreachable",
    });
    const asked = syncPeerOfRecords(
      mapPeer("node-nas", "NAS", {
        state: DataSyncLinkState.AwaitingAccess,
        lastErrorCode: "Unreachable",
      }),
      outgoing(1, "node-nas", "NAS"),
    )!;

    expect(linkStatus(keyT, asked, NOW).code).toBe("AwaitingAccess");
    // Nothing is inferred: a link this device started is waiting for its own request, whatever
    // failed on the way.
    const started = syncPeerOfRecords(
      mapPeer("node-nas", "NAS", {
        state: DataSyncLinkState.AwaitingAccess,
        initiator: DataSyncLinkInitiator.ThisDevice,
        lastErrorCode: "Unreachable",
      }),
    )!;

    expect(linkStatus(keyT, started, NOW).code).not.toBe("ReadBackFailed");
  });

  it("says a full reconciliation runs, after what needs you and never before the first sync", () => {
    expect(statusOf({ fullReconciliationRunning: true })).toMatchObject({
      code: "FullReconciliation",
      tone: "primary",
      text: "dataSync.status.FullReconciliation NAS",
    });
    expect(statusOf({ fullReconciliationRunning: true, openItems: 2 }).code).toBe("NeedsYou");
    expect(statusOf({ fullReconciliationRunning: true, lastSyncedAt: undefined }).code).toBe(
      "Syncing",
    );
    const onMap = syncPeerOfRecords(
      mapPeer("node-nas", "NAS", { fullReconciliationRunning: true }),
    )!;

    expect(linkStatus(keyT, onMap, NOW).code).toBe("FullReconciliation");
  });

  it("says a request that was not approved, until it is dismissed", () => {
    const rejected = syncPeerFromLink(
      link(6, "node-old", "Old laptop", {
        mode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.Stopped,
        lastErrorCode: "AccessRejected",
      }),
    );

    expect(rejected.outcome).toBe("rejected");
    expect(linkStatus(keyT, rejected, NOW).code).toBe("AccessRejected");
  });

  it("adds a read-back that was declined, mutual Follow, and decisions waiting there", () => {
    const notes = (patch: Parameters<typeof link>[3]) =>
      linkNotes(keyT, peerOf(patch), NOW).map((note) => note.code);

    expect(notes({ readBackDeclined: true, peerMayReadUs: false })).toContain("ReadBackDeclined");
    expect(notes({ mode: DataSyncLinkMode.Follow, peerModeTowardsUs: "follow" })).toContain(
      "MutualFollow",
    );
    expect(
      notes({
        peerAttention: {
          headless: true,
          openDecisions: 2,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
    ).toContain("NeedsYouThere");
    expect(notes({ heldCount: 2 })).toContain("Held");
    expect(notes({})).toEqual(["PeerReads"]);
  });

  it("gives every card a short line", () => {
    expect(peerCardLine(keyT, peerOf(), NOW).text).toBe(
      "dataSync.diagram.card.inStep dataSync.time.minutes 5 0",
    );
    expect(peerCardLine(keyT, peerOf({ state: DataSyncLinkState.Paused }), NOW).text).toContain(
      "dataSync.diagram.card.paused",
    );
  });
});

describe("the status of the whole device", () => {
  it("is nothing while data sync is off", () => {
    expect(overallStatus(keyT, status({ level: DataSyncStatusLevel.Off }), NOW)).toBeUndefined();
    expect(overallStatus(keyT, undefined, NOW)).toBeUndefined();
  });

  it.each([
    [DataSyncStatusLevel.InStep, "InStep"],
    [DataSyncStatusLevel.Syncing, "Syncing"],
    [DataSyncStatusLevel.NeedsYou, "NeedsYou"],
    [DataSyncStatusLevel.Paused, "Paused"],
    [DataSyncStatusLevel.Offline, "Offline"],
    [DataSyncStatusLevel.Failed, "Failed"],
    [DataSyncStatusLevel.UpdateNeeded, "UpdateNeeded"],
  ])("reads level %s as %s", (level, code) => {
    expect(overallStatus(keyT, status({ level }), NOW)?.code).toBe(code);
  });

  it("never says in step before anything was synced", () => {
    expect(
      overallStatus(
        keyT,
        status({ level: DataSyncStatusLevel.InStep, lastSyncedAt: undefined }),
        NOW,
      ),
    ).toMatchObject({ code: "Syncing", text: "dataSync.status.Syncing" });
  });

  it("adds the devices holding decisions nobody has taken there", () => {
    expect(waitingElsewhereLine(keyT, status({ peersNeedingDecisions: 2 }))).toBe(
      "dataSync.status.peersNeedingDecisions 2",
    );
    expect(waitingElsewhereLine(keyT, status())).toBeUndefined();
  });

  // Nothing synced yet, and nothing running: never "Syncing…" while it waits for someone.
  const waiting = { links: 1, linksInStep: 0, lastSyncedAt: undefined };

  it("says a link waiting for the other device's approval or first review waits", () => {
    expect(overallStatus(keyT, status({ ...waiting, linksWaiting: 1 }), NOW)).toMatchObject({
      code: "Waiting",
      tone: "primary",
      text: "dataSync.status.level.Waiting 1",
    });
  });

  it("says a first sync ready to review here, before anything else that is fine", () => {
    expect(overallStatus(keyT, status({ ...waiting, linksToReview: 1 }), NOW)).toMatchObject({
      code: "ToReview",
      text: "dataSync.status.level.ToReview 1",
    });
    // Even beside a link that is in step: the review is for the reader to do.
    expect(overallStatus(keyT, status({ links: 2, linksToReview: 1 }), NOW)?.code).toBe("ToReview");
    // A link in step says so, however many others still wait for another device.
    expect(overallStatus(keyT, status({ links: 2, linksWaiting: 1 }), NOW)?.code).toBe("InStep");
  });

  it("says who reads this device, or what waits for an answer, where nothing is linked", () => {
    const none = { links: 0, linksInStep: 0, lastSyncedAt: undefined };

    expect(overallStatus(keyT, status({ ...none, readers: 2 }), NOW)).toMatchObject({
      code: "ReadersOnly",
      text: "dataSync.status.level.ReadersOnly 2",
    });
    const requests = overallStatus(keyT, status({ ...none, pendingRequests: 1 }), NOW);

    expect(requests).toMatchObject({ code: "Requests", text: "dataSync.status.level.Requests 1" });
    // Said once: not again as the line beside it.
    expect(pendingRequestsLine(keyT, status({ ...none, pendingRequests: 1 }), requests)).toBe(
      undefined,
    );
    const inStep = overallStatus(keyT, status({ pendingRequests: 2 }), NOW);

    expect(inStep?.code).toBe("InStep");
    expect(pendingRequestsLine(keyT, status({ pendingRequests: 2 }), inStep)).toBe(
      "dataSync.status.level.Requests 2",
    );
  });
});

describe("every device data sync knows of", () => {
  it("merges links, the map view, own requests and readers by device, once each", () => {
    const peers = syncPeersOf({
      links: [
        link(1, "node-nas", "NAS"),
        link(6, "node-old", "Old laptop", {
          mode: DataSyncLinkMode.Off,
          state: DataSyncLinkState.Stopped,
          lastErrorCode: "AccessRejected",
        }),
      ],
      map: mapView({
        peers: [mapPeer("node-nas", "NAS", { attention: undefined })],
        outgoing: [
          outgoing(6, "node-old", "Old laptop", {
            state: DataSyncLinkState.Stopped,
            outcome: "rejected",
          }),
          outgoing(12, "node-away", "Away PC", {
            state: DataSyncLinkState.Stopped,
            outcome: "expired",
          }),
        ],
      }),
      readers: [reader("node-nas", "NAS"), reader("node-reader", "Reader PC")],
    });

    expect(peers.map((peer) => peer.nodeId)).toEqual([
      "node-away",
      "node-nas",
      "node-old",
      "node-reader",
    ]);
    const away = peers[0];

    // An ended request of this device's own stays until dismissed.
    expect(away).toMatchObject({ outcome: "expired", linkId: 12 });
    expect(peers[2].outcome).toBe("rejected");
    expect(peers[3].peerMayRead).toBe(true);
    expect(peers[3].linkId).toBeUndefined();
    expect(linkStatus(keyT, peers[3], NOW).code).toBe("ReaderOnly");
    expect(peers[1].reader?.nodeId).toBe("node-nas");
  });

  it("takes whether this device reads a device already from the candidates", () => {
    const [peer] = withCandidates(syncPeersOf({ readers: [reader("node-reader", "Reader PC")] }), [
      candidate("node-reader", "Reader PC", { weMayRead: true }),
    ]);

    expect(peer.weMayRead).toBe(true);
  });

  it("says an outgoing link waits for approval", () => {
    expect(peerOf({ state: DataSyncLinkState.AwaitingAccess }).outcome).toBe("awaitingApproval");
    // …but not one the other device started, which waits for its own access.
    expect(
      peerOf({ state: DataSyncLinkState.AwaitingAccess, initiator: DataSyncLinkInitiator.Peer })
        .outcome,
    ).toBeUndefined();
  });

  it("lists the devices holding decisions there", () => {
    const peers = [
      peerOf({
        peerAttention: {
          headless: true,
          openDecisions: 3,
          pausedLinks: 0,
          restorePending: false,
          awaitingReview: 0,
        },
      }),
      syncPeerFromLink(link(2, "node-pc2", "PC-2")),
    ];

    expect(elsewhereLines(keyT, peers)).toEqual([
      expect.objectContaining({ nodeId: "node-nas", count: 3, headless: true }),
    ]);
  });

  it("is live while anything waits on someone", () => {
    expect(isLive({ peers: [peerOf()], pendingRequests: 0 })).toBe(false);
    expect(isLive({ peers: [peerOf()], pendingRequests: 1 })).toBe(true);
    expect(
      isLive({ peers: [peerOf({ state: DataSyncLinkState.AwaitingReview })], pendingRequests: 0 }),
    ).toBe(true);
    expect(isLive({ peers: [], pendingRequests: 0, activeTaskId: "DataSync" })).toBe(true);
  });

  it("reads a pause detail", () => {
    expect(pauseDetail("deletions=182;kind=customProperty")).toEqual({
      deletions: "182",
      kind: "customProperty",
    });
    expect(pauseDetail("restored")).toEqual({ restored: "true" });
    expect(pauseDetail(undefined)).toEqual({});
  });
});

describe("one definition", () => {
  const entity = (patch: Partial<DataSyncEntityStatusView> = {}): DataSyncEntityStatusView => ({
    localKey: "12",
    syncKey: "0123456789abcdef0123456789abcdef",
    state: DataSyncEntitySyncState.Synced,
    childrenLocal: false,
    localOnlyChildren: 0,
    heldChildren: 0,
    lastSyncedAt: minutesAgo(5),
    openItems: 0,
    differsFromSource: false,
    ...patch,
  });

  it("says how it syncs, what needs you first", () => {
    expect(entityBadge(entity({ openItems: 2 })).code).toBe("needsYou");
    expect(entityBadge(entity({ state: DataSyncEntitySyncState.LocalOnly })).code).toBe(
      "localOnly",
    );
    expect(entityBadge(entity({ state: DataSyncEntitySyncState.Detached })).code).toBe("detached");
    // Held back here: said by why — an update helps only a newer version's content.
    expect(entityBadge(entity({ heldAtSource: DataSyncHeldReason.TooLarge })).code).toBe(
      "tooLarge",
    );
    expect(entityBadge(entity({ heldAtSource: DataSyncHeldReason.LocalUnreadable })).code).toBe(
      "unreadable",
    );
    expect(entityBadge(entity({ heldAtSource: DataSyncHeldReason.NewerSchema })).code).toBe(
      "heldAtSource",
    );
    expect(entityBadge(entity({ heldAtSource: DataSyncHeldReason.UnknownKind })).code).toBe(
      "heldAtSource",
    );
    expect(entityBadge(entity({ differsFromSource: true, originName: "NAS" }))).toMatchObject({
      code: "differs",
      values: { name: "NAS" },
    });
    expect(entityBadge(entity({ childrenLocal: true })).code).toBe("definitionOnly");
    expect(entityBadge(entity({ originName: "NAS" })).code).toBe("syncedFrom");
    expect(entityBadge(entity()).code).toBe("synced");
  });

  it("offers what can be chosen for it", () => {
    expect(entityMenu(entity(), true)).toEqual(["keepLocal", "definitionOnlyOn", "detach"]);
    expect(entityMenu(entity({ childrenLocal: true }), true)).toContain("definitionOnlyOff");
    expect(entityMenu(entity(), false)).toEqual(["keepLocal", "detach"]);
    expect(entityMenu(entity({ state: DataSyncEntitySyncState.Detached }), true)).toEqual([
      "rejoin",
    ]);
  });
});

describe("devices to sync with", () => {
  it("says what each can do", () => {
    expect(candidateStatus(candidate("a", "A", { linkId: 3 }), true)).toBe("linked");
    expect(candidateStatus(candidate("a", "A", { contractVersion: undefined }), true)).toBe(
      "tooOld",
    );
    expect(candidateStatus(candidate("a", "A", { contractVersion: 0 }), true)).toBe("tooOld");
    // Known but not seen nearby: nothing says it is too old.
    expect(
      candidateStatus(candidate("a", "A", { discovered: false, contractVersion: undefined }), true),
    ).toBe("asks");
    expect(candidateStatus(candidate("a", "A", { sharesDefinitions: false }), true)).toBe(
      "notSharing",
    );
    expect(candidateStatus(candidate("a", "A", { weMayRead: true }), false)).toBe("readable");
    expect(candidateStatus(candidate("a", "A"), true)).toBe("asks");
    expect(candidateStatus(candidate("a", "A"), false)).toBe("manageElsewhere");
  });
});

// ---- the first sync review (v3.1 models) ----------------------------------------------------------

const { Create, Update, Unchanged, Link, NeedsDecision, Held } = DataSyncPlanItemType;
const R = DataSyncPlanResolution;

/** One of every type, the Link with one exact candidate, the decision with two. */
const mixedPlan = () =>
  plan([
    planItem("create", Create, "Studio"),
    planItem("update", Update, "Genre", {
      changes: [
        fieldChange(
          "name",
          DataSyncFieldChangeKind.Set,
          "name",
          { text: "Genres" },
          { from: { text: "Genre" } },
        ),
        fieldChange("tag:add:t1", DataSyncFieldChangeKind.AddChild, "tags", { text: "Isekai" }),
        fieldChange("tag:add:t2", DataSyncFieldChangeKind.AddChild, "tags", { text: "Mecha" }),
      ],
    }),
    planItem("unchanged", Unchanged, "Mood"),
    planItem("link", Link, "Rating", {
      candidates: [planCandidate("15", "Rating")],
      bulkLinkEligible: true,
    }),
    planItem("decide", NeedsDecision, "Artist", {
      reason: DataSyncPlanItemReason.AmbiguousNameMatch,
      candidates: [
        planCandidate("17", "Artist"),
        planCandidate("18", "artist", { match: DataSyncNaturalMatch.Similar }),
      ],
    }),
    planItem("held", Held, "Huge tags", { heldReason: DataSyncHeldReason.TooLarge }),
  ]);
const idOf = (key: string) => `customProperty/k/${key}`;

describe("the first sync review", () => {
  it("starts every item that needs no confirmation on its default, explicitly", () => {
    const decisions = initialDecisions(mixedPlan());

    expect(decisions[idOf("create")]).toEqual({
      resolution: R.Create,
      targetLocalKey: undefined,
      excludedChangeIds: [],
    });
    expect(decisions[idOf("update")]).toMatchObject({
      resolution: R.Update,
      targetLocalKey: "local-update",
    });
    // An Unchanged item is an Update that records keys.
    expect(decisions[idOf("unchanged")]).toMatchObject({
      resolution: R.Update,
      targetLocalKey: "local-unchanged",
    });
    // Confirmation items — every Link included — stay pending; Held items take no decision.
    expect(decisions[idOf("link")]).toBeUndefined();
    expect(decisions[idOf("decide")]).toBeUndefined();
    expect(decisions[idOf("held")]).toBeUndefined();
    const items = mixedPlan().kinds[0].items;

    expect(items.filter((item) => isPending(item, decisions)).map((item) => item.itemId)).toEqual([
      idOf("link"),
      idOf("decide"),
    ]);
  });

  it("links every exact match at once, then creates or skips what is left", () => {
    const current = mixedPlan();
    const linked = bulkLinkExact(current, initialDecisions(current));

    expect(linked[idOf("link")]).toEqual({
      resolution: R.Link,
      targetLocalKey: "15",
      excludedChangeIds: [],
    });
    expect(linked[idOf("decide")]).toBeUndefined();

    const separate = bulkCreateSeparate(
      current,
      linked,
      (item) => `${item.incoming.name} (Laptop)`,
    );

    expect(separate[idOf("decide")]).toEqual({
      resolution: R.CreateSeparate,
      newName: "Artist (Laptop)",
      excludedChangeIds: [],
    });
    expect(bulkSkipPending(current, linked)[idOf("decide")]).toEqual({
      resolution: R.Skip,
      excludedChangeIds: [],
    });
    // What was decided is left as it is by the pending bulk actions.
    expect(bulkSkipPending(current, linked)[idOf("link")]).toEqual(linked[idOf("link")]);
  });

  it("closes an exclusion over groups and over the changes that depend on it", () => {
    const changes = [
      fieldChange("node:add:a", DataSyncFieldChangeKind.AddChild, "nodes", { path: ["A"] }),
      fieldChange(
        "node:add:b",
        DataSyncFieldChangeKind.AddChild,
        "nodes",
        { path: ["A", "B"] },
        { dependsOnChangeId: "node:add:a" },
      ),
      fieldChange(
        "node:add:c",
        DataSyncFieldChangeKind.AddChild,
        "nodes",
        { path: ["A", "B", "C"] },
        { dependsOnChangeId: "node:add:b" },
      ),
      fieldChange("node:rename:d", DataSyncFieldChangeKind.RenameChild, "nodes", { path: ["D2"] }),
      fieldChange("name", DataSyncFieldChangeKind.Set, "name", { text: "Place" }),
    ];

    expect([...closeExclusions(changes, ["node:add:a"])].sort()).toEqual([
      "node:add:a",
      "node:add:b",
      "node:add:c",
    ]);
    expect([...closeExclusions(changes, ["node:rename:*"])]).toEqual(["node:rename:d"]);
    expect([...closeExclusions(changes, ["name"])]).toEqual(["name"]);
    expect(closeExclusions(changes, []).size).toBe(0);
  });

  it("unticks a group as the group, replacing its single exclusions", () => {
    const decision = {
      resolution: R.Update,
      targetLocalKey: "12",
      excludedChangeIds: ["tag:add:t1", "name"],
    };

    expect(toggleGroup(decision, "tag:add").excludedChangeIds).toEqual(["name", "tag:add:*"]);
    expect(toggleGroup(toggleGroup(decision, "tag:add"), "tag:add").excludedChangeIds).toEqual([
      "name",
    ]);
  });

  it("sends one decision for every item that is not held, with the token of what was shown", () => {
    const current = mixedPlan();
    const decisions = {
      ...bulkLinkExact(current, initialDecisions(current)),
      [idOf("decide")]: { resolution: R.Link, targetLocalKey: "18", excludedChangeIds: [] },
      [idOf("update")]: {
        resolution: R.Update,
        targetLocalKey: "local-update",
        excludedChangeIds: ["tag:add:*"],
      },
    };
    const input = toApplyInput(current, decisions);
    const sent = (key: string) => input.find((decision) => decision.itemId === idOf(key));

    expect(input.map((decision) => decision.itemId)).toEqual([
      idOf("create"),
      idOf("update"),
      idOf("unchanged"),
      idOf("link"),
      idOf("decide"),
    ]);
    expect(sent("create")).toEqual({
      itemId: idOf("create"),
      resolution: R.Create,
      targetLocalKey: undefined,
      newName: undefined,
      excludedChangeIds: [],
      reviewToken: "token-create",
    });
    // Unchanged goes as an Update of its own definition, with the item's token.
    expect(sent("unchanged")).toMatchObject({
      resolution: R.Update,
      targetLocalKey: "local-unchanged",
      reviewToken: "token-unchanged",
    });
    // A candidate target echoes the candidate's token.
    expect(sent("link")?.reviewToken).toBe("token-candidate-15");
    expect(sent("decide")?.reviewToken).toBe("token-candidate-18");
    expect(sent("update")?.excludedChangeIds).toEqual(["tag:add:*"]);
  });

  it("counts what Apply writes, groups and truncated lists included", () => {
    const current = mixedPlan();
    const decisions = bulkLinkExact(current, initialDecisions(current));

    // One pending item still blocks.
    expect(footerCounts(current, decisions)).toMatchObject({
      pending: 1,
      created: 1,
      linked: 1,
      changes: 3,
    });
    const decided = bulkSkipPending(current, decisions);
    const excluded = {
      ...decided,
      [idOf("update")]: { ...decided[idOf("update")], excludedChangeIds: ["tag:add:*"] },
    };

    expect(footerCounts(current, excluded)).toMatchObject({ pending: 0, changes: 1, total: 3 });

    // A truncated list: the group stands for every change of its kind, not only those inline.
    const truncated = plan([
      planItem("big", Update, "Genre", {
        changes: [
          fieldChange("tag:add:t1", DataSyncFieldChangeKind.AddChild, "tags", { text: "A" }),
        ],
        changeCounts: changeCounts({ add: 240, rename: 2 }),
        changesTruncated: true,
      }),
    ]);
    const big = initialDecisions(truncated);

    expect(footerCounts(truncated, big).changes).toBe(242);
    expect(
      footerCounts(truncated, {
        [idOf("big")]: { ...big[idOf("big")], excludedChangeIds: ["tag:add:*"] },
      }).changes,
    ).toBe(2);
  });

  it("has nothing to apply when every item is skipped, held, or unchanged with known keys", () => {
    const quiet = plan([planItem("same", Unchanged, "Mood"), planItem("held", Held, "Huge")]);

    expect(footerCounts(quiet, initialDecisions(quiet))).toMatchObject({
      total: 0,
      nothingToApply: true,
    });

    const newKeys = plan([planItem("same", Unchanged, "Mood", { recordsNewKeys: true })]);

    expect(footerCounts(newKeys, initialDecisions(newKeys))).toMatchObject({
      keyOnly: 1,
      total: 1,
      nothingToApply: false,
    });
    const skipped = plan([planItem("create", Create, "Studio")]);

    expect(
      footerCounts(skipped, { [idOf("create")]: { resolution: R.Skip, excludedChangeIds: [] } })
        .nothingToApply,
    ).toBe(true);
  });

  it("folds an incoming option only in the tick state its warning names", () => {
    const fold = (when?: string): DataSyncPlanWarning => ({
      code: DataSyncWarningCode.OptionLabelConflict,
      changeId: "choice:add:a",
      args: when ? { when, intoLabel: "Action" } : { intoLabel: "Action" },
    });

    expect(foldApplies(fold("always"), false)).toBe(true);
    expect(foldApplies(fold("withIgnoreCaseChange"), true)).toBe(true);
    expect(foldApplies(fold("withIgnoreCaseChange"), false)).toBe(false);
    expect(foldApplies(fold("withoutIgnoreCaseChange"), false)).toBe(true);
    expect(foldApplies(fold("withoutIgnoreCaseChange"), true)).toBe(false);
    expect(foldApplies({ code: DataSyncWarningCode.NodeMoveIgnored }, true)).toBe(false);
  });

  it("keeps decisions across a fresh plan only where their token still matches", () => {
    const before = mixedPlan();
    const decisions = bulkSkipPending(before, bulkLinkExact(before, initialDecisions(before)));
    const after = plan(
      before.kinds[0].items.map((item) =>
        item.itemId === idOf("link")
          ? { ...item, candidates: [planCandidate("15", "Rating", { reviewToken: "changed" })] }
          : item,
      ),
    );
    const rebased = rebaseDecisions(before, after, decisions);

    expect(rebased.dropped).toEqual([idOf("link")]);
    expect(rebased.decisions[idOf("decide")]).toEqual(decisions[idOf("decide")]);
    expect(rebased.decisions[idOf("link")]).toBeUndefined();
  });

  it("lists the types that need the reader first", () => {
    expect(Array.from(groupByType(mixedPlan().kinds[0].items).keys())).toEqual([
      NeedsDecision,
      Link,
      Create,
      Update,
      Held,
      Unchanged,
    ]);
  });
});

// ---- Needs you ----------------------------------------------------------------------------------

const A = DataSyncInboxAction;
const T = DataSyncInboxItemType;

describe("needs you", () => {
  const laptopName = (id: number) =>
    nameConflict(id, { peerNodeId: "node-laptop", peerName: "Laptop" }, "Author");
  const optionRename = inboxItem(
    3,
    T.ChildRenameConflict,
    [A.KeepLocal, A.UseRemote, A.UseCustom, A.Detach],
    { subjectPath: "choice:c-horror" },
  );

  it("puts every conflict of one definition, from every device, on one card", () => {
    const cards = groupInbox([
      nameConflict(1),
      laptopName(2),
      optionRename,
      inboxItem(4, T.DeletedThere, [A.DeleteHere, A.KeepHereOnly], { localKey: "14" }),
    ]);
    const conflict = cards.find((card) => card.key === "conflict:customProperty/12")!;

    expect(conflict.items.map((item) => item.id)).toEqual([3, 1, 2]);
    expect(conflict.peers.map((peer) => peer.name)).toEqual(["NAS", "Laptop"]);
    expect(cards.find((card) => card.key === "item:4")?.items).toHaveLength(1);
  });

  it("keeps a filtered card whole, so its conflicts are still decided together", () => {
    const cards = groupInbox([nameConflict(1), laptopName(2)]);
    const [card] = filterCards(cards, { peer: "node-laptop" });

    expect(card.items).toHaveLength(2);
    expect(filterCards(cards, { kind: "extensionGroup" })).toEqual([]);
  });

  it("sends a conflict card whole: the chosen device's value, and this device's for the rest", () => {
    const [card] = groupInbox([nameConflict(1), laptopName(2), optionRename]);

    expect(conflictDecided(card, { name: { take: "remote", itemId: 2 } })).toBe(false);
    const batch = conflictBatch(
      card,
      {
        name: { take: "remote", itemId: 2 },
        "choice:c-horror": { take: "custom", value: " Horror films " },
      },
      true,
    );

    expect(batch.backupBeforeDestructive).toBe(true);
    expect(batch.items.map((input) => [input.itemId, input.action, input.customValue])).toEqual([
      [3, A.UseCustom, "Horror films"],
      [1, A.KeepLocal, undefined],
      [2, A.UseRemote, undefined],
    ]);
    expect(
      conflictBatch(card, "detach", false).items.every((input) => input.action === A.Detach),
    ).toBe(true);
  });

  it("offers one button per action, candidate and record, and no typed value for a node's parent", () => {
    const suggestion = inboxItem(6, T.LinkSuggestion, [A.Link, A.KeepBoth, A.Skip], {
      payload: inboxPayload({
        candidates: [
          { localKey: "15", name: "Rating", match: DataSyncNaturalMatch.Exact, updatable: true },
          { localKey: "16", name: "rating", match: DataSyncNaturalMatch.Clash, updatable: false },
        ],
      }),
    });

    expect(
      inboxChoices(suggestion).map((choice) => [
        choice.action,
        choice.targetLocalKey,
        choice.input,
      ]),
    ).toEqual([
      [A.Link, "15", undefined],
      [A.KeepBoth, undefined, "newName"],
      [A.Skip, undefined, undefined],
    ]);
    const records = inboxItem(8, T.IdentityConflict, [A.KeepRecordLinked, A.Detach], {
      payload: inboxPayload({
        records: [
          { primaryKey: "k-artist", name: "Artist" },
          { primaryKey: "k-author", name: "Author" },
        ],
      }),
    });

    expect(inboxChoices(records).map((choice) => [choice.action, choice.targetRecordKey])).toEqual([
      [A.KeepRecordLinked, "k-artist"],
      [A.KeepRecordLinked, "k-author"],
      [A.Detach, undefined],
    ]);
    const parent = inboxItem(9, T.ChildRenameConflict, [A.KeepLocal, A.UseRemote, A.UseCustom], {
      subjectPath: "node:n1:parent",
    });

    expect(inboxChoices(parent).map((choice) => choice.action)).toEqual([A.KeepLocal, A.UseRemote]);
    const deleted = inboxItem(4, T.DeletedThere, [A.DeleteHere, A.KeepHereOnly], {
      payload: inboxPayload({ valueCount: 412 }),
    });

    expect(
      inboxChoices(deleted).find((choice) => choice.action === A.DeleteHere)?.destructive,
    ).toBe(true);
  });

  it("builds every bulk action as one whole batch", () => {
    const suggestion = (id: number, match = DataSyncNaturalMatch.Exact) =>
      inboxItem(id, T.LinkSuggestion, [A.Link, A.KeepBoth, A.Skip], {
        localKey: undefined,
        payload: inboxPayload({
          candidates: [{ localKey: `c${id}`, name: "Rating", match, updatable: true }],
        }),
      });
    const deletion = (id: number) =>
      inboxItem(id, T.DeletedThere, [A.DeleteHere, A.KeepHereOnly], {
        localKey: `d${id}`,
        payload: inboxPayload({ valueCount: 3 }),
      });
    const cards = groupInbox([
      suggestion(20),
      suggestion(21),
      suggestion(22, DataSyncNaturalMatch.Similar),
      deletion(30),
      deletion(31),
      nameConflict(1),
      laptopName(2),
      nameConflict(40, { localKey: "13", peerNodeId: "node-laptop", peerName: "Laptop" }),
      nameConflict(41, { localKey: "14" }),
    ]);
    const bulks = inboxBulks(cards, true);
    const of = (id: string, peer?: string) =>
      bulks.find((bulk) => bulk.id === id && bulk.peer?.nodeId === peer)!;

    expect(
      of("linkExact").batch.items.map((input) => [input.itemId, input.targetLocalKey]),
    ).toEqual([
      [20, "c20"],
      [21, "c21"],
    ]);
    expect(of("skipAll").count).toBe(3);
    expect(of("deleteAll")).toMatchObject({ destructive: true, count: 2 });
    expect(of("deleteAll").batch.backupBeforeDestructive).toBe(true);
    expect(of("keepAll").batch.items.every((input) => input.action === A.KeepHereOnly)).toBe(true);
    expect(of("keepLocalAll").count).toBe(3);
    expect(of("keepLocalAll").batch.items.every((input) => input.action === A.KeepLocal)).toBe(
      true,
    );
    // Use the NAS's for all: the Laptop's conflict of the same definition keeps this device's,
    // and a card the NAS has no part in is left out.
    const nas = of("useRemoteAll", "node-nas");

    expect(nas.count).toBe(2);
    expect(nas.batch.items.map((input) => [input.itemId, input.action])).toEqual([
      [1, A.UseRemote],
      [2, A.KeepLocal],
      [41, A.UseRemote],
    ]);
    expect(of("useRemoteAll", "node-laptop").count).toBe(2);
    // One of a kind is decided on its own card.
    expect(inboxBulks(groupInbox([deletion(30)]), true)).toEqual([]);
  });

  it("keeps what was decided in the last seven days, newest first", () => {
    const closed = (id: number, minutes: number) =>
      nameConflict(id, {
        closedAt: minutesAgo(minutes),
        closure: DataSyncInboxClosure.ResolvedElsewhere,
        closedByName: "Laptop",
      });

    expect(
      recentlyResolved(
        [closed(1, 60), closed(2, 5), closed(3, 8 * 24 * 60), nameConflict(4)],
        NOW,
      ).map((item) => item.id),
    ).toEqual([2, 1]);
  });
});

// ---- the history ---------------------------------------------------------------------------------

describe("the history", () => {
  it("undoes every kind but an undo, while it can", () => {
    expect(isUndoable(historyEntry(1, DataSyncHistoryKind.AutoSync))).toBe(true);
    expect(isUndoable(historyEntry(2, DataSyncHistoryKind.Resolution))).toBe(true);
    expect(isUndoable(historyEntry(3, DataSyncHistoryKind.Undo))).toBe(false);
    expect(
      isUndoable(
        historyEntry(4, DataSyncHistoryKind.AutoSync, { undoState: DataSyncUndoState.Expired }),
      ),
    ).toBe(false);
  });

  it("sums the last 30 days per device, leaving out undos and what was undone", () => {
    const sources = historySources(
      [
        historyEntry(1, DataSyncHistoryKind.AutoSync, { appliedAt: minutesAgo(10) }),
        historyEntry(2, DataSyncHistoryKind.FirstLink, {
          appliedAt: minutesAgo(60 * 24 * 3),
          counts: { ...historyEntry(0, DataSyncHistoryKind.FirstLink).counts, linked: 6 },
        }),
        historyEntry(3, DataSyncHistoryKind.AutoSync, {
          peerNodeId: "node-laptop",
          peerName: "Laptop",
          appliedAt: minutesAgo(60 * 24 * 40),
        }),
        historyEntry(4, DataSyncHistoryKind.AutoSync, { undoState: DataSyncUndoState.Undone }),
        historyEntry(5, DataSyncHistoryKind.Undo),
        historyEntry(6, DataSyncHistoryKind.Resolution, {
          peerNodeId: undefined,
          peerName: undefined,
        }),
      ],
      NOW,
    );

    expect(sources).toHaveLength(1);
    expect(sources[0]).toMatchObject({
      nodeId: "node-nas",
      syncs: 2,
      created: 2,
      updated: 4,
      linked: 6,
    });
    expect(sources[0].lastAt).toBe(minutesAgo(10));
  });

  it("groups an undo preview by what happens, blocked rows kept", () => {
    const row = (localKey: string, action: DataSyncUndoAction, blocked?: DataSyncUndoBlock) => ({
      kind: "customProperty",
      localKey,
      name: localKey,
      action,
      blocked,
      settingsMayReferenceIt: false,
      recreatedGetsNewId: false,
    });
    const groups = undoGroups([
      row("a", DataSyncUndoAction.Remove),
      row("b", DataSyncUndoAction.Revert, DataSyncUndoBlock.ChangedSinceImport),
      row("c", DataSyncUndoAction.Recreate),
      row("d", DataSyncUndoAction.RemoveAliases),
    ]);

    expect(Array.from(groups.keys())).toEqual(["remove", "recreate", "unlink", "keep"]);
    expect(groups.get("keep")?.map((item) => item.localKey)).toEqual(["b"]);
  });
});
