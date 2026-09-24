import type { FreshInstallFact, NoticeAudience, NoticeDefinition } from "./registry";
import type { RemoteAccessContextState } from "@/stores/remoteAccess";

import { audienceOf } from "./registry";

import { ClientMode, RemoteAccessMode } from "@/sdk/constants";

/** What this install has recorded about its notices (`UIOptions.Notices` on the server). */
export interface NoticeState {
  readIds: string[];
  baselinePending: boolean;
}

/** Tolerates an answer from a server that predates notices, or a malformed one. */
export const toNoticeState = (
  raw?: {
    readIds?: string[] | null;
    baselinePending?: boolean | null;
  } | null,
): NoticeState => ({
  readIds: Array.isArray(raw?.readIds) ? raw.readIds.filter((id) => typeof id === "string") : [],
  baselinePending: raw?.baselinePending === true,
});

interface ViewerFacts {
  initialized: boolean;
  /** Whether the rest is the server's answer or the defaults it starts from. */
  context: RemoteAccessContextState;
  isLocal: boolean;
  clientMode: ClientMode;
  mode: RemoteAccessMode;
}

/**
 * Who is looking at this page, as far as notices are concerned (see {@link NoticeAudience}).
 * `undefined` until the server has said; `null` where no notice is ever shown or recorded.
 */
export const noticeViewerOf = (facts: ViewerFacts): NoticeAudience | null | undefined => {
  // The server could not say. Its defaults read as "this install's own window", which a
  // browser on another device is not: missing the notices once is safe, showing — and
  // recording — them for the wrong viewer is not.
  if (facts.context === "unknown") return null;
  if (facts.context !== "known" || !facts.initialized) return undefined;
  // The console relay or the retired thin client: another server's UI. Told apart from
  // everything else by clientMode alone, which the relay answers at once — there is no
  // need to wait for which of the two it is.
  if (facts.clientMode === ClientMode.PureClient) return null;
  if (facts.isLocal) return "local";
  // A browser on another device records notices only where it may change this install.
  if (facts.mode === RemoteAccessMode.Unrestricted) return "lanAdmin";

  return null;
};

/** What a fresh install is known to have done when it records its baseline. */
export type FreshInstallFacts = Partial<Record<FreshInstallFact, boolean>>;

/** The facts the upgrade-only notices in `registry` ask about. */
export const freshInstallFactsAsked = (registry: NoticeDefinition[]): FreshInstallFact[] => [
  ...new Set(
    registry
      .filter((notice) => notice.upgradeOnly)
      .flatMap((notice) => (notice.showOnFreshInstallWhen ? [notice.showOnFreshInstallWhen] : [])),
  ),
];

/**
 * The fresh install's baseline: the upgrade-only notices it records as read before it ever
 * shows one — all of them, except those whose `showOnFreshInstallWhen` holds. A fact not in
 * `facts` does not hold: an unknown answer keeps the notice upgrade-only.
 */
export const upgradeOnlyNoticeIds = (registry: NoticeDefinition[], facts: FreshInstallFacts = {}) =>
  registry
    .filter((notice) => notice.upgradeOnly)
    .filter((notice) => !(notice.showOnFreshInstallWhen && facts[notice.showOnFreshInstallWhen]))
    .map((notice) => notice.id);

/**
 * The notices to show this viewer now, in reading order: for their audience, not read, and —
 * while a fresh install has not recorded its baseline yet — not upgrade-only. The last rule
 * also covers a baseline that failed to save: the notices it would have marked stay hidden
 * until it succeeds, rather than greeting a fresh install after all.
 */
export const pendingNotices = (
  registry: NoticeDefinition[],
  state: NoticeState,
  viewer: NoticeAudience,
) => {
  const read = new Set(state.readIds);

  return registry
    .filter((notice) => audienceOf(notice).includes(viewer))
    .filter((notice) => !read.has(notice.id))
    .filter((notice) => !(notice.upgradeOnly && state.baselinePending))
    .sort((a, b) => a.order - b.order);
};
