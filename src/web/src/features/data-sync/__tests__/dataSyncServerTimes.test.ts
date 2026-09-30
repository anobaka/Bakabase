import { afterAll, describe, expect, it } from "vitest";

import { recentlyResolved } from "../inboxModels";
import { hasPassed, millisecondsSince, minutesLeft, timeAgo } from "../times";
import { isOffline, linkNotes, linkStatus, overallStatus, syncPeerOf } from "../viewModels";

import {
  historyEntry,
  keyT,
  mapPeer,
  mapRequest,
  naked,
  nameConflict,
  NOW,
  ownRequest,
  reader,
  firstSyncPreview,
  status,
} from "./dataSyncFixtures";

import { DataSyncHistoryKind, DataSyncInboxClosure } from "@/sdk/constants";

/*
 * Data sync east of UTC, where most of its users are.
 *
 * `/data-sync` writes every time the way every Newtonsoft endpoint does — UTC digits with
 * nothing saying so. A browser reads that shape as local time, so at UTC+8 a link synced five
 * minutes ago read as eight hours ago, and a request with half an hour to run read as expired.
 * Every time of the overview, the map view's links, requests and readers is read as UTC here.
 *
 * The zone is set for this file only, and checked, so the tests cannot pass by running on a
 * UTC machine.
 */
const zone = process.env.TZ;

process.env.TZ = "Asia/Shanghai";
afterAll(() => {
  if (zone === undefined) delete process.env.TZ;
  else process.env.TZ = zone;
});

const MINUTE = 60_000;

describe("data sync: times the server writes without a zone", () => {
  it("runs east of UTC", () => {
    expect(new Date(2026, 8, 1).getTimezoneOffset()).toBe(-480);
    expect(naked(NOW)).toBe("2026-09-01 08:00:00.000");
  });

  it("reads the overview's last sync as UTC", () => {
    const line = overallStatus(keyT, status({ lastSyncedAt: naked(NOW - 5 * MINUTE) }), NOW);

    expect(line?.text).toBe("dataSync.status.InStep dataSync.time.minutes 5");
  });

  it("reads a link's last sync, and the other device's last read, as UTC", () => {
    const peer = syncPeerOf(
      mapPeer("node-nas", "NAS", {
        lastSyncedAt: naked(NOW - 5 * MINUTE),
        peerLastReadAt: naked(NOW - 6 * MINUTE),
      }),
    );

    expect(linkStatus(keyT, peer, NOW).text).toBe("dataSync.status.InStep dataSync.time.minutes 5");
    expect(linkNotes(keyT, peer, NOW).find((note) => note.code === "PeerReads")?.text).toBe(
      "dataSync.link.peerKeepsInStep NAS dataSync.time.minutes 6",
    );
    expect(millisecondsSince(peer.lastSyncedAt, NOW)).toBe(5 * MINUTE);
  });

  it("reads an offline device's last sync as UTC", () => {
    const peer = syncPeerOf(
      mapPeer("node-nas", "NAS", {
        lastErrorCode: "Unreachable",
        lastSyncedAt: naked(NOW - 2 * 60 * MINUTE),
      }),
    );

    expect(isOffline(peer)).toBe(true);
    expect(linkStatus(keyT, peer, NOW).text).toBe(
      "dataSync.status.Offline NAS dataSync.time.hours 2",
    );
  });

  it("reads when this device's own request runs out as UTC", () => {
    const waiting = syncPeerOf(
      mapPeer("node-away", "Away PC", null, {
        request: ownRequest({ expiresAt: naked(NOW + 10 * MINUTE) }),
      }),
    );

    // Ten minutes to run, not eight hours over.
    expect(hasPassed(waiting.outcomeExpiresAt, NOW)).toBe(false);
    expect(minutesLeft(waiting.outcomeExpiresAt, NOW)).toBe(10);
  });

  it("keeps a live request live, and lets an expired one expire", () => {
    const live = mapRequest("req-in-1", "node-newpc", "New PC", {
      expiresAt: naked(NOW + 30 * MINUTE),
    });
    const over = mapRequest("req-in-2", "node-old", "Old PC", { expiresAt: naked(NOW - MINUTE) });

    expect(minutesLeft(live.expiresAt, NOW)).toBe(30);
    expect(hasPassed(over.expiresAt, NOW)).toBe(true);
  });

  it("reads a reader's last read as UTC", () => {
    const peer = syncPeerOf(
      mapPeer("node-reader", "Reader PC", null, {
        reader: reader("node-reader", "Reader PC", {
          lastReadAt: naked(NOW - 3 * 24 * 60 * MINUTE),
        }),
      }),
    );

    expect(timeAgo(keyT, peer.peerLastReadAt, NOW)).toBe("dataSync.time.days 3");
  });

  it("keeps a decision closed six days and twenty hours ago under Recently resolved", () => {
    const closed = {
      ...nameConflict(1),
      closedAt: naked(NOW - (6 * 24 + 20) * 60 * MINUTE),
      closure: DataSyncInboxClosure.ResolvedElsewhere,
    };
    const over = { ...closed, id: 2, closedAt: naked(NOW - (7 * 24 + 1) * 60 * MINUTE) };

    // Read as local time, the first would be eight hours older: past the week.
    expect(recentlyResolved([closed, over], NOW).map((item) => item.id)).toEqual([1]);
    expect(
      timeAgo(keyT, nameConflict(3, { updatedAt: naked(NOW - 10 * MINUTE) }).updatedAt, NOW),
    ).toBe("dataSync.time.minutes 10");
  });

  it("reads the history's times as UTC", () => {
    expect(
      timeAgo(
        keyT,
        historyEntry(3, DataSyncHistoryKind.Undo, { undoneAt: naked(NOW - 2 * 60 * MINUTE) })
          .undoneAt,
        NOW,
      ),
    ).toBe("dataSync.time.hours 2");
  });

  it("reads when a review was fetched as UTC", () => {
    expect(timeAgo(keyT, firstSyncPreview([]).source?.fetchedAt, NOW)).toBe(
      "dataSync.time.minutes 12",
    );
  });

  it("says never for no time at all, and treats an unreadable deadline as over", () => {
    expect(timeAgo(keyT, undefined, NOW)).toBe("dataSync.time.never");
    expect(hasPassed("not a time", NOW)).toBe(true);
    expect(millisecondsSince("not a time", NOW)).toBeNull();
  });
});
