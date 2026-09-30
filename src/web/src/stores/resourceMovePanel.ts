import type {
  CreateMoveRequest,
  MoveBatch,
  MoveDestination,
  MoveDraft,
  MovePanelOptions,
  MovePayload,
  MoveReservation,
  MoveResource,
  MoveSourceContext,
} from "@/components/ResourceMovePanel/types";

import { create } from "zustand";

import envConfig from "@/config/env";
import { movePanelApi, MoveRequestRejected } from "@/components/ResourceMovePanel/api";
import {
  isMoveSourceContext,
  moveSourceContextError,
} from "@/components/ResourceMovePanel/sourceContext";

export const RESOURCE_MOVE_MIME = "application/x-bakabase-resource-move";
export const RESOURCE_MOVE_SUBMITTED_EVENT = "bakabase:resource-move-submitted";
export const RESOURCE_MOVE_UPDATED_EVENT = "bakabase:resource-move-updated";
export type {
  MovePayload,
  MoveResource,
  MoveReservation,
} from "@/components/ResourceMovePanel/types";
const serverKey = encodeURIComponent(
  `${typeof window !== "undefined" ? window.location.origin : ""}${envConfig.apiEndpoint}`,
);
const PREF_KEY = `bakabase:resourceMove:panel:${serverKey}`;
const PENDING_KEY = `bakabase:resourceMove:pending:${serverKey}`;
const read = (key: string) => {
  try {
    return JSON.parse(localStorage.getItem(key) || "null");
  } catch {
    return null;
  }
};
const write = (key: string, value: unknown) => {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    /* Private mode still supports this session. */
  }
};
const pref = read(PREF_KEY);

export const normalizedMovePath = (path: string) => {
  const normalized = path.replace(/\\/g, "/");

  // A drive root must remain C:/, never C: (which is drive-relative on Windows).
  return /^[A-Za-z]:\/$/.test(normalized) ? normalized : normalized.replace(/\/$/, "") || "/";
};
export const movePathsOverlap = (a: string, b: string) => {
  a = normalizedMovePath(a);
  b = normalizedMovePath(b);

  // This is advisory; only the server knows the actual volume's case sensitivity.
  return (
    a === b ||
    a.startsWith(b.endsWith("/") ? b : `${b}/`) ||
    b.startsWith(a.endsWith("/") ? a : `${a}/`)
  );
};
export type MovePanelContext = {
  tabId?: string;
  tabName?: string;
  resources: MoveResource[];
  selectedResources: MoveResource[];
};
type State = {
  sourceContext?: MoveSourceContext;
  sourceContextInvalidated: boolean;
  panelEnabled: boolean;
  open: boolean;
  minimized: boolean;
  mode: "floating" | "docked";
  dragging: boolean;
  context: MovePanelContext;
  explicitPayload?: MovePayload;
  options: MovePanelOptions;
  initialized: boolean;
  loading: boolean;
  error?: string;
  batches: MoveBatch[];
  reservations: MoveReservation[];
  draft?: MoveDraft;
  pendingDrafts: MoveDraft[];
  savingOptions: boolean;
  dockAvailable: boolean;
  geometry: { x: number; y: number; width: number; height: number };
  dockWidth: number;
};
const restored = read(PENDING_KEY) as MoveDraft | MoveDraft[] | null;
const restoredDrafts = (Array.isArray(restored) ? restored : restored ? [restored] : [])
  .filter((d) => d?.request && Array.isArray(d.payload?.resources) && !!d.destination?.path)
  .map((d) => ({ ...d, phase: "unknown" as const, error: undefined }));
const initialDraft = restoredDrafts[0];
const draftReservation = (draft: MoveDraft, sourceContext?: MoveSourceContext): MoveReservation => {
  const contextMatches =
    !moveSourceContextError(draft.payload.sourceContext, sourceContext) &&
    (!draft.contextError || draft.contextError === "sourceContextRequired");
  const ids = !contextMatches
    ? []
    : (draft.request?.resourceIds ??
      (draft.preview ? draftMovableIds(draft) : draft.payload.resources.map((r) => r.id)));
  const selectedIds = new Set(ids);
  const affectedItems =
    draft.preview?.items.filter((item) => selectedIds.has(item.resourceId)) ?? [];

  return {
    key: draft.id,
    phase: draft.phase === "ready" ? "preview" : draft.phase,
    resourceIds: [
      ...new Set([
        ...ids,
        ...affectedItems.flatMap((item) =>
          (item.coveredResources ?? []).map((child) => child.resourceId),
        ),
      ]),
    ],
    paths: draft.payload.resources
      .filter((resource) => selectedIds.has(resource.id))
      .flatMap((resource) => (resource.path ? [resource.path] : [])),
  };
};

