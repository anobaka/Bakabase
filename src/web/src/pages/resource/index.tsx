"use client";

import type { ResourcesRef } from "./components/Resources";
import type { SearchForm } from "@/pages/resource/models";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useUpdate, useUpdateEffect } from "react-use";
import { DisconnectOutlined } from "@ant-design/icons";

import Resources from "./components/Resources";
import styles from "./index.module.scss";
import FilterPanel from "./components/FilterPanel";

import BApi from "@/sdk/BApi";
import Resource from "@/components/Resource";
import { useUiOptionsStore } from "@/models/options";
import BusinessConstants from "@/components/BusinessConstants";
import { Button, Chip, Link, Pagination, Spinner } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { ResourceAdditionalItem } from "@/sdk/constants";

const PageSize = 100;

interface IPageable {
  page: number;
  pageSize?: number;
  totalCount: number;
}

const log = buildLogger("ResourcePage");

export default () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const { hash } = useLocation();

  const [pageable, setPageable] = useState<IPageable>();
  const pageableRef = useRef(pageable);

  const [resources, setResources] = useState<any[]>([]);
  const resourcesRef = useRef(resources);

  const uiOptionsStore = useUiOptionsStore();
  const uiOptions = uiOptionsStore.data;

  const [columnCount, setColumnCount] = useState<number>(0);
  const [searchForm, setSearchForm] = useState<SearchForm>();
  const searchFormRef = useRef(searchForm);

  const [searching, setSearching] = useState(true);
  const searchingRef = useRef(false);

  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const selectedIdsRef = useRef(selectedIds);
  const [multiSelection, setMultiSelection] = useState<boolean>(false);
  const multiSelectionRef = useRef(multiSelection);

  const resourcesComponentRef = useRef<ResourcesRef | null>();

  const { createPortal } = useBakabaseContext();

  const initStartPageRef = useRef(1);

  const initSearch = useCallback(async () => {
    let sf: Partial<SearchForm> | undefined;
    const qIdx = hash!.indexOf("?");

    if (qIdx > -1) {
      const query = new URLSearchParams(hash!.substring(qIdx)).get("query");

      if (query) {
        try {
          const searchFormFromQuery = JSON.parse(query);

          if (searchFormFromQuery) {
            sf = searchFormFromQuery;
          }
        } catch (e) {
          log("Error parsing search form from query", e);
        }
      }
    }

    log("query search form", hash, sf);

    if (!sf) {
      const r = await BApi.resource.getLastResourceSearch();

      sf = r.data ?? { page: 1, pageSize: PageSize };
    }

    search(sf, "replace");
  }, []);

  useEffect(() => {
    // Check if we're in browser environment
    if (typeof window === "undefined") {
      return;
    }

    initSearch();

    const handleScroll = () => {
      console.log(window.scrollY);
    };

    const onKeyDown = (e: KeyboardEvent) => {
      // log(e);
      if (e.key == "Control" && !multiSelectionRef.current) {
        setMultiSelection(true);
      }
    };

    const onKeyUp = (e: KeyboardEvent) => {
      if (e.key == "Control" && multiSelectionRef.current) {
        setMultiSelection(false);
      }
    };

    const onClick = (e: globalThis.MouseEvent) => {
      if (!multiSelectionRef.current) {
        if (selectedIdsRef.current.length != 0) {
          setSelectedIds([]);
        }
      }
    };

    window.addEventListener("scroll", handleScroll);
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("click", onClick);

    return () => {
      window.removeEventListener("scroll", handleScroll);
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick);
    };
  }, []);

  useEffect(() => {
    multiSelectionRef.current = multiSelection;
  }, [multiSelection]);

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

  useUpdateEffect(() => {
    selectedIdsRef.current = selectedIds;
    log("SelectedIds", selectedIds);
  }, [selectedIds]);

  const pageContainerRef = useRef<any>();

  const search = async (
    partialForm: Partial<SearchForm>,
    renderMode: "append" | "replace" | "prepend",
    replaceSearchCriteria: boolean = false,
    save: boolean = true,
  ) => {
    const baseForm = replaceSearchCriteria ? {} : searchForm;

    const newForm = {
      ...baseForm,
      ...partialForm,
      pageSize: PageSize,
    } as SearchForm;

    setSearchForm(newForm);
    log("Search resources", newForm);

    const dto = newForm;

    if (resourcesRef.current.length == 0 || renderMode == "replace") {
      initStartPageRef.current = newForm.page ?? 1;
    }

    if (renderMode == "replace") {
      setResources([]);
    }

    setSearching(true);
    const rsp = await BApi.resource.searchResources(dto, { saveSearch: save });

    if (renderMode == "replace") {
      setPageable({
        page: rsp.pageIndex!,
        pageSize: PageSize,
        totalCount: rsp.totalCount!,
      });
    }

    setSearchForm({
      ...newForm,
      page: rsp.pageIndex!,
    });

    const newResources = rsp.data || [];

    switch (renderMode) {
      case "append":
        setResources([...resources, ...newResources]);
        break;
      case "prepend":
        setResources([...newResources, ...resources]);
        break;
      default:
        setResources(newResources);
        break;
    }
    setSearching(false);
  };

  const onSelect = useCallback((id: number) => {
    const selected = selectedIdsRef.current.includes(id);

    if (selected) {
      setSelectedIds(selectedIdsRef.current.filter((id) => id != id));
    } else {
      setSelectedIds([...selectedIdsRef.current, id]);
    }
  }, []);

  const onSelectedResourcesChanged = useCallback((ids: number[]) => {
    BApi.resource
      .getResourcesByKeys({ ids, additionalItems: ResourceAdditionalItem.All })
      .then((res) => {
        const rs = res.data || [];

        for (const r of rs) {
          const idx = resourcesRef.current.findIndex((x) => x.id == r.id);

          if (idx > -1) {
            resourcesRef.current[idx] = r;
          }
        }
        setResources([...resourcesRef.current]);
      });
  }, []);

  const renderCell = useCallback(
    ({
      columnIndex, // Horizontal (column) index of cell
      // isScrolling, // The Grid is currently being scrolled
      // isVisible, // This cell is visible within the grid (eg it is not an overscanned cell)
      key, // Unique key within array of cells
      parent, // Reference to the parent Grid (instance)
      rowIndex, // Vertical (row) index of cell
      style, // Style object to be applied to cell (to position it);
      // This must be passed through to the rendered cell element.
      measure,
    }) => {
      const index = rowIndex * columnCount + columnIndex;

      if (index >= resources.length) {
        return null;
      }
      const resource = resources[index];
      const selected = selectedIdsRef.current.includes(resource.id);

      return (
        <div
          key={resource.id}
          className={"relative p-0.5"}
          style={{
            ...style,
          }}
          onLoad={measure}
        >
          <Resource
            // debug
            biggerCoverPlacement={
              index % columnCount < columnCount / 2 ? "right" : "left"
            }
            disableMediaPreviewer={uiOptions?.resource?.disableMediaPreviewer}
            mode={multiSelection ? "select" : "default"}
            resource={resource}
            selected={selected}
            selectedResourceIds={selectedIdsRef.current}
            showBiggerCoverOnHover={
              uiOptions?.resource?.showBiggerCoverWhileHover
            }
            onSelected={onSelect}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </div>
      );
    },
    [resources, multiSelection, columnCount],
  );

  log(searchForm?.page, pageable?.page, "resource page rerender");

  return (
    <div
      ref={(r) => {
        pageContainerRef.current = r?.parentElement;
      }}
      className={`${styles.resourcePage} flex flex-col h-full max-h-full relative`}
    >
      <FilterPanel
        multiSelection={multiSelection}
        rearrangeResources={() => resourcesComponentRef.current?.rearrange()}
        reloadResources={(ids) => {
          BApi.resource.getResourcesByKeys({ ids }).then((r) => {
            for (const res of r.data || []) {
              const idx = resources.findIndex((r) => r.id == res.id);

              if (idx > -1) {
                resources[idx] = res;
              }
            }
            setResources([...resources]);
          });
        }}
        resourceCount={resources.length}
        searchForm={searchForm}
        selectedResourceIds={selectedIds}
        totalFilteredResourceCount={pageable?.totalCount}
        onSearch={(f) =>
          search(
            {
              ...f,
              page: 1,
            },
            "replace",
          )
        }
        onSelectAllChange={async (selected, includeNotLoaded) => {
          if (selected) {
            if (includeNotLoaded) {
              const r = await BApi.resource.searchAllResourceIds(
                searchForm ?? { page: 1, pageSize: 100000000 },
              );
              const ids = r.data || [];

              setSelectedIds(ids);
            } else {
              setSelectedIds(resources.map((r) => r.id));
            }
          } else {
            setSelectedIds([]);
          }
        }}
      />
      {columnCount > 0 && resources.length > 0 && (
        <>
          {pageable && (
            <div className={styles.pagination}>
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
              // console.log('scrolling!!!!', e);
              // return;
              if (
                e.scrollHeight < e.scrollTop + e.clientHeight + 200 &&
                !searchingRef.current
              ) {
                searchingRef.current = true;
                const totalPage = Math.ceil(
                  (pageable?.totalCount ?? 0) / PageSize,
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

              const items = pageContainerRef.current?.querySelectorAll(
                "div[role='resource']",
              );

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
                const centerResourceId = parseInt(
                  closest.getAttribute("data-id"),
                  10,
                );
                const pageOffset = Math.floor(
                  resources.findIndex((r) => r.id == centerResourceId) /
                    PageSize,
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
            onScrollToTop={() => {
              // todo: this would be a mess because: 1. the mismatch between the columnCount and the pageSize; 2. Redefining the key of item in react-virtualized seems not to be a easy task.
              // log('scroll to top');
              // if (pageableRef.current && !searchingRef.current && searchFormRef.current) {
              //   const newPage = pageableRef.current.page - 1;
              //   if (newPage > 0 && newPage != searchFormRef.current.pageIndex) search({
              //       pageIndex: newPage,
              //     }, 'prepend', false, false);
              // }
            }}
          />
        </>
      )}
      <div
        className={
          "mt-10 flex items-center gap-2 justify-center left-0 w-ful bottom-0"
        }
        style={{ position: resources.length == 0 ? "relative" : "absolute" }}
      >
        {/* <Spinner label={t<string>('Searching...')} /> */}
        {searching ? (
          <Spinner label={t<string>("Searching...")} />
        ) : (
          resources.length == 0 && (
            <div className={"flex flex-col gap-2"}>
              <div className={"mb-2 flex items-center gap-1"}>
                <DisconnectOutlined className={"text-base"} />
                <Chip variant={"light"}>
                  {t<string>(
                    "Resource Not Found. You can try the following solutions:",
                  )}
                </Chip>
              </div>
              <div className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-1"}>
                  {t<string>(
                    "1. Please check if the search criteria is correct.",
                  )}
                  <Button
                    color={"primary"}
                    radius={"sm"}
                    size={"sm"}
                    variant={"light"}
                    onClick={() => {
                      search({ page: 1 }, "replace", true);
                    }}
                  >
                    {t<string>("Reset search criteria")}
                  </Button>
                </div>
                <div className={"flex items-center gap-1"}>
                  {t<string>(
                    "2. Please ensure that media libraries are created, bound to a template, and fully synchronized.",
                  )}
                  <Link
                    isBlock
                    href={"#/media-library"}
                    size={"sm"}
                    underline={"none"}
                  >
                    {t<string>("Go to media library page")}
                  </Link>
                </div>
              </div>
            </div>
          )
        )}
      </div>
    </div>
  );
};
