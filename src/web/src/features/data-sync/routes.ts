/**
 * Where data sync's page lives, and the query parameters a link to it can carry.
 *
 * Every link into the page — notifications, the status indicator, the properties and
 * extension-groups pages, the help center — is built here, so the page reads back exactly
 * what the others write. The server builds its notification routes by the same rules.
 */
export const DATA_SYNC_ROUTE = "/data-sync";

/** The sections of the page a link can bring forward with `?tab=`. */
export type DataSyncTab = "inbox" | "requests";

/** The details of one link. */
export const dataSyncLinkRoute = (linkId: number) => `${DATA_SYNC_ROUTE}?link=${linkId}`;

/**
 * A link's current first-sync review. It names the link rather than a review, because a
 * review id would dangle after it expired or the server restarted.
 */
export const dataSyncReviewRoute = (linkId: number) => `${dataSyncLinkRoute(linkId)}&review=1`;

/** "Needs you", optionally filtered to the changes from one device. */
export const dataSyncInboxRoute = (peerNodeId?: string) =>
  peerNodeId
    ? `${DATA_SYNC_ROUTE}?tab=inbox&peer=${encodeURIComponent(peerNodeId)}`
    : `${DATA_SYNC_ROUTE}?tab=inbox`;

/** Incoming and outgoing requests. */
export const dataSyncRequestsRoute = `${DATA_SYNC_ROUTE}?tab=requests`;

/** The "Sync with another device" wizard. */
export const dataSyncAddRoute = `${DATA_SYNC_ROUTE}?add=1`;

/** The restore panel. */
export const dataSyncRestoreRoute = `${DATA_SYNC_ROUTE}?restore=1`;

/** What a link into the page asked for; everything absent or unreadable is left out. */
export interface DataSyncQuery {
  linkId?: number;
  /** `review=1` next to a link: that link's current review. */
  review: boolean;
  /** A stale `?review={id}` from an older link, still accepted. */
  reviewId?: string;
  tab?: DataSyncTab;
  /** With `tab=inbox`: the device whose changes to show. */
  peer?: string;
  add: boolean;
  restore: boolean;
}

const isTab = (value: string | null): value is DataSyncTab =>
  value === "inbox" || value === "requests";

export function readDataSyncQuery(params: URLSearchParams): DataSyncQuery {
  // Only a plain decimal id: Number() alone would also read `0x10`, `1e2` or ` 7 ` as
  // another link's id. Anything else reads as NaN, which no link has.
  const link = params.get("link");
  const linkId = link && /^[1-9]\d*$/.test(link) ? Number(link) : NaN;
  const review = params.get("review");
  const tab = params.get("tab");
  const peer = params.get("peer");

  return {
    linkId: Number.isSafeInteger(linkId) && linkId > 0 ? linkId : undefined,
    review: review === "1",
    reviewId: review && review !== "1" ? review : undefined,
    tab: isTab(tab) ? tab : undefined,
    peer: tab === "inbox" && peer ? peer : undefined,
    add: params.get("add") === "1",
    restore: params.get("restore") === "1",
  };
}
