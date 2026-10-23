import { beforeEach, describe, expect, it, vi } from "vitest";

import BApi from "@/sdk/BApi";
import i18next from "i18next";
import { ContentType } from "@/sdk/Api";
import {
  CustomPropertyAdditionalItem,
  CollectionMemberFilter,
  PropertyPool,
  type ResourceAdditionalItem,
} from "@/sdk/constants";
import {
  createResourceApi,
  managedResourceCoverUrl,
  managedResourceAssetUrl,
  subscribeResourceOwnerChanges,
} from "../resourceApi";
import type { ManagementSource } from "../api";

const { request, notice } = vi.hoisted(() => ({ request: vi.fn(), notice: vi.fn() }));
vi.mock("react-hot-toast", () => ({ default: Object.assign(notice, { error: vi.fn() }) }));

vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");

  return { default: Object.assign(new Api({ baseUrl: "http://manager" }), { request }) };
});
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://manager" } }));

const source = (serverId: string): ManagementSource => ({
  serverId,
  name: serverId,
  libraryEpoch: `epoch-${serverId}`,
  isLocal: false,
  available: true,
  contractVersion: 1,
});

beforeEach(() => {
  vi.spyOn(i18next, "t").mockImplementation(((key: string) => key) as any);
  notice.mockReset();
  request.mockReset().mockResolvedValue({ code: 0, data: [] });
});