export const useResourceMovePanelStore = create<State>(() => ({
  sourceContextInvalidated: false,
  panelEnabled: !!initialDraft,
  open: !!initialDraft,
  minimized: false,
  mode: pref?.mode === "docked" ? "docked" : "floating",
  dragging: false,
  context: { resources: [], selectedResources: [] },
  options: { destinations: [], autoOverwrite: false },
  initialized: false,
  loading: false,
  batches: [],
  reservations: [],
  draft: initialDraft,
  pendingDrafts: restoredDrafts.slice(1),
  savingOptions: false,
  dockAvailable: false,
  geometry: pref?.geometry ?? {
    x: Math.max(16, (typeof window !== "undefined" ? window.innerWidth : 900) - 460),
    y: 90,
    width: 420,
    height: 580,
  },
  dockWidth: pref?.dockWidth ?? 400,
}));
const state = useResourceMovePanelStore.getState;
const set = useResourceMovePanelStore.setState;
const errorText = (e: unknown) => (e instanceof Error ? e.message : "Request failed");
const savePreferences = () => {
  const s = state();

  write(PREF_KEY, { mode: s.mode, geometry: s.geometry, dockWidth: s.dockWidth });
};

export function openMovePanel(payload?: MovePayload) {
  set({
    panelEnabled: true,
    open: true,
    minimized: false,
    explicitPayload: payload?.resources.length ? bindMovePayload(payload) : undefined,
  });
  void refreshMovePanel();
}
export function closeMovePanel() {
  set({ open: false, panelEnabled: false, minimized: false });
}
export function minimizeMovePanel() {
  set({ open: false, panelEnabled: true, minimized: true });
}
export function expandMovePanel() {
  set({ open: true, panelEnabled: true, minimized: false });
}
export function setMovePanelMode(mode: State["mode"]) {
  set({ mode });
  savePreferences();
}
export function setMovePanelGeometry(geometry: State["geometry"]) {
  set({ geometry });
  savePreferences();
}
export function setMovePanelDockWidth(dockWidth: number) {
  set({ dockWidth });
  savePreferences();
}
export function setMovePanelDragging(dragging: boolean) {
  set({ dragging });
}
export function setMovePanelContext(context: MovePanelContext) {
  const previous = state().context;
  const selectionChanged =
    previous.tabId !== context.tabId ||
    previous.selectedResources.map((r) => r.id).join(",") !==
      context.selectedResources.map((r) => r.id).join(",");

  set({ context, ...(selectionChanged ? { explicitPayload: undefined } : {}) });
}
export function clearMovePanelContext(tabId?: string) {
  if (!tabId || state().context.tabId === tabId)
    set({ context: { resources: [], selectedResources: [] } });
}
export const selectPanelResourceReservation = (id: number, path?: string | null) => (s: State) =>
  s.reservations.find(
    (r) => r.resourceIds.includes(id) || (!!path && r.paths.some((p) => movePathsOverlap(p, path))),
  );
export function currentMovePayload(): MovePayload {
  const s = state();

  return (
    s.explicitPayload ??
    bindMovePayload({
      resources: s.context.selectedResources,
      sourceTabId: s.context.tabId,
      sourceTabName: s.context.tabName,
    })
  );
}
/** Capture ownership at the producer, before a payload can leave this window. */
export function bindMovePayload(payload: MovePayload): MovePayload {
  const sourceContext = payload.sourceContext ?? state().sourceContext;

  return {
    ...payload,
    resources: payload.resources.map((resource) => ({ ...resource })),
    sourceContext: sourceContext ? { ...sourceContext } : undefined,
  };
}
function setDraft(draft?: MoveDraft) {
  set((s) => ({
    draft,
    reservations: [
      ...s.reservations.filter((r) => r.phase === "task"),
      ...s.pendingDrafts.map((d) => draftReservation(d, s.sourceContext)),
      ...(draft ? [draftReservation(draft, s.sourceContext)] : []),
    ],
  }));
  write(PENDING_KEY, [...state().pendingDrafts, ...(draft?.request ? [draft] : [])]);
}
let refreshPromise: Promise<void> | undefined;
let batchMutationVersion = 0;

