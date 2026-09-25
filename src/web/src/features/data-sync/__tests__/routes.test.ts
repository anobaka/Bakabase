import { describe, expect, it } from "vitest";

import {
  DATA_SYNC_ROUTE,
  dataSyncAddRoute,
  dataSyncInboxRoute,
  dataSyncLinkRoute,
  dataSyncRequestsRoute,
  dataSyncRestoreRoute,
  dataSyncReviewRoute,
  readDataSyncQuery,
} from "../routes";

/** What the page reads from a route built for it. */
const read = (route: string) => {
  const [path, query = ""] = route.split("?");

  expect(path).toBe(DATA_SYNC_ROUTE);

  return readDataSyncQuery(new URLSearchParams(query));
};

const nothing = { review: false, add: false, restore: false };

describe("data sync routes", () => {
  it("reads back what every link into the page asks for", () => {
    expect(read(DATA_SYNC_ROUTE)).toEqual(nothing);
    expect(read(dataSyncLinkRoute(7))).toEqual({ ...nothing, linkId: 7 });
    expect(read(dataSyncReviewRoute(7))).toEqual({ ...nothing, linkId: 7, review: true });
    expect(read(dataSyncInboxRoute())).toEqual({ ...nothing, tab: "inbox" });
    expect(read(dataSyncInboxRoute("node a&b"))).toEqual({
      ...nothing,
      tab: "inbox",
      peer: "node a&b",
    });
    expect(read(dataSyncRequestsRoute)).toEqual({ ...nothing, tab: "requests" });
    expect(read(dataSyncAddRoute)).toEqual({ ...nothing, add: true });
    expect(read(dataSyncRestoreRoute)).toEqual({ ...nothing, restore: true });
  });

  it("still accepts a review named by its id", () => {
    expect(read(`${DATA_SYNC_ROUTE}?review=abc123`)).toEqual({ ...nothing, reviewId: "abc123" });
  });

  it("leaves out what it cannot read", () => {
    for (const query of [
      "link=",
      "link=0",
      "link=-3",
      "link=1.5",
      "link=x",
      "tab=history",
      "peer=n1",
      "add=true",
      "restore=yes",
    ]) {
      expect(readDataSyncQuery(new URLSearchParams(query)), query).toEqual(nothing);
    }
    // A device filter belongs to "Needs you" only.
    expect(readDataSyncQuery(new URLSearchParams("tab=requests&peer=n1"))).toEqual({
      ...nothing,
      tab: "requests",
    });
  });
});
