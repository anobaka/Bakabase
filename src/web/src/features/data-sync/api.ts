import type { components } from "@/sdk/BApi2";
import type { RequestParams } from "@/sdk/Api";

import BApi from "@/sdk/BApi";
import { DataSyncProblemCodeLabel } from "@/sdk/constants";

/*
 * Data sync's calls, over the generated SDK. Every call:
 *
 * - shows nothing by itself (`showErrorToast: false`): the page says what went wrong next to
 *   what failed, in its own words;
 * - answers the record, never the envelope;
 * - throws a {@link DataSyncRequestError} when the request itself failed — refused by the
 *   remote-access gate (`HostOnly`), the server erring, the network — and, for the actions, a
 *   {@link DataSyncProblemError} when the server answered with an expected failure
 *   (`DataSyncProblem`, HTTP 200, spec §10.1).
 *
 * The review, "Needs you" and history reads answer their record whole, problem included: the
 * screens built on them (a fresh plan with `DecisionsInvalid`, a closed item) decide what a
 * problem means there.
 */

type Schemas = components["schemas"];

export type DataSyncOverview = Schemas["Bakabase.Modules.DataSync.Services.DataSyncOverview"];
export type DataSyncStatusView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncStatusView"];
export type DataSyncKindCount = Schemas["Bakabase.Modules.DataSync.Services.DataSyncKindCount"];
export type DataSyncMapView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncMapView"];
export type DataSyncMapPeer = Schemas["Bakabase.Modules.DataSync.Services.DataSyncMapPeer"];
export type DataSyncMapRequest = Schemas["Bakabase.Modules.DataSync.Services.DataSyncMapRequest"];
export type DataSyncMapOutgoing = Schemas["Bakabase.Modules.DataSync.Services.DataSyncMapOutgoing"];
export type DataSyncLinkView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncLinkView"];
export type DataSyncLinkResult = Schemas["Bakabase.Modules.DataSync.Services.DataSyncLinkResult"];
export type DataSyncLinkCreateInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncLinkCreateInput"];
export type DataSyncLinkUpdateInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncLinkUpdateInput"];
export type DataSyncSharingInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncSharingInput"];
export type DataSyncPeerCandidate =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncPeerCandidate"];
export type DataSyncAccessRequestView =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncAccessRequestView"];
export type DataSyncApproveInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncApproveInput"];
export type DataSyncRequestResult =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncRequestResult"];
export type DataSyncReaderView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncReaderView"];
export type DataSyncInvitationView =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncInvitationView"];
export type DataSyncCopyOnceInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncCopyOnceInput"];
export type DataSyncReviewResult =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncReviewResult"];
export type DataSyncReviewApplyInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncReviewApplyInput"];
export type DataSyncApplyStart = Schemas["Bakabase.Modules.DataSync.Services.DataSyncApplyStart"];
export type DataSyncReviewCancelResult =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncReviewCancelResult"];
export type DataSyncChangePage = Schemas["Bakabase.Modules.DataSync.Services.DataSyncChangePage"];
export type DataSyncTaskStart = Schemas["Bakabase.Modules.DataSync.Services.DataSyncTaskStart"];
export type DataSyncProblem = Schemas["Bakabase.Modules.DataSync.Services.DataSyncProblem"];
export type DataSyncSourceAttention =
  Schemas["Bakabase.Modules.DataSync.Wire.DataSyncSourceAttention"];
export type DataSyncEntityStatusView =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncEntityStatusView"];
export type DataSyncEntitySyncInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncEntitySyncInput"];
export type DataSyncInboxPage = Schemas["Bakabase.Modules.DataSync.Services.DataSyncInboxPage"];
export type DataSyncInboxItemView =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncInboxItemView"];
export type DataSyncResolveBatchInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncResolveBatchInput"];
export type DataSyncHistoryEntry =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncHistoryEntry"];
export type DataSyncHistoryDetail =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncHistoryDetail"];
export type DataSyncUndoPreview = Schemas["Bakabase.Modules.DataSync.Services.DataSyncUndoPreview"];
export type DataSyncRestoreView = Schemas["Bakabase.Modules.DataSync.Services.DataSyncRestoreView"];
export type DataSyncReviewSource =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncReviewSource"];
export type DataSyncPlan = Schemas["Bakabase.Modules.DataSync.Planning.DataSyncPlan"];
export type DataSyncPlanItem = Schemas["Bakabase.Modules.DataSync.Planning.DataSyncPlanItem"];
export type DataSyncPlanCandidate =
  Schemas["Bakabase.Modules.DataSync.Planning.DataSyncPlanCandidate"];
export type DataSyncPlanDecision =
  Schemas["Bakabase.Modules.DataSync.Planning.DataSyncPlanDecision"];
export type DataSyncPlanWarning = Schemas["Bakabase.Modules.DataSync.Planning.DataSyncPlanWarning"];
export type DataSyncFieldChange = Schemas["Bakabase.Modules.DataSync.Planning.DataSyncFieldChange"];
export type DataSyncDisplayValue =
  Schemas["Bakabase.Modules.DataSync.Planning.DataSyncDisplayValue"];
