import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  context: vi.fn(),
  options: vi.fn(),
  saveOptions: vi.fn(),
  batches: vi.fn(),
  activeBatches: vi.fn(),
  preview: vi.fn(),
  create: vi.fn(),
}));

vi.mock("@/components/ResourceMovePanel/api", () => ({
  movePanelApi: api,
  MoveRequestRejected: class MoveRequestRejected extends Error {},
}));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "/api" } }));
import {
  bindMovePayload,
  cancelMoveDraft,
  normalizedMovePath,
  draftMovableIds,
  prepareMove,
  refreshMovePanel,
  selectPanelResourceReservation,
  setMovePanelContext,
  submitMoveDraft,
  updateMovePanelOptions,
  useResourceMovePanelStore,
  visibleMoveDestinations,
} from "../resourceMovePanel";

import { MoveRequestRejected } from "@/components/ResourceMovePanel/api";
const sourceContext = { nodeId: "this-server", libraryEpoch: "this-library" };
const destination = { id: "dest", path: "/target", scope: "global" as const, order: 0 };
const preview = {
  previewFingerprint: "snapshot",
  items: [
    {
      resourceId: 1,
      sourcePath: "/source/a",
      destPath: "/target/a",
      coveredResources: [{ resourceId: 2, path: "/source/a/child", wasSelected: false }],
      effects: [],
    },
  ],
};
const initial = useResourceMovePanelStore.getState();

