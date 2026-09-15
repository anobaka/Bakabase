"use client";

import type { SearchForm } from "@/pages/resource/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { AiOutlineExport, AiOutlineFilter, AiOutlinePlus, AiOutlineSearch } from "react-icons/ai";
import { MdPlaylistPlay } from "react-icons/md";
import { HistoryOutlined } from "@ant-design/icons";

import { ReferenceValueSearchProvider } from "@/hooks/useReferenceValueResourceCounts";

import OrderSelector from "./OrderSelector";
import ShortcutsButton from "./ShortcutsButton";
import { requiresAdvancedFilterMode } from "./utils";

import { FilterDisplayMode } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { Button, Checkbox, Popover, Spinner, Tooltip } from "@/components/bakaui";
import MiscellaneousOptions from "@/pages/resource/components/FilterPanel/MiscellaneousOptions";
import CreatePlaceholderResourcesModal from "@/components/Resource/components/CreatePlaceholderResourcesModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { ResourceFilterController, GroupCombinator } from "@/components/ResourceFilter";
import { buildLogger, useTraceUpdate } from "@/components/utils.tsx";

interface IProps {
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: SearchForm;
  onSearch?: (form: Partial<SearchForm>, newTab: boolean) => Promise<any>;
  /** Fires on every local search-form change (pre-debounce) so the tab name
   *  can track filter edits live, not wait for the auto-search to fire. */
  onSearchFormLiveChange?: (form: SearchForm) => void;
  reloadResources: (ids: number[]) => any;
  rearrangeResources?: () => any;
  onSelectAllChange: (selected: boolean, includeNotLoaded?: boolean) => any;
  resourceCount?: number;
  totalFilteredResourceCount?: number;
  onOpenRecentlyPlayed?: () => void;
}

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const DefaultMaxResourceColCount = 10;
const AutoSearchDebounceMs = 1000;

const defaultSearchForm = (): SearchForm => ({
  page: 1,
  pageSize: 0,
});

const log = buildLogger("FilterPanel");

