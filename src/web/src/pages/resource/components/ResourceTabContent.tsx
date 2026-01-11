"use client";

import type { ResourcesRef } from "./Resources";
import type { SearchForm } from "@/pages/resource/models";

import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from "react";
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
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useResourceSearch } from "@/hooks/useResourceSearch";

const PageSize = 50;

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

const log = buildLogger("ResourceTabContent");

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
    response: searchResponse,
    search: progressiveSearch,
    reloadResources,
  } = useResourceSearch();
  const resourcesRef = useRef(resources);

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

  log("render");

  useEffect(() => {
    if (resourceOptionsInitializedRef.current) {
      return;
    }
    if (resourceOptions.initialized) {
      BApi.resource.getSavedSearch({ id: props.searchId }).then((r) => {
        const sf = r.data!;

        resourceOptionsInitializedRef.current = true;
        setSearchForm(sf.search);
        log("Initial search form", sf);
        search(sf.search, "replace");
      });
    }
  }, [resourceOptions.initialized]);

  // Use Meta (Command) on Mac, Control on Windows/Linux
  const isMac = typeof navigator !== "undefined" && navigator.platform.toUpperCase().indexOf("MAC") >= 0;
  const selectionKey = isMac ? "Meta" : "Control";

  const onKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key == selectionKey && !multiSelectionRef.current) {
      multiSelectionRef.current = true;
      containerRef.current?.setAttribute("data-selection-mode", "true");
    }
  }, [selectionKey]);

  const onKeyUp = useCallback((e: KeyboardEvent) => {
    if (e.key == selectionKey && multiSelectionRef.current) {
      multiSelectionRef.current = false;
      containerRef.current?.removeAttribute("data-selection-mode");
    }
  }, [selectionKey]);

  const onClick = useCallback((e: globalThis.MouseEvent) => {
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
  }, []);

  useEffect(() => {
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick, true);
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
      window.addEventListener("keydown", onKeyDown);
      window.addEventListener("keyup", onKeyUp);
      window.addEventListener("click", onClick, true);
    } else {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick, true);
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

  useEffect(() => {
    searchingRef.current = searching;
  }, [searching]);

  useEffect(() => {
    pageableRef.current = pageable;
  }, [pageable]);

  useEffect(() => {
    resourcesRef.current = resources;
  }, [resources]);

  // Update pageable when search response changes
  useEffect(() => {
    if (searchResponse) {
      setPageable({
        page: searchResponse.pageIndex,
        pageSize: searchResponse.pageSize,
        totalCount: searchResponse.totalCount,
      });
      // Also update searchForm with the actual page index
      setSearchForm((prev) => prev ? { ...prev, page: searchResponse.pageIndex } : prev);
    }
  }, [searchResponse]);

  // Sync ref during render phase to ensure renderCell gets the latest value
  if (selectedIdsRef.current !== selectedIds) {
    selectedIdsRef.current = selectedIds;
    log("SelectedIds", selectedIds);
  }

  const pageContainerRef = useRef<any>();

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
        log("Search already in progress, skipping");
        return;
      }

      searchingRef.current = true;

      const baseForm = replaceSearchCriteria ? {} : searchFormRef.current;

      const newForm = {
        ...baseForm,
        ...partialForm,
        pageSize: PageSize,
      } as SearchForm;

      setSearchForm(newForm);
      log("Search resources", newForm);

      if (resourcesRef.current.length == 0 || renderMode == "replace") {
        initStartPageRef.current = newForm.page ?? 1;
      }

      if (renderMode == "replace") {
        lastSelectedIndexRef.current = null;
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

  const onSelectedResourcesChanged = useCallback((ids: number[]) => {
    reloadResources(ids);
  }, [reloadResources]);

  type GridCellRenderArgs = {
    columnIndex: number;
    key: string;
    parent: any;
    rowIndex: number;
    style: React.CSSProperties;
    measure: () => void;
  };

  const renderCell = useCallback(
    ({ columnIndex, key, parent, rowIndex, style, measure }: GridCellRenderArgs) => {
      // console.log("123456789");
      const index = rowIndex * columnCount + columnIndex;

      if (index >= resources.length) {
        return null;
      }
      const resource = resources[index];
      const selected = selectedIdsRef.current.includes(resource.id);
      // Get selected resources data for context menu
      const selectedResources = resources.filter(r => selectedIdsRef.current.includes(r.id));

      return (
        <div
          key={resource.id}
          onLoad={measure}
          // ref={(el) => {
          //   if (el) {
          //     measure();
          //   }
          // }}
          className={"relative p-0.5"}
          style={{
            ...style,
          }}
        >
          <ResourceCard
            biggerCoverPlacement={index % columnCount < columnCount / 2 ? "right" : "left"}
            selectionModeRef={multiSelectionRef}
            resource={resource}
            selected={selected}
            selectedResourceIds={selectedIdsRef.current}
            selectedResources={selectedResources}
            onSelected={onSelect}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </div>
      );
    },
    [resources, columnCount],
  );

  useImperativeHandle(ref, () => ({
    getCurrentSearch: () => searchFormRef.current,
  }));

  log(searchForm?.page, pageable?.page, `initialized: ${resourceOptionsInitializedRef.current}`);

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
        {columnCount > 0 && resources.length > 0 && (
          <>
            {pageable && (
              <div className={"flex items-center justify-end py-2"}>
                <Pagination
                  showControls
                  boundaries={3}
                  page={pageable.page}
                  size={"sm"}
                  total={
                    pageable.pageSize == undefined
                      ? 0
                      : Math.ceil(pageable.totalCount / pageable.pageSize)
                  }
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
            cellCount={resources.length}
            columnCount={columnCount}
            renderCell={renderCell}
            onScroll={(e) => {
              if (!props.activated) {
                return;
              }
              if (e.scrollHeight < e.scrollTop + e.clientHeight + 200 && !searchingRef.current) {
                const totalPage = Math.ceil((pageable?.totalCount ?? 0) / PageSize);

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

              const items = pageContainerRef.current?.querySelectorAll("div[role='resource']");

              if (items && items.length > 0) {
                const x = e.clientWidth / 2;
                const y = e.scrollTop + e.clientHeight / 2;

                log("on Scroll", `center: ${x},${y}`);
                let closest = items[0];
                let minDis = Number.MAX_VALUE;

                for (const item of items) {
                  const parent = item.parentElement;
                  const ix = parent.offsetLeft + parent.clientWidth / 2;
                  const iy = parent.offsetTop + parent.clientHeight / 2;
                  const dis = Math.abs((ix - x) ** 2 + (iy - y) ** 2);

                  if (dis < minDis) {
                    minDis = dis;
                    closest = item;
                  }
                }
                const centerResourceId = parseInt(closest.getAttribute("data-id"), 10);
                const pageOffset = Math.floor(
                  resources.findIndex((r) => r.id == centerResourceId) / PageSize,
                );
                const currentPage = pageOffset + initStartPageRef.current;

                log(
                  "on Scroll",
                  "center item",
                  centerResourceId,
                  closest,
                  "page offset",
                  pageOffset,
                  "active page",
                  currentPage,
                );
                if (currentPage != pageableRef.current?.page) {
                  setPageable({
                    ...pageableRef.current!,
                    page: currentPage,
                  });
                  BApi.options.patchResourceOptions({
                    searchCriteria: searchFormRef.current,
                  });
                }
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
        defaultWidth={320}
        minWidth={250}
        maxWidth={500}
        storageKey="resource-filter-panel-width"
        collapsible
        collapsedStorageKey="resource-filter-panel-collapsed"
        className="h-full"
        leftPanel={leftPanelContent}
        rightPanel={rightPanelContent}
      />
    </div>
  );
});

ResourceTabContent.displayName = "ResourceTabContent";

export default ResourceTabContent;
