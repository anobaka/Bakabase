import { useCallback, useEffect, useRef, useState } from "react";
import type { Resource } from "@/core/models/Resource";
import type { SearchForm } from "@/pages/resource/models";
import { ResourceAdditionalItem } from "@/sdk/constants";
import { createResourceApi, subscribeResourceOwnerChanges } from "./resourceApi";
import BApi from "@/sdk/BApi";
import { useResourceOwner } from "./resourceContext";
import { useResourceSources } from "./resourceSourcesContext";
import { ManagedResourcePager } from "./resourceSearch";
import { resourceSelectionKey, type ResourceSelectionKey } from "./resourceIdentity";
import type { ManagementSource } from "./api";
import type { SearchOptions } from "@/hooks/useResourceSearch";
import { compileResourceQuery, resolveQuerySources } from "./resourceQuery";
import { sourceSearchRevision } from "./metadata";

const metadata = (ResourceAdditionalItem.Cover |
  ResourceAdditionalItem.Properties |
  ResourceAdditionalItem.Alias |
  ResourceAdditionalItem.DisplayName |
  ResourceAdditionalItem.MediaLibraryName |
  ResourceAdditionalItem.HasChildren |
  ResourceAdditionalItem.CollectionName) as ResourceAdditionalItem;

export function useManagedResourceSearch() {
  const context = useResourceSources();
  const owner = useResourceOwner();
  const enabled = !!owner || !!context?.sources.some((source) => !source.isLocal);
  const [resources, setResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(false);
  const [response, setResponse] = useState<{
    pageIndex: number;
    pageSize: number;
    totalCount: number;
  }>();
  const [sourceErrors, setSourceErrors] = useState<{ source: ManagementSource; error: Error }[]>(
    [],
  );
  const current = useRef(resources);
  current.current = resources;
  const active = useRef<{
    signature: string;
    pager: ManagedResourcePager;
    controller: AbortController;
    page: number;
  }>();
  const selectionRequest = useRef<AbortController>();
  const cancelResourceSelection = useCallback(() => selectionRequest.current?.abort(), []);
  const revision = useRef(0);
  const refreshRevisions = useRef(new Map<ResourceSelectionKey, number>());
  const nextRefreshRevision = useRef(0);
  const saveQueue = useRef(Promise.resolve());
  useEffect(
    () => () => {
      revision.current++;
      active.current?.controller.abort();
    },
    [],
  );

  const createPager = useCallback(
    (
      sources: ManagementSource[],
      form: SearchForm,
      controller: AbortController,
      additionalItems = metadata,
    ) =>
      new ManagedResourcePager(
        sources,
        form,
        async (source, page, signal) => {
          const compiled = compileResourceQuery(page, source);
          if (!compiled) return { data: [], totalCount: 0 };
          const result = await createResourceApi(source).resource.searchResources(
            compiled,
            { additionalItems, saveSearch: false },
            { signal },
          );
          if (result.code) throw new Error(result.message || String(result.code));
          return { data: (result.data ?? []) as Resource[], totalCount: result.totalCount ?? 0 };
        },
        controller.signal,
      ),
    [],
  );

  const search = useCallback(
    async (form: SearchForm, options?: SearchOptions) => {
      if ((options?.mode ?? "replace") === "replace") cancelResourceSelection();
      form = owner ? { ...form, owner } : form;
      const sources = resolveQuerySources(form, context?.sources ?? [], owner);
      const signature = JSON.stringify([
        sources.map(sourceSearchRevision),
        { ...form, page: undefined },
      ]);
      const mode = options?.mode ?? "replace";
      if (
        !active.current ||
        signature !== active.current.signature ||
        (mode === "replace" && active.current.page === form.page)
      ) {
        active.current?.controller.abort();
        const controller = new AbortController();
        active.current = {
          signature,
          controller,
          pager: createPager(sources, form, controller),
          page: form.page,
        };
      }
      active.current.page = form.page;
      const request = active.current;
      const attempt = ++revision.current;
      refreshRevisions.current.clear();
      setLoading(true);
      let reported = false;
      try {
        const result = await request.pager.page(form.page || 1);
        if (attempt !== revision.current || request.controller.signal.aborted) return [];
        reported = true;
        options?.onResult?.(result.totalCount, request.pager.failures.size ? "failed" : "success");
        const items = options?.filter ? options.filter(result.data) : result.data;
        setResources((previous) =>
          mode === "replace"
            ? items
            : mode === "append"
              ? [...previous, ...items]
              : [...items, ...previous],
        );
        setResponse({
          pageIndex: form.page || 1,
          pageSize: form.pageSize,
          totalCount: result.totalCount,
        });
        setSourceErrors(
          sources.flatMap((source) => {
            const error = request.pager.failures.get(source.serverId);
            return error ? [{ source, error }] : [];
          }),
        );
        if (options?.saveSearch) {
          // Serialize tab saves so an earlier, slower request cannot replace the latest criteria.
          saveQueue.current = saveQueue.current
            .catch(() => {})
            .then(async () => {
              const saved = await BApi.resource.putSavedSearchCriteria(form, {
                id: options.searchId,
              });
              if (saved.code) throw new Error(saved.message || String(saved.code));
            });
          await saveQueue.current;
        }
        return items;
      } catch (error) {
        if (request.controller.signal.aborted || attempt !== revision.current) return [];
        if (!reported) options?.onResult?.(0, "failed");
        throw error;
      } finally {
        if (attempt === revision.current) setLoading(false);
      }
    },
    [context?.sources, owner, createPager, cancelResourceSelection],
  );

  useEffect(() => () => selectionRequest.current?.abort(), []);
  const searchAllResourceKeys = useCallback(
    async (form: SearchForm): Promise<ResourceSelectionKey[]> => {
      form = owner ? { ...form, owner } : form;
      selectionRequest.current?.abort();
      const controller = new AbortController();
      selectionRequest.current = controller;
      const sources = resolveQuerySources(form, context?.sources ?? [], owner);
      const pager = createPager(sources, { ...form, pageSize: 100 }, controller, 0);
      const keys = new Set<ResourceSelectionKey>();
      for (let page = 1; ; page++) {
        const result = await pager.page(page);
        if (pager.failures.size)
          throw new Error([...pager.failures.values()].map((error) => error.message).join("; "));
        result.data.forEach((resource) => keys.add(resourceSelectionKey(resource)));
        if (!result.data.length || page * 100 >= result.totalCount) break;
      }
      return [...keys];
    },
    [context?.sources, owner, createPager],
  );

  const reloadResources = useCallback(
    async (keys: ResourceSelectionKey[], options?: { forceRefresh?: boolean }) => {
      const selected = new Set(keys);
      const groups = new Map<string, { source: ManagementSource; ids: number[] }>();
      for (const resource of current.current) {
        const matches =
          selected.has(resourceSelectionKey(resource)) ||
          (owner && selected.has(resource.id)) ||
          (!owner && resource.owner?.isLocal && selected.has(resource.id));
        if (!matches || !resource.owner) continue;
        const key = JSON.stringify([resource.owner.serverId, resource.owner.libraryEpoch]);
        if (!groups.has(key)) groups.set(key, { source: resource.owner, ids: [] });
        groups.get(key)!.ids.push(resource.id);
      }
      const queryRevision = revision.current;
      const refreshRevision = ++nextRefreshRevision.current;
      for (const { source, ids } of groups.values())
        for (const id of ids)
          refreshRevisions.current.set(
            resourceSelectionKey({ id, owner: source }),
            refreshRevision,
          );
      for (const { source, ids } of groups.values()) {
        for (let start = 0; start < ids.length; start += 100) {
          const result = await createResourceApi(source).resource.getResourcesByKeys({
            ids: ids.slice(start, start + 100),
            additionalItems: metadata,
          });
          if (result.code) throw new Error(result.message || String(result.code));
          if (queryRevision !== revision.current) return;
          const checked = new Set(
            ids
              .slice(start, start + 100)
              .map((id) => resourceSelectionKey({ id, owner: source }))
              .filter((key) => refreshRevisions.current.get(key) === refreshRevision),
          );
          const updates = new Map<ResourceSelectionKey, Resource>();
          for (const resource of result.data ?? []) {
            const updated = {
              ...resource,
              owner: source,
              ...(options?.forceRefresh ? { reloadToken: Date.now() } : {}),
            } as Resource;
            if (checked.has(resourceSelectionKey(updated)))
              updates.set(resourceSelectionKey(updated), updated);
          }
          setResources((previous) =>
            previous
              .filter(
                (resource) =>
                  !checked.has(resourceSelectionKey(resource)) ||
                  updates.has(resourceSelectionKey(resource)),
              )
              .map((resource) => updates.get(resourceSelectionKey(resource)) ?? resource),
          );
        }
      }
    },
    [owner],
  );

  const removeResources = useCallback(
    (keys: ResourceSelectionKey[]) => {
      const selected = new Set(keys);
      setResources((previous) =>
        previous.filter(
          (resource) =>
            !selected.has(resourceSelectionKey(resource)) &&
            !(owner && selected.has(resource.id)) &&
            !(!owner && resource.owner?.isLocal && selected.has(resource.id)),
        ),
      );
    },
    [owner],
  );

  useEffect(() => {
    const unsubscribers = (owner ? [owner] : (context?.sources ?? []))
      .filter((source) => !source.isLocal)
      .map((source) =>
        subscribeResourceOwnerChanges(source, (ids) => {
          active.current?.controller.abort();
          active.current = undefined;
          const keys = current.current
            .filter(
              (resource) =>
                resource.owner?.serverId === source.serverId &&
                resource.owner.libraryEpoch === source.libraryEpoch &&
                (!ids?.length || ids.includes(resource.id)),
            )
            .map(resourceSelectionKey);
          if (keys.length) void reloadResources(keys, { forceRefresh: true }).catch(() => {});
        }),
      );
    return () => unsubscribers.forEach((unsubscribe) => unsubscribe());
  }, [context?.sources, owner, reloadResources]);

  return {
    enabled,
    resources,
    setResources,
    loading,
    loadingDetails: false,
    response,
    search,
    reloadResources,
    removeResources,
    sourceErrors,
    searchAllResourceKeys,
    cancelResourceSelection,
  };
}