const FilterPanel = (props: IProps) => {
  const {
    maxResourceColCount = DefaultMaxResourceColCount,
    selectedResourceIds,
    onSearch,
    searchForm: propsSearchForm,
    rearrangeResources,
    onSelectAllChange,
    resourceCount,
    totalFilteredResourceCount,
    onOpenRecentlyPlayed,
  } = props;

  useTraceUpdate(props, "FilterPanel");

  const isFirstRender = useRef(true);

  useEffect(() => {
    if (isFirstRender.current) {
      log("🔵 first time render");
      isFirstRender.current = false;
    } else {
      log("🟡 update render");
    }
  });

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [selectedAll, setSelectedAll] = useState(false);

  const [searchForm, setSearchForm] = useState<SearchForm>(propsSearchForm || defaultSearchForm());
  const [searching, setSearching] = useState(false);
  const [filterMode, setFilterMode] = useState<FilterDisplayMode>(() =>
    requiresAdvancedFilterMode(propsSearchForm?.group)
      ? FilterDisplayMode.Advanced
      : FilterDisplayMode.Simple,
  );

  const [selectingAllFilteredResources, setSelectingAllFilteredResources] = useState(false);

  // Handle mode change - clean up advanced features when switching to Simple mode
  const handleModeChange = (newMode: FilterDisplayMode) => {
    if (newMode === FilterDisplayMode.Simple && filterMode === FilterDisplayMode.Advanced) {
      // When switching from Advanced to Simple:
      // 1. Remove disabled filters
      // 2. Remove sub-groups
      // 3. Reset disabled state on remaining filters
      const currentGroup = searchForm.group;

      if (currentGroup) {
        const cleanedFilters = (currentGroup.filters || [])
          .filter((f) => !f.disabled)
          .map((f) => ({ ...f, disabled: false }));

        setSearchForm({
          ...searchForm,
          group: {
            ...currentGroup,
            filters: cleanedFilters,
            groups: [], // Remove all sub-groups in Simple mode
            combinator: GroupCombinator.And, // Reset to AND
            disabled: false,
          },
        });
      }
    }
    setFilterMode(newMode);
  };

  useUpdateEffect(() => {
    setSearchForm(propsSearchForm || defaultSearchForm());
    // Restoring a query changes presentation only: never flatten or discard its conditions.
    if (requiresAdvancedFilterMode(propsSearchForm?.group)) {
      setFilterMode(FilterDisplayMode.Advanced);
    }
  }, [propsSearchForm]);

  const onSearchFormLiveChange = props.onSearchFormLiveChange;

  useEffect(() => {
    onSearchFormLiveChange?.(searchForm);
  }, [searchForm, onSearchFormLiveChange]);

  useUpdateEffect(() => {
    console.log("Search form changed", searchForm);
  }, [searchForm]);

  const search = async (patches: Partial<SearchForm>, newTab: boolean = false) => {
    if (onSearch) {
      setSearching(true);

      try {
        await onSearch(patches, newTab);
      } catch (e) {
        console.error(e);
      } finally {
        setSearching(false);
      }
    }
  };

  const searchFormRef = useRef(searchForm);

  searchFormRef.current = searchForm;

  const autoSearchTimerRef = useRef<ReturnType<typeof setTimeout>>();

  const cancelPendingAutoSearch = () => {
    if (autoSearchTimerRef.current) {
      clearTimeout(autoSearchTimerRef.current);
      autoSearchTimerRef.current = undefined;
    }
  };

  // Debounce filter-driven searches so rapid edits don't each fire a full search.
  const scheduleAutoSearch = () => {
    cancelPendingAutoSearch();
    autoSearchTimerRef.current = setTimeout(() => {
      autoSearchTimerRef.current = undefined;
      search({ ...searchFormRef.current, page: 1 });
    }, AutoSearchDebounceMs);
  };

  useEffect(() => cancelPendingAutoSearch, []);

  console.log("resource page filter panel rerender", searchForm);

  return (
    <section
      aria-label={t("resource.search.panelTitle")}
      className="flex h-full min-h-0 min-w-0 flex-col"
    >
      <header className="flex shrink-0 flex-col gap-3 pb-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="flex items-center gap-2 text-sm font-semibold">
            <AiOutlineFilter aria-hidden className="text-base text-primary" />
            {t("resource.search.panelTitle")}
          </h2>
          <div className="flex items-center gap-1">
            <ShortcutsButton />
            <Popover
              className="min-w-[160px]"
              trigger={
                <Button
                  isIconOnly
                  aria-label={t("resource.search.playlists")}
                  color={"default"}
                  size={"sm"}
                  variant={"light"}
                >
                  <MdPlaylistPlay className={"text-xl"} />
                </Button>
              }
            >
              <PlaylistCollection />
            </Popover>
            <Tooltip content={t<string>("resource.search.recentlyPlayed")}>
              <Button
                isIconOnly
                aria-label={t("resource.search.recentlyPlayed")}
                color={"default"}
                size={"sm"}
                variant={"light"}
                onPress={onOpenRecentlyPlayed}
              >
                <HistoryOutlined className={"text-base"} />
              </Button>
            </Tooltip>
            <MiscellaneousOptions rearrangeResources={rearrangeResources} />
          </div>
        </div>
        <Tooltip content={t<string>("resource.unmaterialized.tip")}>
          <Button
            className="w-full justify-start"
            color={"default"}
            size={"sm"}
            startContent={<AiOutlinePlus className={"text-base"} />}
            variant={"flat"}
            onPress={() =>
              createPortal(CreatePlaceholderResourcesModal, {
                // Re-run the current search so the new resources appear where the user is looking.
                onCreated: () => onSearch?.({}, false),
              })
            }
          >
            {t<string>("resource.unmaterialized.action.open")}
            <span className="ml-auto text-xs font-normal text-default-500">
              {t("resource.unmaterialized.later")}
            </span>
          </Button>
        </Tooltip>
      </header>

      {/* Scrollable Filters Area */}
      <div className="min-h-0 min-w-0 flex-1 overflow-y-auto overscroll-contain pr-1">
        <ReferenceValueSearchProvider search={searchForm}>
          <ResourceFilterController
            autoCreateMediaLibraryFilter
            showRecentFilters
            showTags
            filterDisplayMode={filterMode}
            filterLayout="vertical"
            group={searchForm.group}
            keyword={searchForm.keyword}
            keywordPlaceholder={t<string>("resource.search.placeholder")}
            tags={searchForm.tags}
            onFilterDisplayModeChange={handleModeChange}
            onGroupChange={(group) => {
              setSearchForm({
                ...searchForm,
                group,
              });
              scheduleAutoSearch();
            }}
            onKeywordChange={(keyword) => {
              setSearchForm({
                ...searchForm,
                keyword,
              });
            }}
            onSearch={() => {
              search({
                ...searchForm,
                page: 1,
              });
            }}
            onTagsChange={(tags) => {
              setSearchForm({
                ...searchForm,
                tags: tags.length > 0 ? tags : undefined,
              });
            }}
          />
        </ReferenceValueSearchProvider>
      </div>

      {/* Order Selector - Fixed */}
      <div className="mt-3 shrink-0">
        <OrderSelector
          value={searchForm.orders}
          onChange={(orders) => {
            const nf = {
              ...searchForm,
              orders,
            };

            setSearchForm(nf);
            cancelPendingAutoSearch();
            search(nf);
          }}
        />
      </div>

      {/* Fixed Bottom Actions */}
      <div className="mt-3 shrink-0 space-y-3 border-t border-default-100 pt-3">
        {/* Selection Info */}
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Tooltip
            content={
              <div className={"flex items-center gap-1"}>
                {t<string>("resource.search.loadedInPage")}
                {selectingAllFilteredResources ? (
                  <Spinner size={"sm"} />
                ) : (
                  totalFilteredResourceCount != resourceCount && (
                    <Button
                      color={"primary"}
                      size={"sm"}
                      variant={"light"}
                      onPress={async () => {
                        setSelectedAll(true);
                        setSelectingAllFilteredResources(true);
                        try {
                          const ret = onSelectAllChange(true, true);

                          if (!!ret && typeof ret.then === "function") {
                            await ret;
                          }
                        } finally {
                          setSelectingAllFilteredResources(false);
                        }
                      }}
                    >
                      {t<string>("resource.search.selectAllFiltered", {
                        count: totalFilteredResourceCount,
                      })}
                    </Button>
                  )
                )}
              </div>
            }
          >
            <Checkbox
              isSelected={selectedAll && selectedResourceIds && selectedResourceIds?.length > 0}
              size={"sm"}
              onValueChange={(isSelected) => {
                onSelectAllChange(isSelected);
                setSelectedAll(isSelected);
              }}
            >
              {selectedAll
                ? t<string>("resource.search.selectedCount", {
                    count: selectedResourceIds?.length,
                  })
                : t<string>("resource.search.selectAll")}
            </Checkbox>
          </Tooltip>
          <span className="text-xs tabular-nums text-default-500">
            {t("resource.search.resultCount", {
              loaded: resourceCount ?? 0,
              total: totalFilteredResourceCount ?? 0,
            })}
          </span>
        </div>

        {/* Search Buttons */}
        <div className="flex items-center gap-2">
          <Button
            className="min-w-0 flex-1"
            color={"primary"}
            isLoading={searching}
            size={"sm"}
            onPress={async () => {
              cancelPendingAutoSearch();
              await search({
                ...searchForm,
                page: 1,
              });
            }}
          >
            <AiOutlineSearch className={"text-base"} />
            {t<string>("resource.search.button")}
          </Button>
          <Button
            isLoading={searching}
            size={"sm"}
            variant="flat"
            onPress={async () => {
              await search(
                {
                  ...searchForm,
                  page: 1,
                },
                true,
              );
            }}
          >
            <AiOutlineExport className={"text-base"} />
            {t<string>("resource.search.newTab")}
          </Button>
        </div>
      </div>
    </section>
  );
};

FilterPanel.displayName = "FilterPanel";

export default FilterPanel;
