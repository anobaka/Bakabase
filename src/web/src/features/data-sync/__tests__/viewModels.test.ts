import type { DataSyncEntityStatusView } from "../api";

import { describe, expect, it } from "vitest";

import {
  candidateStatus,
  elsewhereLines,
  entityBadge,
  entityMenu,
  isLive,
  isMutualFollow,
  lineMode,
  linkEditor,
  linkNotes,
  linkStatus,
  orderKinds,
  overallStatus,
  pauseDetail,
  peerCardLine,
  readLane,
  receiveLane,
  receiveToggleTarget,
  syncIssueOf,
  syncPeerFromLink,
  syncPeerFromMapPeer,
  syncPeersOf,
  toggleKind,
  waitingElsewhereLine,
  withCandidates,
} from "../viewModels";

import {
  candidate,
  keyT,
  link,
  mapPeer,
  mapView,
  minutesAgo,
  NOW,
  outgoing,
  reader,
  status,
} from "./dataSyncFixtures";

import {
  DataSyncEntitySyncState,
  DataSyncHeldReason,
  DataSyncLinkInitiator,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPauseReason,
  DataSyncStatusLevel,
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
    expect(syncIssueOf(peerOf({ lastErrorCode: "InvalidResponse" }))).toBe("syncFailed");
    expect(syncIssueOf(peerOf({ lastErrorCode: "Unreachable" }))).toBeUndefined();
    expect(syncIssueOf(peerOf({ peerOnline: false, lastErrorCode: "Busy" }))).toBeUndefined();
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
    });
    expect(statusOf({ lastErrorCode: "InvalidResponse" })).toMatchObject({
      code: "Failed",
      tone: "danger",
      text: "dataSync.status.Failed dataSync.peerError.InvalidResponse",
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

  it("adds the devices holding decisions nobody has taken there", () => {
    expect(waitingElsewhereLine(keyT, status({ peersNeedingDecisions: 2 }))).toBe(
      "dataSync.status.peersNeedingDecisions 2",
    );
    expect(waitingElsewhereLine(keyT, status())).toBeUndefined();
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
    expect(entityBadge(entity({ heldAtSource: DataSyncHeldReason.TooLarge })).code).toBe(
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
