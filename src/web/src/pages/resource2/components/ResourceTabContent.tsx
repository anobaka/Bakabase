"use client";

import type { ResourcesRef } from "../../resource/components/Resources";
import type { SearchForm } from "@/pages/resource/models";

import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { usePrevious, useUpdate } from "react-use";
import { DisconnectOutlined } from "@ant-design/icons";

import Resources from "../../resource/components/Resources";
import FilterPanel from "../../resource/components/FilterPanel";

import BApi from "@/sdk/BApi";
import ResourceCard from "@/components/Resource";
import { useResourceOptionsStore, useUiOptionsStore } from "@/stores/options";
import BusinessConstants from "@/components/BusinessConstants";
import { Button, Card, CardBody, Chip, Link, Pagination, Spinner } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { ResourceAdditionalItem } from "@/sdk/constants";

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

  const [resources, setResources] = useState<any[]>([]);
  const resourcesRef = useRef(resources);

  const uiOptionsStore = useUiOptionsStore();
  const uiOptions = uiOptionsStore.data;

  const resourceOptions = useResourceOptionsStore();
  const resourceOptionsInitializedRef = useRef(false);

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
  const rearrangeResources = useCallback(() => {
    resourcesComponentRef.current?.rearrange();
  }, []);

  const reloadResources = useCallback((ids: number[]) => {
    BApi.resource.getResourcesByKeys({ ids }).then((r) => {
      for (const res of r.data || []) {
        const idx = resources.findIndex((x) => x.id == res.id);

        if (idx > -1) {
          resources[idx] = res;
        }
      }
      setResources([...resources]);
    });
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

  const onKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key == "Control" && !multiSelectionRef.current) {
      setMultiSelection(true);
    }
  }, []);

  const onKeyUp = useCallback((e: KeyboardEvent) => {
    if (e.key == "Control" && multiSelectionRef.current) {
      setMultiSelection(false);
    }
  }, []);

  const onClick = useCallback((e: globalThis.MouseEvent) => {
    if (!multiSelectionRef.current) {
      if (selectedIdsRef.current.length != 0) {
        setSelectedIds([]);
      }
    }
  }, []);

  useEffect(() => {
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick);
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
      window.addEventListener("click", onClick);
    } else {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("keyup", onKeyUp);
      window.removeEventListener("click", onClick);
    }
  }, [props.activated]);

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

      const baseForm = replaceSearchCriteria ? {} : searchFormRef.current;

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
      const rsp = await BApi.resource.searchResources(dto, {
        saveSearch: save,
        searchId: props.searchId,
      });

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
          setResources([...resourcesRef.current, ...newResources]);
          break;
        case "prepend":
          setResources([...newResources, ...resourcesRef.current]);
          break;
        default:
          setResources(newResources);
          break;
      }
      setSearching(false);
    },
    [],
  );

  const onSelect = useCallback((id: number) => {
    const selected = selectedIdsRef.current.includes(id);

    if (selected) {
      setSelectedIds(selectedIdsRef.current.filter((x) => x != id));
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
            mode={multiSelection ? "select" : "default"}
            resource={resource}
            selected={selected}
            selectedResourceIds={selectedIdsRef.current}
            onSelected={onSelect}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </div>
      );
    },
    [resources, multiSelection, columnCount],
  );

  useImperativeHandle(ref, () => ({
    getCurrentSearch: () => searchFormRef.current,
  }));

  log(searchForm?.page, pageable?.page, `initialized: ${resourceOptionsInitializedRef.current}`);

  if (!resourceOptionsInitializedRef.current) {
    return null;
  }

  return (
    <div
      ref={(r) => {
        pageContainerRef.current = r?.parentElement;
      }}
      className={`flex flex-col h-full max-h-full relative`}
    >
      <FilterPanel
        multiSelection={multiSelection}
        rearrangeResources={rearrangeResources}
        reloadResources={reloadResources}
        resourceCount={resources.length}
        searchForm={searchForm}
        selectedResourceIds={selectedIds}
        totalFilteredResourceCount={pageable?.totalCount}
        onSearch={onSearch}
        onSelectAllChange={onSelectAllChange}
      />
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
                searchingRef.current = true;
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
              <Spinner label={t<string>("Searching...")} />
            </CardBody>
          </Card>
        ) : (
          resources.length == 0 && (
            <div className={"flex flex-col gap-2"}>
              <div className={"mb-2 flex items-center gap-1"}>
                <DisconnectOutlined className={"text-base"} />
                <Chip variant={"light"}>
                  {t<string>("Resource Not Found. You can try the following solutions:")}
                </Chip>
              </div>
              <div className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-1"}>
                  {t<string>("1. Please check if the search criteria is correct.")}
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
                  <Link isBlock href={"#/media-library"} size={"sm"} underline={"none"}>
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
});

ResourceTabContent.displayName = "ResourceTabContent";

export default ResourceTabContent;
