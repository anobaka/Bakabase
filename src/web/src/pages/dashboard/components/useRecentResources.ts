import type { Resource } from "@/core/models/Resource";
import type { RecentResourceTab } from "./recentResourcesData";

import { useCallback, useEffect, useRef, useState } from "react";

import { buildRecentResourceSearch, RECENT_RESOURCE_LIMIT } from "./recentResourcesData";

import BApi from "@/sdk/BApi";
import { ResourceAdditionalItem } from "@/sdk/constants";

type ResourcePage = { resources: Resource[]; totalCount: number };
type Snapshot = ResourcePage & {
  tab: RecentResourceTab;
  refreshKey: number;
  status: "loading" | "ready" | "error";
};

export function useRecentResources(tab: RecentResourceTab, refreshKey: number) {
  const cache = useRef<{
    refreshKey: number;
    pages: Partial<Record<RecentResourceTab, ResourcePage>>;
  }>({ refreshKey, pages: {} });
  const deletedIds = useRef(new Set<number>());
  const [retryKey, setRetryKey] = useState(0);
  const [snapshot, setSnapshot] = useState<Snapshot>();

  useEffect(() => {
    if (cache.current.refreshKey !== refreshKey) {
      cache.current = { refreshKey, pages: {} };
    }
    const cached = cache.current.pages[tab];

    if (cached) {
      setSnapshot({ ...cached, tab, refreshKey, status: "ready" });

      return;
    }

    let active = true;
    const controller = new AbortController();

    setSnapshot({ tab, refreshKey, resources: [], totalCount: 0, status: "loading" });
    BApi.resource
      .searchResources(
        buildRecentResourceSearch(tab),
        { additionalItems: ResourceAdditionalItem.All, saveSearch: false },
        { signal: controller.signal },
      )
      .then((response) => {
        if (!active) return;
        if (response.code) throw new Error(response.message);
        const returned = (response.data ?? []).slice(0, RECENT_RESOURCE_LIMIT) as Resource[];
        const resources = returned.filter((resource) => !deletedIds.current.has(resource.id));
        const page = {
          resources,
          totalCount: Math.max(
            resources.length,
            (response.totalCount ?? returned.length) - (returned.length - resources.length),
          ),
        };

        cache.current.pages[tab] = page;
        setSnapshot({ ...page, tab, refreshKey, status: "ready" });
      })
      .catch(() => {
        if (!active) return;
        setSnapshot({ tab, refreshKey, resources: [], totalCount: 0, status: "error" });
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [tab, refreshKey, retryKey]);

  const retry = useCallback(() => {
    delete cache.current.pages[tab];
    setRetryKey((key) => key + 1);
  }, [tab]);

  const removeResources = useCallback((ids: number[]) => {
    if (ids.length === 0) return;
    ids.forEach((id) => deletedIds.current.add(id));
    const remove = <T extends ResourcePage>(page: T): T => {
      const resources = page.resources.filter((resource) => !deletedIds.current.has(resource.id));

      return {
        ...page,
        resources,
        totalCount: Math.max(0, page.totalCount - (page.resources.length - resources.length)),
      };
    };

    for (const key of Object.keys(cache.current.pages) as RecentResourceTab[]) {
      cache.current.pages[key] = remove(cache.current.pages[key]!);
    }
    setSnapshot((current) => (current ? remove(current) : current));
  }, []);

  // Never flash a different tab or pre-refresh list while its effect is being scheduled.
  const current =
    snapshot?.tab === tab && snapshot.refreshKey === refreshKey ? snapshot : undefined;

  return {
    resources: current?.resources ?? [],
    totalCount: current?.totalCount ?? 0,
    status: current?.status ?? "loading",
    retry,
    removeResources,
  };
}