export function refreshMovePanel() {
  if (refreshPromise) return refreshPromise;
  const startedAtVersion = batchMutationVersion;

  refreshPromise = (async () => {
    set({ loading: !state().initialized });
    try {
      const [options, batches, active, sourceContext] = await Promise.all([
        movePanelApi.options(),
        movePanelApi.batches(),
        movePanelApi.activeBatches(),
        movePanelApi.context(),
      ]);

      if (startedAtVersion !== batchMutationVersion) return;
      if (!isMoveSourceContext(sourceContext))
        throw new MoveRequestRejected("sourceContextRequired");
      const identityChanged =
        !!state().sourceContext && !!moveSourceContextError(state().sourceContext, sourceContext);
      const sourceContextInvalidated = state().sourceContextInvalidated || identityChanged;
      const oldBatches = state().batches;
      const updatedIds = new Set<number>();

      for (const batch of batches ?? []) {
        const previous = oldBatches.find((b) => b.batchId === batch.batchId);

        if (
          previous &&
          JSON.stringify(previous.records.map((r) => [r.id, r.status])) !==
            JSON.stringify(batch.records.map((r) => [r.id, r.status]))
        ) {
          batch.records.forEach((r) => updatedIds.add(r.resourceId));
          batch.lockedResourceIds?.forEach((id) => updatedIds.add(id));
          previous.lockedResourceIds?.forEach((id) => updatedIds.add(id));
          batch.resourceIds?.forEach((id) => updatedIds.add(id));
        }
      }
      const { draft, explicitPayload } = state();

      set({
        sourceContext,
        sourceContextInvalidated,
        // Only newly opened in-memory inputs can inherit the first verified context.
        explicitPayload:
          explicitPayload && !explicitPayload.sourceContext && !identityChanged
            ? { ...explicitPayload, sourceContext: { ...sourceContext } }
            : explicitPayload,
        draft: draft
          ? {
              ...draft,
              contextError: draftSourceError(draft, sourceContext, sourceContextInvalidated),
            }
          : undefined,
        options:
          !identityChanged &&
          (state().savingOptions || (options?.revision ?? 0) < (state().options.revision ?? 0))
            ? state().options
            : (options ?? { destinations: [], autoOverwrite: false }),
        batches: [
          ...new Map([...(batches ?? []), ...(active ?? [])].map((b) => [b.batchId, b])).values(),
        ],
        initialized: true,
        error: sourceContextInvalidated ? "sourceContextChanged" : undefined,
        reservations: [
          ...state().reservations.filter(
            (r) =>
              !identityChanged &&
              r.optimistic &&
              ![...(active ?? []), ...(batches ?? [])].some((b) => b.batchId === r.batchId),
          ),
          ...(active ?? [])
            .filter(
              (b) => (b.lockedResourceIds?.length ?? 0) > 0 || (b.reservedPaths?.length ?? 0) > 0,
            )
            .map((b) => ({
              key: b.batchId,
              phase: "task" as const,
              resourceIds: b.lockedResourceIds ?? [],
              paths: b.reservedPaths ?? [],
              batchId: b.batchId,
            })),
          ...state().pendingDrafts.map((d) => draftReservation(d, sourceContext)),
          ...(draft ? [draftReservation(draft, sourceContext)] : []),
        ],
      });
      if (updatedIds.size)
        window.dispatchEvent(
          new CustomEvent(RESOURCE_MOVE_UPDATED_EVENT, {
            detail: { resourceIds: [...updatedIds] },
          }),
        );
    } catch (e) {
      set({ error: errorText(e) });
    } finally {
      set({ loading: false });
    }
  })().finally(() => {
    refreshPromise = undefined;
  });

  return refreshPromise;
}
let optionsQueue = Promise.resolve();