export type DataSyncDecisionError =
  Schemas["Bakabase.Modules.DataSync.Planning.DataSyncDecisionError"];
export type DataSyncInboxPayload =
  Schemas["Bakabase.Modules.DataSync.Merging.DataSyncInboxPayload"];
export type DataSyncFieldOutcome =
  Schemas["Bakabase.Modules.DataSync.Abstractions.DataSyncFieldOutcome"];
export type DataSyncTypeChangePreview =
  Schemas["Bakabase.Modules.DataSync.Abstractions.DataSyncTypeChangePreview"];
export type DataSyncResolveInput =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncResolveInput"];
export type DataSyncHistoryItem = Schemas["Bakabase.Modules.DataSync.Services.DataSyncHistoryItem"];
export type DataSyncHistoryCounts =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncHistoryCounts"];
export type DataSyncUndoPreviewItem =
  Schemas["Bakabase.Modules.DataSync.Services.DataSyncUndoPreviewItem"];

/**
 * The inbox query, as `GET /data-sync/inbox` binds it (one query parameter per member). With
 * `kind`, `localKey` answers one definition's items whole, however many others are open.
 */
export type DataSyncInboxQuery = NonNullable<
  Parameters<ReturnType<typeof api>["getDataSyncInbox"]>[0]
>;

/**
 * The request itself failed. `code` is the remote-access gate's reason when it refused
 * (`HostOnly`: this window may not use the page), `Http{status}` for any other HTTP failure,
 * `Server` for an error envelope, and `Network` when nothing answered.
 */
export class DataSyncRequestError extends Error {
  constructor(
    public readonly code: string,
    message: string,
    public readonly status?: number,
  ) {
    super(message);
    this.name = "DataSyncRequestError";
  }
}

/** The server answered with one of data sync's expected failures (spec §2.10). */
export class DataSyncProblemError extends Error {
  constructor(public readonly problem: DataSyncProblem) {
    super(problem.detail || DataSyncProblemCodeLabel[problem.code] || String(problem.code));
    this.name = "DataSyncProblemError";
  }

  /** The problem code's name, e.g. `NotAllowedOnThisDevice`. */
  get label() {
    return DataSyncProblemCodeLabel[this.problem.code];
  }
}

/** The page may not be used from this window: the remote-access gate refused it. */
export const isRefusedHere = (error: unknown) =>
  error instanceof DataSyncRequestError && error.code === "HostOnly";

/** What the SDK throws for an HTTP failure: the response itself. */
const isResponse = (value: unknown): value is Response =>
  typeof value === "object" &&
  value !== null &&
  typeof (value as Response).status === "number" &&
  typeof (value as Response).headers?.get === "function";

const asRequestError = (cause: unknown) => {
  // A DOMException is not an Error everywhere: judged by its name.
  if ((cause as { name?: unknown } | null)?.name === "AbortError") return cause;
  if (isResponse(cause)) {
    const reason = cause.headers.get("X-Bakabase-Remote-Access");
    const body = (cause as Response & { error?: { message?: string } }).error;

    return new DataSyncRequestError(
      reason || `Http${cause.status}`,
      body?.message || cause.statusText || `HTTP ${cause.status}`,
      cause.status,
    );
  }

  return new DataSyncRequestError(
    "Network",
    cause instanceof Error ? cause.message : String(cause),
  );
};

interface Envelope<T> {
  code?: number;
  message?: string | null;
  data?: T | null;
}

const quiet: RequestParams = { showErrorToast: false };

async function call<T>(request: (params: RequestParams) => Promise<Envelope<T>>): Promise<T> {
  let response: Envelope<T>;

  try {
    response = await request(quiet);
  } catch (cause) {
    throw asRequestError(cause);
  }
  if (response?.code) throw new DataSyncRequestError("Server", response.message || "");

  return response?.data as T;
}

/** An action's answer, or its expected failure thrown. */
const solved = <T extends { problem?: DataSyncProblem | null }>(result: T): T => {
  if (result?.problem) throw new DataSyncProblemError(result.problem);

  return result;
};

/**
 * A record answered whole (a task start, a review, an undo preview) whose problem the caller
 * wants thrown after all: said where the action's failures are said.
 */
export const throwIfProblem = solved;

/** An action that answers only a problem, or nothing when it worked. */
const done = (problem: DataSyncProblem | null | undefined) => {
  if (problem) throw new DataSyncProblemError(problem);
};

const api = () => BApi.dataSync;

