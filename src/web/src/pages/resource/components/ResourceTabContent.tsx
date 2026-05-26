"use client";

import type { ResourcesRef } from "./Resources";
import type { SearchForm } from "@/pages/resource/models";

import React, {
  useCallback,
  useDeferredValue,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { useTranslation } from "react-i18next";
import { usePrevious, useUpdate } from "react-use";
import { SearchOutlined, FolderOpenOutlined } from "@ant-design/icons";
import { MdVideoLibrary } from "react-icons/md";
import { AiOutlineControl } from "react-icons/ai";

import Resources from "./Resources";
import FilterPanel from "./FilterPanel";

import BApi from "@/sdk/BApi";
import ResizablePanelDivider from "@/components/ResizablePanelDivider";
import ResourceCard from "@/components/Resource";
import { useResourceOptionsStore, useUiOptionsStore } from "@/stores/options";
import BusinessConstants from "@/components/BusinessConstants";
import { Button, Card, CardBody, Link, Pagination, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useResourceSearch } from "@/hooks/useResourceSearch";

const BasePageSize = 50;
const getPageSize = (cols: number) =>
  cols > 0 ? Math.ceil(BasePageSize / cols) * cols : BasePageSize;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

type Props = {
  searchId: string;
  searchInNewTab?: (form: SearchForm) => any;
  activated: boolean;
  onOpenRecentlyPlayed?: () => void;
};

export type ResourceTabContentRef = {
  getCurrentSearch: () => SearchForm | undefined;
};

const ResourceTabContent = React.forwardRef<ResourceTabContentRef, Props>((props, ref) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const [pageable, setPageable] = useState<IPageable>();
  const pageableRef = useRef(pageable);

  // Use the progressive search hook for two-phase loading
  const {
    resources,
    setResources,
    loading: searching,
    loadingDetails,
    response: searchResponse,
    search: progressiveSearch,
    reloadResources,
  } = useResourceSearch();
  const resourcesRef = useRef(resources);
  // Deferred resources keep the heavy grid render low-priority so filter edits stay responsive.
  const displayResources = useDeferredValue(resources);

  const uiOptionsStore = useUiOptionsStore();
  const uiOptions = uiOptionsStore.data;

  const resourceOptions = useResourceOptionsStore();
  const resourceOptionsInitializedRef = useRef(false);

  const [columnCount, setColumnCount] = useState<number>(0);
  const [searchForm, setSearchForm] = useState<SearchForm>();
  const searchFormRef = useRef(searchForm);

  const searchingRef = useRef(false);

  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const selectedIdsRef = useRef(selectedIds);
  // Mirror of selectedIds as a stable RefObject<Resource[]> so we can pass
  // a stable reference to each ResourceCard. The contents update on selection
  // change but the ref object itself never changes, which keeps React.memo
  // effective on ResourceCard.
  const selectedResourcesRef = useRef<typeof resources>([]);
  const multiSelectionRef = useRef(false);
  const lastSelectedIndexRef = useRef<number | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);

  const resourcesComponentRef = useRef<ResourcesRef | null>();
  const rearrangeResources = useCallback(() => {
    resourcesComponentRef.current?.rearrange();
  }, []);

  const onSearch = useCallback(async (f: Partial<SearchForm>, newTab: boolean) => {
    if (newTab) {
      props.searchInNewTab?.({ page: 1, pageSize: 50, ...f });
    } else {
      search(
        {
          ...f,
          page: 1,
        },
        "replace",
        false,
        true,
      );
    }
  }, []);

  const onSelectAllChange = useCallback(async (selected: boolean, includeNotLoaded?: boolean) => {
    if (selected) {
      if (includeNotLoaded) {
        const r = await BApi.resource.searchAllResourceIds(
          searchFormRef.current ?? { page: 1, pageSize: 100000000 },
        );
        const ids = r.data || [];

        setSelectedIds(ids);
      } else {
        setSelectedIds(resourcesRef.current.map((r) => r.id));
      }
    } else {
      setSelectedIds([]);
    }
  }, []);

  const { createPortal } = useBakabaseContext();

  const initStartPageRef = useRef(1);

  useEffect(() => {
    if (resourceOptionsInitializedRef.current) {
      return;
    }
    if (resourceOptions.initialized) {
      BApi.resource.getSavedSearch({ id: props.searchId }).then((r) => {
        const sf = r.data!;

        resourceOptionsInitializedRef.current = true;
        setSearchForm(sf.search);
        search(sf.search, "replace");
      });
    }
  }, [resourceOptions.initialized]);

  // Use Meta (Command) on Mac, Control on Windows/Linux
  const isMac =
    typeof navigator !== "undefined" && navigator.platform.toUpperCase().indexOf("MAC") >= 0;
  const selectionKey = isMac ? "Meta" : "Control";

  // Use refs for event handlers to avoid stale closures in event listeners
  const onKeyDownRef = useRef<(e: KeyboardEvent) => void>(() => {});
  const onKeyUpRef = useRef<(e: KeyboardEvent) => void>(() => {});
  const onClickRef = useRef<(e: globalThis.MouseEvent) => void>(() => {});
  const onWindowBlurRef = useRef<() => void>(() => {});

  onKeyDownRef.current = (e: KeyboardEvent) => {
    // Ctrl+A / Cmd+A: Select all loaded resources
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "a") {
      e.preventDefault();
      setSelectedIds(resourcesRef.current.map((r) => r.id));

      return;
    }
    if (e.key == selectionKey && !multiSelectionRef.current) {
      multiSelectionRef.current = true;
      containerRef.current?.setAttribute("data-selection-mode", "true");
    }
  };

  onKeyUpRef.current = (e: KeyboardEvent) => {
    if (e.key == selectionKey && multiSelectionRef.current) {
      multiSelectionRef.current = false;
      containerRef.current?.removeAttribute("data-selection-mode");
    }
  };

  onWindowBlurRef.current = () => {
    if (multiSelectionRef.current) {
      multiSelectionRef.current = false;
      containerRef.current?.removeAttribute("data-selection-mode");
    }
  };

  onClickRef.current = (e: globalThis.MouseEvent) => {
    if (!multiSelectionRef.current && !e.shiftKey) {
      // Don't clear selection if clicking on menu items, modals, or other overlay elements
      const target = e.target as HTMLElement;

      if (target.closest(".szh-menu, [role='dialog'], [role='menu'], .bakaui-modal")) {
        return;
      }

      if (selectedIdsRef.current.length != 0) {
        setSelectedIds([]);
        lastSelectedIndexRef.current = null;
      }
    }
  };

  // Stable event handler delegates that forward to the latest ref
  const stableKeyDown = useCallback((e: KeyboardEvent) => onKeyDownRef.current(e), []);
  const stableKeyUp = useCallback((e: KeyboardEvent) => onKeyUpRef.current(e), []);
  const stableClick = useCallback((e: globalThis.MouseEvent) => onClickRef.current(e), []);
  const stableBlur = useCallback(() => onWindowBlurRef.current(), []);

  useEffect(() => {
    return () => {
      window.removeEventListener("keydown", stableKeyDown);
      window.removeEventListener("keyup", stableKeyUp);
      window.removeEventListener("click", stableClick, true);
      window.removeEventListener("blur", stableBlur);
      document.removeEventListener("visibilitychange", stableBlur);
    };
  }, []);

  const prevActivated = usePrevious(props.activated);

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }

    if (props.activated == prevActivated) {
      return;
    }

    if (props.activated) {
      window.addEventListener("keydown", stableKeyDown);
      window.addEventListener("keyup", stableKeyUp);
      window.addEventListener("click", stableClick, true);
      window.addEventListener("blur", stableBlur);
      document.addEventListener("visibilitychange", stableBlur);
    } else {
      window.removeEventListener("keydown", stableKeyDown);
      window.removeEventListener("keyup", stableKeyUp);
      window.removeEventListener("click", stableClick, true);
      window.removeEventListener("blur", stableBlur);
      document.removeEventListener("visibilitychange", stableBlur);
    }
  }, [props.activated]);

  useEffect(() => {
    if (uiOptionsStore.initialized) {
      const c = uiOptions?.resource?.colCount
        ? uiOptions.resource.colCount
        : BusinessConstants.DefaultResourceColumnCount;

      if (!uiOptions.resource || uiOptions.resource.colCount == 0) {
        BApi.options.patchUiOptions({
          resource: {
            ...(uiOptions.resource || {}),
            colCount: c,
          },
        });
      }
      if (columnCount == 0 || columnCount != c) {
        setColumnCount(c);
      }
    }
  }, [uiOptions]);

  useEffect(() => {
    searchFormRef.current = searchForm;
  }, [searchForm]);

  // Intentionally NOT mirroring `searching` into searchingRef here.
  // `searching` only tracks Phase 1 (it flips to false the instant Phase 1's
  // `setLoading(false)` fires, while Phase 2 — which fetches displayName,
  // properties, etc. — is still in flight). Mirroring it would prematurely
  // unlock concurrent searches: a new "append" started during Phase 2 would
  // increment the hook's internal searchIdRef, causing the older Phase 2 to
  // abort on its searchIdRef mismatch check. Those resources stay forever
  // without a displayName until something triggers a full re-search.
  // The manual `searchingRef.current = true / false` around progressiveSearch
  // in `search()` below already guards the full P1+P2 lifetime correctly.

  useEffect(() => {
    pageableRef.current = pageable;
  }, [pageable]);

  useEffect(() => {
    resourcesRef.current = resources;
  }, [resources]);

  // Keep selectedResourcesRef.current in sync with the current selection.
  // Computed in an effect so the work only happens when either resources or
  // selectedIds change, not on every render.
  useEffect(() => {
    if (selectedIds.length === 0) {
      selectedResourcesRef.current = [];

      return;
    }
    const set = new Set(selectedIds);

    selectedResourcesRef.current = resources.filter((r) => set.has(r.id));
  }, [resources, selectedIds]);

  // When Phase 2 finishes (loadingDetails transitions true → false), the new
  // displayName / properties / mediaLibrary chips have just been committed to
  // the DOM. CellMeasurer's cached heights from the Phase-1 layout are now
  // stale; re-measure once so the virtualized grid uses the real heights.
  // This is the replacement for the per-image `onLoad={measure}` cascade.
  const prevLoadingDetails = usePrevious(loadingDetails);

  useEffect(() => {
    if (prevLoadingDetails === true && loadingDetails === false) {
      resourcesComponentRef.current?.measure();
    }
  }, [loadingDetails, prevLoadingDetails]);

  // Update pageable when search response changes
  useEffect(() => {
    if (searchResponse) {
      setPageable((prev) => ({
        // Only update page on initial load (when prev is undefined) or when it's a replace search
        page: prev?.page ?? searchResponse.pageIndex,
        pageSize: searchResponse.pageSize,
        totalCount: searchResponse.totalCount,
      }));
    }
  }, [searchResponse]);

  // Auto-load more when content doesn't fill the viewport
  useEffect(() => {
    const pageSize = pageable?.pageSize;

    if (pageable && pageSize && props.activated && !searching && resources.length > 0) {
      const timer = setTimeout(() => {
        const gridElement = pageContainerRef.current?.querySelector(".ReactVirtualized__Grid");

        if (gridElement && gridElement.scrollHeight <= gridElement.clientHeight) {
          const totalPage = Math.ceil(pageable.totalCount / pageSize);
          // Calculate loaded pages from resources.length
          const loadedPages = Math.ceil(resources.length / pageSize);

          if (loadedPages < totalPage) {
            search({ page: loadedPages + 1 }, "append", false, false);
          }
        }
      }, 300);

      return () => clearTimeout(timer);
    }
  }, [pageable, props.activated, searching, resources.length]);

  // Sync ref during render phase to ensure renderCell gets the latest value
  if (selectedIdsRef.current !== selectedIds) {
    selectedIdsRef.current = selectedIds;
  }

  const pageContainerRef = useRef<any>();

  // rAF-throttle the expensive part of onScroll (DOM querySelectorAll +
  // center-resource calculation + setPageable + persistence API call). Raw
  // scroll events fire many times per frame; coalescing to once per frame
  // keeps the main thread responsive without losing the trailing event.
  const scrollRafIdRef = useRef<number>(0);
  const lastScrollEventRef = useRef<{
    clientHeight: number;
    clientWidth: number;
    scrollHeight: number;
    scrollLeft: number;
    scrollTop: number;
    scrollWidth: number;
  } | null>(null);

  useEffect(() => {
    return () => {
      if (scrollRafIdRef.current !== 0) {
        cancelAnimationFrame(scrollRafIdRef.current);
        scrollRafIdRef.current = 0;
      }
    };
  }, []);

  const search = useCallback(
    async (
      partialForm: Partial<SearchForm>,
      renderMode: "append" | "replace" | "prepend",
      replaceSearchCriteria: boolean = false,
      save: boolean = true,
    ) => {
      console.trace();

      // Prevent concurrent searches
      if (searchingRef.current) {
        return;
      }

      searchingRef.current = true;

      const baseForm = replaceSearchCriteria ? {} : searchFormRef.current;

      const newForm = {
        ...baseForm,
        ...partialForm,
        pageSize: getPageSize(columnCount),
      } as SearchForm;

      setSearchForm(newForm);

      if (resourcesRef.current.length == 0 || renderMode == "replace") {
        initStartPageRef.current = newForm.page ?? 1;
      }

      if (renderMode == "replace") {
        lastSelectedIndexRef.current = null;
        // Reset pageable so page will be set from searchResponse on next load
        setPageable(undefined);
      }

      // Use the progressive search hook for two-phase loading
      // Pageable will be automatically updated via the searchResponse effect
      try {
        return await progressiveSearch(newForm, {
          saveSearch: save,
          searchId: props.searchId,
          mode: renderMode,
        });
      } finally {
        // Always reset searching flag after search completes
        searchingRef.current = false;
      }
    },
    [progressiveSearch, props.searchId],
  );

  const onSelect = useCallback((id: number, shiftKey: boolean = false) => {
    const currentIndex = resourcesRef.current.findIndex((r) => r.id === id);

    // Shift+Click range selection
    if (shiftKey && lastSelectedIndexRef.current !== null && currentIndex !== -1) {
      const start = Math.min(lastSelectedIndexRef.current, currentIndex);
      const end = Math.max(lastSelectedIndexRef.current, currentIndex);
      const rangeIds = resourcesRef.current.slice(start, end + 1).map((r) => r.id);

      // Merge with existing selection
      const newSelectedIds = [...new Set([...selectedIdsRef.current, ...rangeIds])];

      setSelectedIds(newSelectedIds);

      // Update last selected index to current position for subsequent shift selections
      lastSelectedIndexRef.current = currentIndex;
    } else {
      // Normal toggle selection
      const selected = selectedIdsRef.current.includes(id);

      if (selected) {
        setSelectedIds(selectedIdsRef.current.filter((x) => x != id));
      } else {
        setSelectedIds([...selectedIdsRef.current, id]);
      }

      // Update last selected index for future range selection
      if (currentIndex !== -1) {
        lastSelectedIndexRef.current = currentIndex;
      }
    }
  }, []);

  const onSelectedResourcesChanged = useCallback(
    (ids: number[]) => {
      reloadResources(ids);
    },
    [reloadResources],
  );

  type GridCellRenderArgs = {
    columnIndex: number;
    key: string;
    parent: any;
    rowIndex: number;
    style: React.CSSProperties;
    measure: () => void;
  };

  // No `onLoad={measure}` here on purpose: cover height is CSS-driven via
  // `pb-[100%]`, so image loads don't actually change cell height. The
  // height-affecting content (displayName / properties / mediaLibrary chips)
  // arrives in Phase 2, and we re-measure once when that lands (see the
  // loadingDetails-transition effect below). Per-image `onLoad` cascading
  // measure() calls would otherwise fire dozens of times per second during
  // scroll-triggered loads.
  const renderCell = useCallback(
    ({ columnIndex, rowIndex, style }: GridCellRenderArgs) => {
      const index = rowIndex * columnCount + columnIndex;

      if (index >= displayResources.length) {
        return null;
      }
      const resource = displayResources[index];
      const selected = selectedIdsRef.current.includes(resource.id);

      return (
        <div
          key={resource.id}
          className={"relative p-0.5"}
          style={{
            ...style,
          }}
        >
          <ResourceCard
            biggerCoverPlacement={index % columnCount < columnCount / 2 ? "right" : "left"}
            resource={resource}
            selected={selected}
            selectedResourceIdsRef={selectedIdsRef}
            selectedResourcesRef={selectedResourcesRef}
            selectionModeRef={multiSelectionRef}
            onSelected={onSelect}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </div>
      );
    },
    [displayResources, columnCount, onSelect, onSelectedResourcesChanged],
  );

  useImperativeHandle(ref, () => ({
    getCurrentSearch: () => searchFormRef.current,
  }));

  if (!resourceOptionsInitializedRef.current) {
    return null;
  }

  const leftPanelContent = (
    <div className="h-full pr-2">
      <FilterPanel
        rearrangeResources={rearrangeResources}
        reloadResources={reloadResources}
        resourceCount={resources.length}
        searchForm={searchForm}
        selectedResourceIds={selectedIds}
        totalFilteredResourceCount={pageable?.totalCount}
        onOpenRecentlyPlayed={props.onOpenRecentlyPlayed}
        onSearch={onSearch}
        onSelectAllChange={onSelectAllChange}
      />
    </div>
  );

  const rightPanelContent = (
    <div className="flex flex-col h-full overflow-hidden">
      {columnCount > 0 && displayResources.length > 0 && (
        <>
          {pageable &&
            pageable.pageSize != undefined &&
            Math.ceil(pageable.totalCount / pageable.pageSize) > 1 && (
              <div className={"flex items-center justify-end py-2"}>
                <Pagination
                  showControls
                  boundaries={3}
                  page={pageable.page}
                  size={"sm"}
                  total={Math.ceil(pageable.totalCount / pageable.pageSize)}
                  onChange={(p) => {
                    search(
                      {
                        page: p,
                      },
                      "replace",
                    );
                  }}
                />
              </div>
            )}
          <Resources
            ref={(r) => {
              resourcesComponentRef.current = r;
            }}
            cellCount={displayResources.length}
            columnCount={columnCount}
            renderCell={renderCell}
            onScroll={(e) => {
              if (!props.activated) {
                return;
              }

              // Load-more check is cheap, run it unthrottled so we react
              // immediately when the user hits the bottom.
              if (e.scrollHeight < e.scrollTop + e.clientHeight + 200 && !searchingRef.current) {
                const totalPage = Math.ceil(
                  (pageable?.totalCount ?? 0) / (pageable?.pageSize ?? BasePageSize),
                );

                if (
                  searchFormRef.current?.page != undefined &&
                  searchFormRef.current.page < totalPage
                ) {
                  search(
                    {
                      page: searchFormRef.current.page + 1,
                    },
                    "append",
                    false,
                    false,
                  );
                }
              }

              // Heavy: DOM query + center-resource calc + setPageable +
              // persistence call. Coalesce to once per frame, always using
              // the most recent scroll event (rAF auto-trails: when scroll
              // stops, the last scheduled frame still runs).
              lastScrollEventRef.current = e;
              if (scrollRafIdRef.current === 0) {
                scrollRafIdRef.current = requestAnimationFrame(() => {
                  scrollRafIdRef.current = 0;
                  const ev = lastScrollEventRef.current;

                  if (!ev) return;

                  const items =
                    pageContainerRef.current?.querySelectorAll("div[role='resource']");

                  if (!items || items.length === 0) return;

                  const x = ev.clientWidth / 2;
                  const y = ev.scrollTop + ev.clientHeight / 2;

                  let closest = items[0];
                  let minDis = Number.MAX_VALUE;

                  for (const item of items) {
                    const parent = item.parentElement;

                    if (!parent) continue;
                    const ix = parent.offsetLeft + parent.clientWidth / 2;
                    const iy = parent.offsetTop + parent.clientHeight / 2;
                    const dis = (ix - x) ** 2 + (iy - y) ** 2;

                    if (dis < minDis) {
                      minDis = dis;
                      closest = item;
                    }
                  }
                  const centerResourceId = parseInt(closest.getAttribute("data-id") ?? "0", 10);
                  const pageOffset = Math.floor(
                    resourcesRef.current.findIndex((r) => r.id == centerResourceId) /
                      (searchFormRef.current?.pageSize ?? BasePageSize),
                  );
                  const currentPage = pageOffset + initStartPageRef.current;

                  if (currentPage != pageableRef.current?.page) {
                    setPageable({
                      ...pageableRef.current!,
                      page: currentPage,
                    });
                    BApi.options.patchResourceOptions({
                      searchCriteria: searchFormRef.current,
                    });
                  }
                });
              }
            }}
            onScrollToTop={() => {}}
          />
        </>
      )}
      <div
        className={"mt-10 flex items-center gap-2 justify-center left-0 w-full bottom-0"}
        style={{ position: resources.length == 0 ? "relative" : "absolute" }}
      >
        {searching ? (
          <Card>
            <CardBody className={"w-[120px]"}>
              <Spinner label={t<string>("resource.search.searching")} />
            </CardBody>
          </Card>
        ) : (
          resources.length == 0 && (
            <Card className="w-[520px]">
              <CardBody className="gap-5 p-6">
                <div className="flex items-center gap-3">
                  <FolderOpenOutlined className="text-2xl text-default-400" />
                  <span className="text-lg font-medium">{t("resource.empty.title")}</span>
                </div>
                <div className="flex flex-col gap-4">
                  <div className="p-4 rounded-xl bg-default-100 hover:bg-default-200 transition-colors">
                    <div className="flex items-center gap-3 mb-2">
                      <SearchOutlined className="text-xl text-primary" />
                      <span className="font-medium">{t("resource.empty.checkCriteria")}</span>
                    </div>
                    <p className="text-sm text-default-500 mb-3 ml-8">
                      {t("resource.empty.checkCriteriaDesc")}
                    </p>
                    <div className="ml-8">
                      <Button
                        color="primary"
                        radius="sm"
                        size="sm"
                        variant="flat"
                        onClick={() => {
                          search({ page: 1 }, "replace", true);
                        }}
                      >
                        {t("resource.empty.resetCriteria")}
                      </Button>
                    </div>
                  </div>
                  <div className="p-4 rounded-xl bg-default-100 hover:bg-default-200 transition-colors">
                    <div className="flex items-center gap-3 mb-2">
                      <MdVideoLibrary className="text-xl text-primary" />
                      <span className="font-medium">{t("resource.empty.addToLibrary")}</span>
                    </div>
                    <p className="text-sm text-default-500 mb-3 ml-8">
                      {t("resource.empty.addToLibraryDesc")}
                    </p>
                    <div className="ml-8">
                      <Link href="#/media-library" underline="none">
                        <Button color="primary" radius="sm" size="sm" variant="flat">
                          {t("resource.empty.goToLibrary")}
                        </Button>
                      </Link>
                    </div>
                  </div>
                  <div className="p-4 rounded-xl bg-default-100 hover:bg-default-200 transition-colors">
                    <div className="flex items-center gap-3 mb-2">
                      <AiOutlineControl className="text-xl text-primary" />
                      <span className="font-medium">{t("resource.empty.configurePathMark")}</span>
                    </div>
                    <p className="text-sm text-default-500 mb-3 ml-8">
                      {t("resource.empty.configurePathMarkDesc")}
                    </p>
                    <div className="ml-8">
                      <Link href="#/path-mark-config" underline="none">
                        <Button color="primary" radius="sm" size="sm" variant="flat">
                          {t("resource.empty.goToPathMarkConfig")}
                        </Button>
                      </Link>
                    </div>
                  </div>
                </div>
              </CardBody>
            </Card>
          )
        )}
      </div>
    </div>
  );

  return (
    <div
      ref={(r) => {
        containerRef.current = r;
        pageContainerRef.current = r?.parentElement;
      }}
      className="h-full max-h-full relative"
    >
      <ResizablePanelDivider
        collapsible
        className="h-full"
        collapsedStorageKey="resource-filter-panel-collapsed"
        defaultWidth={320}
        leftPanel={leftPanelContent}
        maxWidth={500}
        minWidth={250}
        rightPanel={rightPanelContent}
        storageKey="resource-filter-panel-width"
      />
    </div>
  );
});

ResourceTabContent.displayName = "ResourceTabContent";

export default React.memo(ResourceTabContent);