export function updateMovePanelOptions(update: (options: MovePanelOptions) => MovePanelOptions) {
  const expectedContext = state().sourceContext;
  const work = optionsQueue.then(async () => {
    set({ savingOptions: true });
    try {
      const contextError = state().sourceContextInvalidated
        ? "sourceContextChanged"
        : moveSourceContextError(expectedContext, await movePanelApi.context());

      if (contextError) throw new MoveRequestRejected(contextError);
      const next = update(state().options);
      const saved = await movePanelApi.saveOptions(next);

      set({ options: saved ?? next, error: undefined });
    } catch (e) {
      try {
        const latest = await movePanelApi.options();

        set({ options: latest });
      } catch {
        /* Keep the last confirmed snapshot. */
      }
      set({ error: errorText(e) });
      throw e;
    } finally {
      set({ savingOptions: false });
    }
  });

  optionsQueue = work.catch(() => {});

  return work;
}
export function visibleMoveDestinations(options: MovePanelOptions, tabId?: string) {
  const globals = options.destinations.filter((d) => !d.isDeleted && d.scope === "global");
  const globalPaths = new Set(globals.map((d) => normalizedMovePath(d.path)));

  return [
    ...globals,
    ...options.destinations.filter(
      (d) =>
        !d.isDeleted &&
        d.scope === "tab" &&
        !!tabId &&
        d.tabId === tabId &&
        !globalPaths.has(normalizedMovePath(d.path)),
    ),
  ].sort((a, b) => (a.scope === b.scope ? a.order - b.order : a.scope === "global" ? -1 : 1));
}
export async function prepareMove(destination: MoveDestination, payload = currentMovePayload()) {
  if (!payload.resources.length || state().draft) return;
  payload = bindMovePayload(payload);
  const contextError = state().sourceContextInvalidated
    ? "sourceContextChanged"
    : payload.resources.some(
          (resource) => "ref" in resource || "resourceRef" in resource || "nodeId" in resource,
        )
      ? "foreignMoveSource"
      : moveSourceContextError(payload.sourceContext, state().sourceContext);
  const draft: MoveDraft = {
    id: crypto.randomUUID(),
    destination: { ...destination },
    payload: { ...payload, resources: payload.resources.map((r) => ({ ...r })) },
    phase: "preview",
    conflictPolicy: "inherit",
  };

  if (contextError) {
    setDraft({ ...draft, phase: "ready", contextError });

    return;
  }
  const alreadyReserved = payload.resources.some((resource) =>
    selectPanelResourceReservation(resource.id, resource.path)(state()),
  );

  setDraft(draft);
  if (alreadyReserved) {
    setDraft({
      ...draft,
      phase: "ready",
      error: "resourceLocked",
    });

    return;
  }
  try {
    const currentContext = await movePanelApi.context();
    const contextError = moveSourceContextError(payload.sourceContext, currentContext);

    if (contextError) {
      if (state().draft?.id === draft.id) setDraft({ ...draft, phase: "ready", contextError });

      return;
    }
    const preview = await previewDraft(draft);

    if (state().draft?.id === draft.id) setDraft({ ...draft, preview, phase: "ready" });
  } catch (e) {
    if (state().draft?.id === draft.id) setDraft({ ...draft, phase: "ready", error: errorText(e) });
  }
}
/** Hide an unresolved request without releasing its reservation or preventing unrelated work. */
export function hideUnknownMoveDraft() {
  const draft = state().draft;

  if (!draft?.request || draft.phase !== "unknown") return;
  set((s) => ({ pendingDrafts: [...s.pendingDrafts.filter((d) => d.id !== draft.id), draft] }));
  setDraft(undefined);
}
export function resumePendingMoveDraft(id: string) {
  if (state().draft) return;
  const draft = state().pendingDrafts.find((d) => d.id === id);

  if (!draft) return;
  set((s) => ({ pendingDrafts: s.pendingDrafts.filter((d) => d.id !== id) }));
  setDraft({ ...draft, contextError: draftSourceError(draft) });
}
export function cancelMoveDraft() {
  if (state().draft?.request) return;
  setDraft(undefined);
}
export function setMoveDraftPolicy(conflictPolicy: MoveDraft["conflictPolicy"]) {
  const draft = state().draft;

  if (draft && !draft.request) setDraft({ ...draft, conflictPolicy });
}
export function draftMovableIds(draft: MoveDraft) {
  const excluded = new Set([
    ...(draft.preview?.skippedResourceIds ?? []),
    ...(draft.preview?.excludedResources ?? []).map((resource) => resource.resourceId),
  ]);

  for (const item of draft.preview?.items ?? []) {
    if (item.unavailableReason || item.destInsideSource || excluded.has(item.resourceId)) {
      excluded.add(item.resourceId);
      for (const child of item.coveredResources ?? []) excluded.add(child.resourceId);
    }
  }
  const valid = new Set(
    draft.preview?.items
      .filter((i) => !i.unavailableReason && !i.destInsideSource && !excluded.has(i.resourceId))
      .flatMap((i) => [
        i.resourceId,
        ...(i.coveredResources ?? []).filter((c) => c.wasSelected).map((c) => c.resourceId),
      ]) ?? [],
  );

  return draft.payload.resources
    .map((r) => r.id)
    .filter((id) => valid.has(id) && !excluded.has(id));
}
function refsForIds(draft: MoveDraft, ids: number[]) {
  if (!isMoveSourceContext(draft.payload.sourceContext))
    throw new MoveRequestRejected("sourceContextRequired");

  return ids.map((resourceId) => ({ ...draft.payload.sourceContext!, resourceId }));
}
function previewDraft(draft: MoveDraft) {
  const ids = draft.payload.resources.map((resource) => resource.id);

  return movePanelApi.preview(ids, draft.destination.path, refsForIds(draft, ids));
}
function draftSourceError(
  draft: MoveDraft,
  currentContext = state().sourceContext,
  invalidated = state().sourceContextInvalidated,
) {
  const contextError = invalidated
    ? "sourceContextChanged"
    : moveSourceContextError(draft.payload.sourceContext, currentContext);

  if (contextError) return contextError;
  if (draft.request) {
    const refs = draft.request.resourceRefs;

    if (!refs?.length) return "sourceContextRequired";
    if (
      refs.length !== draft.request.resourceIds.length ||
      refs.some((ref) => moveSourceContextError(ref, draft.payload.sourceContext)) ||
      [...refs.map((ref) => ref.resourceId)].sort((a, b) => a - b).join(",") !==
        [...draft.request.resourceIds].sort((a, b) => a - b).join(",")
    )
      return "invalidMoveSourceReferences";
  }

  return undefined;
}
export async function submitMoveDraft() {
  const draft = state().draft;

  if (!draft || draft.phase === "submitting" || draft.phase === "preview") return;
  const contextError = draftSourceError(draft);

  if (contextError) {
    setDraft({ ...draft, contextError });

    return;
  }
  const resourceIds = draftMovableIds(draft);
  const request: CreateMoveRequest = draft.request ?? {
    resourceIds,
    resourceRefs: refsForIds(draft, resourceIds),
    destDir: draft.destination.path,
    origin: "move-panel",
    sourceTabId: draft.payload.sourceTabId,
    sourceTabName: draft.payload.sourceTabName,
    destinationId: draft.destination.id,
    destinationName: draft.destination.name,
    idempotencyKey: draft.id,
    conflictPolicy: draft.conflictPolicy,
    expectedPreviewFingerprint: draft.preview?.previewFingerprint,
  };

  if (!request.resourceIds.length) return;
  setDraft({ ...draft, phase: "submitting", request, error: undefined, contextError: undefined });
  // Validate again before an unknown receipt is retried or an old confirmation is submitted.
  try {
    const currentContext = await movePanelApi.context();
    const contextError = moveSourceContextError(draft.payload.sourceContext, currentContext);

    if (contextError) {
      setDraft({ ...draft, contextError });

      return;
    }
  } catch {
    setDraft({ ...draft, contextError: "sourceContextRequired" });

    return;
  }
  try {
    const created = await movePanelApi.create(request);

    batchMutationVersion++;
    // Preserve an optimistic reservation until the authoritative snapshot contains the task.
    const reservation: MoveReservation = {
      ...draftReservation(draft, state().sourceContext),
      key: created.batchId,
      optimistic: true,
      phase: "task",
      batchId: created.batchId,
    };

    set((s) => ({
      reservations: [...s.reservations.filter((r) => r.key !== draft.id), reservation],
      explicitPayload: undefined,
    }));
    setDraft(undefined);
    window.dispatchEvent(
      new CustomEvent(RESOURCE_MOVE_SUBMITTED_EVENT, {
        detail: { resourceIds: request.resourceIds, sourceTabId: request.sourceTabId },
      }),
    );
    try {
      const batch = await movePanelApi.batch(created.batchId);

      if (batch?.batchId) {
        batchMutationVersion++;
        set((s) => ({
          batches: [batch, ...s.batches.filter((b) => b.batchId !== batch.batchId)],
          reservations: [
            ...s.reservations.filter((r) => r.batchId !== batch.batchId),
            ...(batch.lockedResourceIds?.length || batch.reservedPaths?.length
              ? [
                  {
                    key: batch.batchId,
                    phase: "task" as const,
                    resourceIds: batch.lockedResourceIds ?? [],
                    paths: batch.reservedPaths ?? [],
                    batchId: batch.batchId,
                  },
                ]
              : []),
          ],
        }));
      }
    } catch {
      /* Accepted already: retain the optimistic lock until an authoritative snapshot arrives. */
    }
    await refreshMovePanel();
  } catch (e) {
    if (e instanceof MoveRequestRejected) {
      if (e.message.includes("previewChanged")) {
        setDraft({ ...draft, phase: "preview", request: undefined });
        try {
          const preview = await previewDraft(draft);

          setDraft({ ...draft, preview, phase: "ready", request: undefined, error: undefined });
        } catch (previewError) {
          setDraft({
            ...draft,
            phase: "ready",
            request: undefined,
            error: errorText(previewError),
          });
        }
      } else setDraft({ ...draft, phase: "ready", request: undefined, error: errorText(e) });
    } else setDraft({ ...draft, phase: "unknown", request, error: errorText(e) });
  }
}