describe("original resource UI owner-scoped SDK", () => {
  it("forwards bulk library deletion and its path-mark cleanup option to the captured owner", async () => {
    const api = createResourceApi(source("owner"));

    await api.mediaLibraryV2.deleteMediaLibrariesV2({ ids: [1, 2], removeRelatedPathMarks: true });
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/federation/local/management/owner/request",
        body: {
          libraryEpoch: "epoch-owner",
          operation: "libraryBulkDelete",
          body: { ids: [1, 2], removeRelatedPathMarks: true },
        },
      }),
    );
  });
  it("forwards the generated custom-property ValueCount query unchanged for both catalog and ID reads", async () => {
    const api = createResourceApi(source("owner"));
    await api.customProperty.getAllCustomProperties({
      additionalItems: CustomPropertyAdditionalItem.ValueCount,
    });
    await api.customProperty.getCustomPropertyByKeys({
      ids: [7],
      additionalItems: CustomPropertyAdditionalItem.ValueCount,
    });
    expect(CustomPropertyAdditionalItem.ValueCount).toBe(2);
    expect(request.mock.calls.map(([r]) => r.body)).toEqual([
      {
        libraryEpoch: "epoch-owner",
        operation: "customProperties",
        query: { additionalItems: ["2"] },
      },
      {
        libraryEpoch: "epoch-owner",
        operation: "getCustomPropertyByKeys",
        ids: [7],
        query: { additionalItems: ["2"] },
      },
    ]);
  });
  it("keeps definition and collection editors on the captured owner, including same numeric IDs", async () => {
    const a = createResourceApi(source("a")),
      b = createResourceApi(source("b"));
    await a.extensionGroup.putExtensionGroup(7, { name: "Video", extensions: [".mkv"] });
    await b.customProperty.sortCustomProperties({ ids: [7, 8] });
    await a.collection.getCollection(7);
    await b.collection.putCollection(7, { name: "Series", autoAcquire: false, order: 0 });
    await a.collection.searchCollectionMembers(7, { pageIndex: 2, pageSize: 40, filter: 0 });
    await b.collection.setCollectionMemberIgnored(7, 8, { ignored: true });
    await a.subscription.patchSubscription(7, { enabled: false });
    expect(request.mock.calls.map(([r]) => [r.path, r.body.operation, r.body.id])).toEqual([
      ["/federation/local/management/a/request", "extensionGroupPut", 7],
      ["/federation/local/management/b/request", "sortCustomProperties", undefined],
      ["/federation/local/management/a/request", "getCollection", 7],
      ["/federation/local/management/b/request", "putCollection", 7],
      ["/federation/local/management/a/request", "searchCollectionMembers", 7],
      ["/federation/local/management/b/request", "setCollectionMemberIgnored", 7],
      ["/federation/local/management/a/request", "patchSubscription", 7],
    ]);
    expect(request.mock.calls[5][0].body.query).toEqual({ ignored: ["true"], resourceId: ["8"] });
  });
  it("retains native collection filter and page-member query values rather than treating them as resource flags", async () => {
    const api = createResourceApi(source("owner"));
    await api.collection.searchCollectionMembers(7, {
      filter: CollectionMemberFilter.GoneFromSource,
      pageIndex: 2,
      pageSize: 40,
    });
    await api.collection.getCollectionMemberships(7, { resourceIds: [8, 9] });
    expect(request.mock.calls.map(([r]) => r.body)).toEqual([
      {
        libraryEpoch: "epoch-owner",
        operation: "searchCollectionMembers",
        id: 7,
        query: { filter: ["5"], pageIndex: ["2"], pageSize: ["40"] },
      },
      { libraryEpoch: "epoch-owner", operation: "getCollectionMemberships", id: 7, ids: [8, 9] },
    ]);
  });
  it("reads cached extension groups offline while refusing edits", async () => {
    const owner = source("offline");
    owner.available = false;
    owner.metadata = {
      identity: { serverId: owner.serverId, libraryEpoch: owner.libraryEpoch },
      name: owner.name,
      denied: false,
      data: {
        activeGeneration: "complete",
        activeSequence: 1,
        resourceCount: 0,
        stagedResourceCount: 0,
      },
      sync: { paused: false, isOffline: true },
    };
    const api = createResourceApi(owner);
    await api.extensionGroup.getAllExtensionGroups();
    await expect(
      api.extensionGroup.putExtensionGroup(7, { name: "Video", extensions: [] }),
    ).rejects.toThrow();
    expect(request).toHaveBeenCalledOnce();
    expect(request.mock.calls[0][0].body.operation).toBe("extensionGroups");
  });
  it("reports a saved mutation awaiting fresh metadata without retrying the mutation", async () => {
    request.mockResolvedValueOnce({ code: 0, metadataPending: true });
    const owner = source("b");
    const changed = vi.fn();
    const unsubscribe = subscribeResourceOwnerChanges(owner, changed);
    const api = createResourceApi(owner);

    await api.resource.pinResource(7, { pin: true });
    expect(request).toHaveBeenCalledTimes(1);
    expect(notice).toHaveBeenCalledWith("management.metadata.savedRefreshing", {
      id: "metadata-refresh-b",
    });
    expect(changed).toHaveBeenCalledWith([7]);
    request.mockRejectedValueOnce(new Error("MetadataRefreshPending"));
    await expect(api.resource.getResourcesByKeys({ ids: [7] })).rejects.toThrow(
      "MetadataRefreshPending",
    );
    expect(request.mock.calls.map(([input]) => input.body.operation)).toEqual([
      "resourcePin",
      "resourceGet",
    ]);
    unsubscribe();
  });
  it("routes offline replica reads through the same owner endpoint without allowing writes or file access", async () => {
    const owner = source("b");
    owner.available = false;
    owner.metadata = {
      identity: { serverId: owner.serverId, libraryEpoch: owner.libraryEpoch },
      name: owner.name,
      denied: false,
      data: {
        activeGeneration: "complete",
        resourceCount: 1,
        activeSequence: 1,
        stagedResourceCount: 0,
      },
      sync: { paused: false, isOffline: true },
    };
    const api = createResourceApi(owner);
    await api.resource.searchResources({ page: 1, pageSize: 20 });
    await api.property.getPropertiesByPool(PropertyPool.All);
    expect(request.mock.calls.map(([input]) => input.body.operation)).toEqual([
      "resourceSearch",
      "properties",
    ]);
    await expect(api.resource.pinResource(7, { pin: true })).rejects.toThrow("unavailable");
    await expect(api.resource.getResourcePlayableItems(7)).rejects.toThrow("unavailable");
    expect(request).toHaveBeenCalledTimes(2);
  });
  it("launches a native batch on the manager while binding all resource IDs to one immutable owner", async () => {
    const owner = source("b");
    const api = createResourceApi(owner);
    owner.libraryEpoch = "changed";
    await api.player.getBatchPlayCandidates({ resourceIds: [7, 8] });
    await api.player.batchPlayResources({
      resourceIds: [7, 8],
      playerKey: "manager-potplayer",
      fileSelectionMode: 2,
    });
    expect(request.mock.calls.map(([input]) => ({ path: input.path, body: input.body }))).toEqual([
      {
        path: "/federation/local/management/b/batch-play/candidates",
        body: { libraryEpoch: "epoch-b", resourceIds: [7, 8] },
      },
      {
        path: "/federation/local/management/b/batch-play",
        body: {
          libraryEpoch: "epoch-b",
          resourceIds: [7, 8],
          playerKey: "manager-potplayer",
          fileSelectionMode: 2,
        },
      },
    ]);
  });

  it("does not split an oversized native batch into multiple independent launches", async () => {
    await expect(
      createResourceApi(source("b")).player.batchPlayResources({
        resourceIds: Array.from({ length: 101 }, (_, i) => i + 1),
        playerKey: "manager-potplayer",
        fileSelectionMode: 2,
      }),
    ).rejects.toThrow("between 1 and 100");
    expect(request).not.toHaveBeenCalled();
  });

  it("preserves playlist item order and repeated resources in a native launch", async () => {
    await createResourceApi(source("b")).player.batchPlayResources({
      resourceIds: [8, 7, 8],
      playerKey: "manager-potplayer",
      fileSelectionMode: 2,
    });
    expect(request).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        body: expect.objectContaining({ resourceIds: [8, 7, 8] }),
      }),
    );
  });

  it("keeps playlist reads and writes on the playlist owner", async () => {
    const api = createResourceApi(source("b"));
    await api.playlist.getAllPlaylists();
    await api.playlist.getPlaylist(7);
    await api.playlist.putPlaylist(7, { name: "Updated", items: [] } as any);
    await api.playlist.deletePlaylist(7);
    expect(request.mock.calls.map(([input]) => input.body.operation)).toEqual([
      "getAllPlaylists",
      "getPlaylist",
      "putPlaylist",
      "deletePlaylist",
    ]);
    expect(
      request.mock.calls.every(
        ([input]) => input.path === "/federation/local/management/b/request",
      ),
    ).toBe(true);
  });
  it("leaves the normal all-in-one SDK exactly unchanged", () => {
    expect(createResourceApi()).toBe(BApi);
    expect(createResourceApi({ ...source("local"), isLocal: true })).toBe(BApi);
    expect(BApi.baseUrl).toBe("http://manager");
  });

  it("routes simultaneous identical resource IDs to their immutable owners", async () => {
    const a = source("a");
    const aApi = createResourceApi(a);
    const bApi = createResourceApi(source("b"));

    a.serverId = "wrong";
    a.libraryEpoch = "changed";
    await Promise.all([
      aApi.resource.pinResource(7, { pin: true }),
      bApi.resource.pinResource(7, { pin: false }),
    ]);
    expect(request.mock.calls.map(([input]) => [input.path, input.body])).toEqual([
      [
        "/federation/local/management/a/request",
        {
          libraryEpoch: "epoch-a",
          operation: "resourcePin",
          id: 7,
          query: { pin: ["true"] },
        },
      ],
      [
        "/federation/local/management/b/request",
        {
          libraryEpoch: "epoch-b",
          operation: "resourcePin",
          id: 7,
          query: { pin: ["false"] },
        },
      ],
    ]);
  });

  it("preserves SDK search fields and converts scalar/array query values", async () => {
    const api = createResourceApi(source("b"));
    const search = { page: 2, pageSize: 50, keyword: "album" };

    await api.resource.searchResources(search, {
      saveSearch: false,
      additionalItems: 3 as ResourceAdditionalItem,
    });
    await api.resource.getResourcesByKeys({
      ids: [7, 8],
      additionalItems: 3 as ResourceAdditionalItem,
    });
    expect(request.mock.calls[0][0].body).toEqual({
      libraryEpoch: "epoch-b",
      operation: "resourceSearch",
      body: search,
      query: { saveSearch: ["false"], additionalItems: ["3"] },
    });
    expect(request.mock.calls[1][0].body).toEqual({
      libraryEpoch: "epoch-b",
      operation: "resourceGet",
      ids: [7, 8],
      query: { additionalItems: ["3"] },
    });
  });

  it("preserves owner property pools and collection IDs independently of this device", async () => {
    const api = createResourceApi(source("b"));

    await api.property.getPropertiesByPool(PropertyPool.Custom);
    await api.resource.getCollectionIdsByResourceIds({ resourceIds: [7, 8] });
    expect(request.mock.calls[0][0].body).toMatchObject({
      operation: "properties",
      query: { pool: [String(PropertyPool.Custom)] },
    });
    expect(request.mock.calls[1][0].body).toEqual({
      libraryEpoch: "epoch-b",
      operation: "getCollectionIdsByResourceIds",
      ids: [7, 8],
    });
  });

  it("preserves DELETE payloads for owner collection membership", async () => {
    await createResourceApi(source("b")).collection.removeCollectionMembers(3, {
      resourceIds: [7],
    });
    expect(request.mock.calls[0][0].body).toMatchObject({
      operation: "removeCollectionMembers",
      id: 3,
      body: { resourceIds: [7] },
    });
  });

  it("launches native playback on the manager, never the resource owner's play endpoint", async () => {
    await createResourceApi(source("b")).resource.playResourceItem(7, {
      origin: 2,
      key: "/nas/movie.mkv",
      explicitChoice: true,
    });
    expect(request.mock.calls[0][0]).toMatchObject({
      path: "/federation/local/management/b/play",
      method: "POST",
      body: {
        libraryEpoch: "epoch-b",
        resourceId: 7,
        origin: 2,
        key: "/nas/movie.mkv",
        explicitChoice: true,
      },
    });
  });

  it("requires a specific resource before browsing its owner files", async () => {
    await expect(
      createResourceApi(source("b")).file.getChildrenIwFsInfo({ root: "/nas" }),
    ).rejects.toThrow("unbound resource files");
    expect(request).not.toHaveBeenCalled();
    await createResourceApi(source("b"), 7).file.getChildrenIwFsInfo({ root: "/nas/album" });
    expect(request.mock.calls[0][0].body).toMatchObject({
      operation: "getChildrenIwFsInfo",
      id: 7,
      query: { root: ["/nas/album"] },
    });
  });

  it("keeps display preferences local but owner filters source-bound", async () => {
    const api = createResourceApi(source("b"));

    await api.options.patchUiOptions({ hideResourceCovers: true });
    await api.options.getRecentResourceFilters();
    expect(request.mock.calls[0][0]).toMatchObject({ path: "/options/ui", method: "PATCH" });
    expect(request.mock.calls[1][0].body.operation).toBe("getRecentResourceFilters");
  });

  it("fails closed on unavailable owners, unknown endpoints, URL builders and multipart files", async () => {
    await expect(
      createResourceApi({ ...source("b"), available: false }).resource.pinResource(7, {
        pin: true,
      }),
    ).rejects.toThrow("unavailable");
    const api = createResourceApi(source("b"));

    await expect(api.request({ path: "/app/restart", method: "POST" })).rejects.toThrow(
      "not available",
    );
    expect(() => api.file.getRawFileUrl({ fullname: "/secret" })).toThrow("not available");
    await expect(
      api.request({
        path: "/resource/7/acquisition-leads/torrent",
        method: "POST",
        type: ContentType.FormData,
        body: {},
      }),
    ).rejects.toThrow("multipart");
    expect(request).not.toHaveBeenCalled();
  });

  it("carries request cancellation without retargeting the singleton", async () => {
    const controller = new AbortController();

    await createResourceApi(source("b")).resource.getResourcesByKeys(
      { ids: [7] },
      { signal: controller.signal },
    );
    expect(request.mock.calls[0][0].signal).toBe(controller.signal);
    expect(BApi.baseUrl).toBe("http://manager");
  });

  it("builds cover URLs from an owner triple and index without forwarding raw paths", () => {
    const result = new URL(managedResourceCoverUrl(source("b"), 7, 2, 300, 400));

    expect(result.pathname).toBe("/federation/local/management/b/cover");
    expect(Object.fromEntries(result.searchParams)).toEqual({
      libraryEpoch: "epoch-b",
      resourceId: "7",
      index: "2",
      w: "300",
      h: "400",
    });
  });

  it("sends attachment and torrent uploads only to their captured resource owner", async () => {
    const api = createResourceApi(source("b"), 7);

    await api.file.uploadFile({ file: new File(["image"], "cover.png") });
    await api.resource.addResourceAcquisitionTorrent(8, {
      file: new File(["torrent"], "download.torrent"),
    });
    expect(request.mock.calls.map(([call]) => [call.path, call.body])).toEqual([
      [
        "/federation/local/management/b/upload",
        {
          libraryEpoch: "epoch-b",
          operation: "uploadFile",
          id: 7,
          body: { fileName: "cover.png", base64: btoa("image") },
        },
      ],
      [
        "/federation/local/management/b/upload",
        {
          libraryEpoch: "epoch-b",
          operation: "addResourceAcquisitionTorrent",
          id: 8,
          body: { fileName: "download.torrent", base64: btoa("torrent") },
        },
      ],
    ]);
  });

  it("rejects oversized uploads before sending or reading the body", async () => {
    const file = new File([new Uint8Array(4 * 1024 * 1024 + 1)], "large.torrent");

    await expect(
      createResourceApi(source("b")).resource.addResourceAcquisitionTorrent(7, { file }),
    ).rejects.toThrow("4 MiB");
    expect(request).not.toHaveBeenCalled();
  });

  it("routes folder requests to the manager's mapped desktop handler", async () => {
    await createResourceApi(source("b")).resource.openResourceDirectory({ id: 7 });
    expect(request.mock.calls[0][0]).toMatchObject({
      path: "/federation/local/management/b/directory",
      body: { libraryEpoch: "epoch-b", resourceId: 7 },
    });
  });

  it("publishes successful mutations only to the same owner and library generation", async () => {
    const a = vi.fn();
    const b = vi.fn();
    const oldB = vi.fn();
    const stop = [
      subscribeResourceOwnerChanges(source("a"), a),
      subscribeResourceOwnerChanges(source("b"), b),
      subscribeResourceOwnerChanges({ ...source("b"), libraryEpoch: "old" }, oldB),
    ];

    try {
      await createResourceApi(source("b")).resource.pinResource(7, { pin: true });
      expect(b).toHaveBeenCalledWith([7]);
      expect(a).not.toHaveBeenCalled();
      expect(oldB).not.toHaveBeenCalled();
      request.mockResolvedValueOnce({ code: 409, message: "epoch changed" });
      await createResourceApi(source("b")).resource.pinResource(7, { pin: false });
      expect(b).toHaveBeenCalledTimes(1);
    } finally {
      stop.forEach((unsubscribe) => unsubscribe());
    }
  });

  it("keeps asset paths owner-bound for owner-side membership validation", () => {
    const url = new URL(managedResourceAssetUrl(source("b"), 7, "/nas/cover.png", true));

    expect(url.pathname).toBe("/federation/local/management/b/asset");
    expect(url.searchParams.get("key")).toBe("/nas/cover.png");
    expect(url.searchParams.get("libraryEpoch")).toBe("epoch-b");
    expect(url.searchParams.get("resourceId")).toBe("7");
  });

  it("loads more than 100 selected resources in bounded owner batches", async () => {
    const ids = Array.from({ length: 205 }, (_, index) => index + 1);

    request.mockImplementation(async ({ body }) => ({
      code: 0,
      data: body.ids.map((id: number) => ({ id })),
    }));
    const result = await createResourceApi(source("b")).resource.getResourcesByKeys({ ids });

    expect(request.mock.calls.map(([call]) => call.body.ids.length)).toEqual([100, 100, 5]);
    expect(result.data?.map((resource) => resource.id)).toEqual(ids);
  });

  it("merges owner-local dictionary responses without losing mappings across read batches", async () => {
    const ids = Array.from({ length: 105 }, (_, index) => index + 1);

    request.mockImplementation(async ({ body }) => ({
      code: 0,
      data: Object.fromEntries(body.ids.map((id: number) => [id, [9]])),
    }));
    const result = await createResourceApi(source("b")).resource.getCollectionIdsByResourceIds({
      resourceIds: ids,
    });

    expect(request.mock.calls.map(([call]) => call.body.ids.length)).toEqual([100, 5]);
    expect(Object.keys(result.data ?? {})).toHaveLength(105);
    expect(result.data?.[105]).toEqual([9]);
  });

  it("stops a bulk write on the first failure and reports confirmed progress without retrying", async () => {
    const ids = Array.from({ length: 250 }, (_, index) => index + 1);
    const changed = vi.fn();
    const unsubscribe = subscribeResourceOwnerChanges(source("b"), changed);

    request
      .mockResolvedValueOnce({ code: 0 })
      .mockRejectedValueOnce(new Error("Device went offline"));
    try {
      await expect(
        createResourceApi(source("b")).resource.bulkPutResourcePropertyValue({
          resourceIds: ids,
          propertyId: 7,
          isCustomProperty: true,
          isBizValue: true,
          value: "value",
        }),
      ).rejects.toMatchObject({
        name: "ManagedResourceBatchError",
        confirmedCount: 100,
        totalCount: 250,
      });
      expect(request).toHaveBeenCalledTimes(2);
      expect(changed).toHaveBeenCalledExactlyOnceWith(ids.slice(0, 100));
    } finally {
      unsubscribe();
    }
  });
});