beforeEach(() => {
  vi.resetAllMocks();
  api.context.mockResolvedValue(sourceContext);
  localStorage.clear();
  useResourceMovePanelStore.setState({ ...initial, sourceContext }, true);
  api.options.mockResolvedValue({ revision: 3, destinations: [destination], autoOverwrite: false });
  api.saveOptions.mockImplementation(async (options) => ({
    ...options,
    revision: options.revision + 1,
  }));
  api.batches.mockResolvedValue([]);
  api.activeBatches.mockResolvedValue([]);
  api.preview.mockResolvedValue(preview);
});
describe("move panel domain state", () => {
  it("preserves absolute Windows drive roots", () => {
    expect(normalizedMovePath("C:\\")).toBe("C:/");
    expect(normalizedMovePath("/")).toBe("/");
    expect(normalizedMovePath("C:\\media\\")).toBe("C:/media");
  });
  it("reserves preview children across tabs and releases only the cancelled draft", async () => {
    await prepareMove(destination, {
      resources: [{ id: 1, path: "/source/a" }],
      sourceTabId: "tab-a",
    });
    expect(selectPanelResourceReservation(2)(useResourceMovePanelStore.getState())?.phase).toBe(
      "preview",
    );
    expect(
      selectPanelResourceReservation(99, "/source/a/deeper")(useResourceMovePanelStore.getState()),
    ).toBeDefined();
    cancelMoveDraft();
    expect(useResourceMovePanelStore.getState().reservations).toEqual([]);
  });
  it("keeps an ambiguous submission locked and retries with the exact same immutable request", async () => {
    api.create
      .mockRejectedValueOnce(new TypeError("Network disconnected"))
      .mockResolvedValueOnce({ batchId: "batch", skippedResourceCount: 0 });
    await prepareMove(destination, {
      resources: [{ id: 1, path: "/source/a" }],
      sourceTabId: "tab-a",
    });
    await submitMoveDraft();
    const unknown = useResourceMovePanelStore.getState().draft;

    expect(unknown?.phase).toBe("unknown");
    expect(unknown?.request?.expectedPreviewFingerprint).toBe("snapshot");
    cancelMoveDraft();
    expect(useResourceMovePanelStore.getState().draft).toBe(unknown);
    setMovePanelContext({ tabId: "tab-b", resources: [{ id: 5 }], selectedResources: [{ id: 5 }] });
    await submitMoveDraft();
    expect(api.create.mock.calls[1][0]).toEqual(api.create.mock.calls[0][0]);
    expect(api.create.mock.calls[1][0].sourceTabId).toBe("tab-a");
    expect(api.create.mock.calls[1][0].resourceRefs).toEqual([{ ...sourceContext, resourceId: 1 }]);
    expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
  });
  it("rejects a drag from another server even when its resource ID exists here", async () => {
    const payload = bindMovePayload({
      resources: [{ id: 1, path: "/source/a" }],
      sourceTabId: "a",
    });

    payload.sourceContext = { nodeId: "other-server", libraryEpoch: "other-library" };
    await prepareMove(destination, payload);
    expect(api.preview).not.toHaveBeenCalled();
    expect(useResourceMovePanelStore.getState().draft?.contextError).toBe("foreignMoveSource");
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toBeUndefined();
    await submitMoveDraft();
    expect(api.create).not.toHaveBeenCalled();
  });
  it("never retries an unknown submission against a new library at the same address", async () => {
    api.create.mockRejectedValueOnce(new TypeError("Disconnected"));
    await prepareMove(destination, { resources: [{ id: 1, path: "/source/a" }] });
    await submitMoveDraft();
    const request = useResourceMovePanelStore.getState().draft?.request;

    api.context.mockResolvedValue({ ...sourceContext, libraryEpoch: "restored-library" });
    await submitMoveDraft();
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(useResourceMovePanelStore.getState().draft).toMatchObject({
      phase: "unknown",
      contextError: "sourceContextChanged",
      request,
    });
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toBeUndefined();
    await refreshMovePanel();
    expect(useResourceMovePanelStore.getState().sourceContextInvalidated).toBe(true);
  });
  it("does not downgrade an older unknown submission without references to a bare ID request", async () => {
    await prepareMove(destination, { resources: [{ id: 1 }] });
    api.create.mockRejectedValueOnce(new TypeError("Disconnected"));
    await submitMoveDraft();
    const draft = useResourceMovePanelStore.getState().draft!;

    useResourceMovePanelStore.setState({
      draft: { ...draft, request: { ...draft.request!, resourceRefs: undefined as any } },
    });
    await submitMoveDraft();
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(useResourceMovePanelStore.getState().draft?.contextError).toBe("sourceContextRequired");
  });
  it("requires a fresh confirmation when the backend detects a changed preview", async () => {
    api.create.mockRejectedValueOnce(
      new MoveRequestRejected("previewChanged: destination changed"),
    );
    await prepareMove(destination, { resources: [{ id: 1, path: "/source/a" }] });
    api.preview.mockResolvedValueOnce({ ...preview, previewFingerprint: "new" });
    await submitMoveDraft();
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(useResourceMovePanelStore.getState().draft?.phase).toBe("ready");
    expect(useResourceMovePanelStore.getState().draft?.preview?.previewFingerprint).toBe("new");
  });
  it("uses authoritative settings revision and refreshes a conflicting write without overwriting it", async () => {
    await refreshMovePanel();
    await updateMovePanelOptions((options) => ({ ...options, autoOverwrite: true }));
    expect(api.saveOptions.mock.calls[0][0].revision).toBe(3);
    expect(useResourceMovePanelStore.getState().options.revision).toBe(4);
    api.saveOptions.mockRejectedValueOnce(new MoveRequestRejected("revision conflict"));
    api.options.mockResolvedValueOnce({
      revision: 7,
      destinations: [destination],
      autoOverwrite: false,
    });
    await expect(
      updateMovePanelOptions((options) => ({ ...options, autoOverwrite: true })),
    ).rejects.toThrow("revision conflict");
    expect(useResourceMovePanelStore.getState().options).toMatchObject({
      revision: 7,
      autoOverwrite: false,
    });
  });
  it("does not release a newly created batch when an older refresh completes without it", async () => {
    await prepareMove(destination, { resources: [{ id: 1, path: "/source/a" }] });
    let resolveActive!: (value: unknown[]) => void;

    api.activeBatches.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveActive = resolve;
      }),
    );
    const staleRefresh = refreshMovePanel();

    api.create.mockResolvedValue({ batchId: "new-batch", skippedResourceCount: 0 });
    const submission = submitMoveDraft();

    await vi.waitFor(() =>
      expect(
        useResourceMovePanelStore.getState().reservations.some((r) => r.batchId === "new-batch"),
      ).toBe(true),
    );
    resolveActive([]);
    await staleRefresh;
    await submission;
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toMatchObject({
      batchId: "new-batch",
      optimistic: true,
    });
    api.batches.mockResolvedValueOnce([
      {
        batchId: "new-batch",
        status: "completed",
        records: [],
        lockedResourceIds: [],
        reservedPaths: [],
      },
    ]);
    await refreshMovePanel();
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toBeUndefined();
  });
  it("explicitly excludes unavailable resources while keeping selected descendants covered by a valid parent", async () => {
    api.preview.mockResolvedValueOnce({
      items: [
        {
          ...preview.items[0],
          coveredResources: [{ resourceId: 2, path: "/source/a/child", wasSelected: true }],
        },
        { resourceId: 3, sourcePath: "/locked", unavailableReason: "busy", coveredResources: [] },
      ],
      skippedResourceIds: [4],
    });
    await prepareMove(destination, { resources: [{ id: 1 }, { id: 2 }, { id: 3 }, { id: 4 }] });
    expect(draftMovableIds(useResourceMovePanelStore.getState().draft!)).toEqual([1, 2]);
  });
  it("keeps explicit right-click resources through background reload, clears on a real selection change", () => {
    const context = { tabId: "a", resources: [{ id: 1 }], selectedResources: [{ id: 1 }] };

    setMovePanelContext(context);
    const explicitPayload = { resources: [{ id: 2 }] };

    useResourceMovePanelStore.setState({ explicitPayload });
    setMovePanelContext({ ...context, resources: [{ id: 1, path: "/new" }] });
    expect(useResourceMovePanelStore.getState().explicitPayload).toEqual(explicitPayload);
    setMovePanelContext({ ...context, selectedResources: [{ id: 3 }] });
    expect(useResourceMovePanelStore.getState().explicitPayload).toBeUndefined();
  });
  it("uses the server's platform exclusions and only reserves the effective move set", async () => {
    api.preview.mockResolvedValue({
      ...preview,
      items: [
        preview.items[0],
        { resourceId: 3, sourcePath: "/steam/game", destPath: "/target/game" },
      ],
      excludedResources: [
        { resourceId: 3, displayName: "Steam game", reasonCode: "steamManaged" },
        { resourceId: 4, displayName: "Remote book", reasonCode: "noLocalFiles" },
      ],
    });
    api.create.mockResolvedValue({ batchId: "mixed", skippedResourceCount: 0 });
    await prepareMove(destination, {
      resources: [{ id: 1, path: "/source/a" }, { id: 3, path: "/steam/game" }, { id: 4 }],
    });
    expect(api.preview).toHaveBeenCalledWith(
      [1, 3, 4],
      "/target",
      [1, 3, 4].map((resourceId) => ({ ...sourceContext, resourceId })),
    );
    expect(draftMovableIds(useResourceMovePanelStore.getState().draft!)).toEqual([1]);
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toBeDefined();
    expect(selectPanelResourceReservation(2)(useResourceMovePanelStore.getState())).toBeDefined();
    expect(
      selectPanelResourceReservation(3, "/steam/game")(useResourceMovePanelStore.getState()),
    ).toBeUndefined();
    expect(selectPanelResourceReservation(4)(useResourceMovePanelStore.getState())).toBeUndefined();
    await submitMoveDraft();
    expect(api.create.mock.calls[0][0]).toMatchObject({
      resourceIds: [1],
      expectedPreviewFingerprint: "snapshot",
    });
  });
  it("excludes selected descendants of a root rejected for containing Steam resources", async () => {
    api.preview.mockResolvedValue({
      items: [
        {
          ...preview.items[0],
          coveredResources: [{ resourceId: 2, path: "/source/a/Steam game", wasSelected: true }],
        },
      ],
      excludedResources: [
        {
          resourceId: 1,
          reasonCode: "containsSteamManagedResource",
          blockingResourceIds: [2],
        },
      ],
    });
    await prepareMove(destination, {
      resources: [
        { id: 2, path: "/source/a/Steam game" },
        { id: 1, path: "/source/a" },
      ],
    });
    expect(draftMovableIds(useResourceMovePanelStore.getState().draft!)).toEqual([]);
    expect(selectPanelResourceReservation(1)(useResourceMovePanelStore.getState())).toBeUndefined();
    expect(selectPanelResourceReservation(2)(useResourceMovePanelStore.getState())).toBeUndefined();
    await submitMoveDraft();
    expect(api.create).not.toHaveBeenCalled();
  });
  it("shows only applicable paths and restores a shadowed local destination after global removal", () => {
    const local = { ...destination, id: "local", scope: "tab" as const, tabId: "a" };
    const options = {
      autoOverwrite: false,
      destinations: [destination, local, { ...local, id: "other", path: "/other", tabId: "b" }],
    };

    expect(visibleMoveDestinations(options, "a").map((d) => d.id)).toEqual(["dest"]);
    expect(
      visibleMoveDestinations(
        {
          ...options,
          destinations: options.destinations.map((d) =>
            d.id === "dest" ? { ...d, isDeleted: true } : d,
          ),
        },
        "a",
      ).map((d) => d.id),
    ).toEqual(["local"]);
  });
});