export const dataSyncApi = {
  overview: () => call((p) => api().getDataSyncOverview(p)),
  map: () => call((p) => api().getDataSyncMap(p)),
  links: () => call((p) => api().getDataSyncLinks(p)).then((links) => links ?? []),
  /** `discover` also asks the network who is nearby, which takes a few seconds. */
  peers: (discover = false) =>
    call((p) => api().getDataSyncPeers({ discover }, p)).then((peers) => peers ?? []),
  requests: () => call((p) => api().getDataSyncRequests(p)).then((requests) => requests ?? []),
  readers: () => call((p) => api().getDataSyncReaders(p)).then((readers) => readers ?? []),
  entities: (kind: string) =>
    call((p) => api().getDataSyncEntities({ kind }, p)).then((entities) => entities ?? []),

  setSharing: async (input: DataSyncSharingInput) =>
    done(await call((p) => api().setDataSyncSharing(input, p))),
  createLink: async (input: DataSyncLinkCreateInput) =>
    solved(await call((p) => api().createDataSyncLink(input, p))),
  updateLink: async (id: number, input: DataSyncLinkUpdateInput) =>
    solved(await call((p) => api().updateDataSyncLink(id, input, p))),
  pauseLink: async (id: number) => solved(await call((p) => api().pauseDataSyncLink(id, p))),
  resumeLink: async (id: number, action: DataSyncLinkResumeAction) =>
    solved(await call((p) => api().resumeDataSyncLink(id, { action }, p))),
  /** Forgets the link's state and keeps every definition; also "Dismiss" on an ended request. */
  resetLink: async (id: number) => done(await call((p) => api().resetDataSyncLink(id, p))),
  /** Always sends a body: the endpoint refuses an empty one. */
  syncNow: async (linkId?: number) =>
    solved(await call((p) => api().syncDataSyncNow(linkId ? { linkId } : {}, p))),
  setAllPaused: async (paused: boolean) =>
    done(await call((p) => api().setDataSyncAllPaused({ paused }, p))),
  /** "Stop reading X": drops this device's own access to that device's definitions. */
  forgetAccess: async (nodeId: string) =>
    done(await call((p) => api().forgetDataSyncAccess(nodeId, p))),
  copyOnce: async (input: DataSyncCopyOnceInput) =>
    solved(await call((p) => api().createDataSyncCopyOnce(input, p))),
  approveRequest: async (id: string, input: DataSyncApproveInput) =>
    solved(await call((p) => api().approveDataSyncRequest(id, input, p))),
  rejectRequest: async (id: string) => done(await call((p) => api().rejectDataSyncRequest(id, p))),
  /** Withdraws a request this device filed. */
  cancelRequest: async (id: string) => done(await call((p) => api().cancelDataSyncRequest(id, p))),
  /** Stops a device from reading this device's definitions. */
  revokeReader: async (nodeId: string) =>
    done(await call((p) => api().revokeDataSyncReader(nodeId, p))),
  createInvitation: async (allowTwoWay: boolean) => {
    const result = solved(await call((p) => api().createDataSyncInvitation({ allowTwoWay }, p)));

    if (!result?.invitation) throw new DataSyncRequestError("Server", "");

    return result.invitation;
  },
  setEntitySync: async (kind: string, localKey: string, input: DataSyncEntitySyncInput) =>
    solved(await call((p) => api().setDataSyncEntitySync(kind, localKey, input, p))),

  // The review, "Needs you", history and restore: answered whole, problem included.
  review: (reviewId: string) => call((p) => api().getDataSyncReview(reviewId, p)),
  refetchReview: (reviewId: string) => call((p) => api().refetchDataSyncReview(reviewId, p)),
  reviewChanges: (
    reviewId: string,
    query: { planId: string; itemId: string; candidate?: string; skip?: number; take?: number },
  ) => call((p) => api().getDataSyncReviewChanges(reviewId, query, p)),
  applyReview: (reviewId: string, input: DataSyncReviewApplyInput) =>
    call((p) => api().applyDataSyncReview(reviewId, input, p)),
  cancelReviewApply: (reviewId: string) =>
    call((p) => api().cancelDataSyncReviewApply(reviewId, p)),
  discardReview: (reviewId: string) =>
    call((p) => api().discardDataSyncReview(reviewId, p) as Promise<Envelope<unknown>>),
  inbox: (query: DataSyncInboxQuery = {}) => call((p) => api().getDataSyncInbox(query, p)),
  inboxItem: (id: number) => call((p) => api().getDataSyncInboxItem(id, p)),
  previewInboxItem: (id: number) => call((p) => api().previewDataSyncInboxItem(id, p)),
  resolve: (input: DataSyncResolveBatchInput) => call((p) => api().resolveDataSyncInbox(input, p)),
  history: () => call((p) => api().getDataSyncHistory(p)).then((entries) => entries ?? []),
  historyEntry: (id: number) => call((p) => api().getDataSyncHistoryEntry(id, p)),
  undoPreview: (id: number) => call((p) => api().previewDataSyncUndo(id, p)),
  undo: (id: number) => call((p) => api().undoDataSync(id, p)),
  restore: () => call((p) => api().getDataSyncRestore(p)),
  chooseRestore: (choice: DataSyncRestoreChoiceValue, linkId?: number) =>
    call((p) => api().chooseDataSyncRestore({ choice, linkId }, p)),
  cancelTask: async (taskId: string) =>
    done(await call((p) => api().cancelDataSyncTask(taskId, p))),
};

type DataSyncLinkResumeAction = Schemas["Bakabase.Modules.DataSync.Services.DataSyncResumeAction"];
type DataSyncRestoreChoiceValue = Schemas["Bakabase.Modules.DataSync.DataSyncRestoreChoice"];
