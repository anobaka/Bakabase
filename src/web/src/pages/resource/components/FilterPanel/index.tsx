"use client";

import type { SearchForm } from "@/pages/resource/models";
import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { SearchOutlined } from "@ant-design/icons";
import { AiOutlineExport, AiOutlineSearch } from "react-icons/ai";
import { MdPlaylistPlay } from "react-icons/md";
import { HistoryOutlined } from "@ant-design/icons";

import styles from "./index.module.scss";
import OrderSelector from "./OrderSelector";

import BApi from "@/sdk/BApi";
import { FilterDisplayMode, PropertyPool, ResourceProperty, ResourceTag, SearchOperation } from "@/sdk/constants";
import { PlaylistCollection } from "@/components/Playlist";
import { Button, Checkbox, Chip, Popover, Spinner, Tooltip } from "@/components/bakaui";
import ShortcutsButton from "./ShortcutsButton";
import MiscellaneousOptions from "@/pages/resource/components/FilterPanel/MiscellaneousOptions";
import { ResourceFilterController, GroupCombinator } from "@/components/ResourceFilter";
import { buildLogger, useTraceUpdate } from "@/components/utils.tsx";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: SearchForm;
  onSearch?: (form: Partial<SearchForm>, newTab: boolean) => Promise<any>;
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

  const [selectedAll, setSelectedAll] = useState(false);

  const [searchForm, setSearchForm] = useState<SearchForm>(propsSearchForm || defaultSearchForm());
  const [searching, setSearching] = useState(false);
  const [filterMode, setFilterMode] = useState<FilterDisplayMode>(FilterDisplayMode.Simple);

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
          .filter(f => !f.disabled)
          .map(f => ({ ...f, disabled: false }));

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
  }, [propsSearchForm]);

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

  console.log("resource page filter panel rerender", searchForm);

  return (
    <div className={`${styles.filterPanel} flex flex-col h-full`}>
      {/* Top Actions */}
      <div className="flex items-center justify-between mb-3 flex-shrink-0">
        <div />
        <div className="flex items-center gap-1">
          <ShortcutsButton />
          <Popover
            className="min-w-[160px]"
            trigger={
              <Button
                isIconOnly
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

      {/* Scrollable Filters Area */}
      <div className="flex-grow overflow-y-auto min-h-0">
        <ResourceFilterController
          keyword={searchForm.keyword}
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
          group={searchForm.group}
          onGroupChange={(group) => {
            const newSearchForm = {
              ...searchForm,
              group,
            };
            setSearchForm(newSearchForm);
            // Auto-search when filter changes
            search({
              ...newSearchForm,
              page: 1,
            });
          }}
          tags={searchForm.tags}
          onTagsChange={(tags) => {
            setSearchForm({
              ...searchForm,
              tags: tags.length > 0 ? tags : undefined,
            });
          }}
          filterDisplayMode={filterMode}
          onFilterDisplayModeChange={handleModeChange}
          filterLayout="vertical"
          showRecentFilters
          showTags
          keywordPlaceholder={t<string>("resource.search.placeholder")}
          autoCreateMediaLibraryFilter
        />
      </div>

        {/* Order Selector - Fixed */}
        <div className="flex-shrink-0 mt-3">
          <OrderSelector
            value={searchForm.orders}
            onChange={(orders) => {
              const nf = {
                ...searchForm,
                orders,
              };

              setSearchForm(nf);
              search(nf);
            }}
          />
        </div>

      {/* Fixed Bottom Actions */}
      <div className="flex-shrink-0 pt-3 mt-3 border-t border-divider space-y-2">
          {/* Selection Info */}
          <div className="flex items-center justify-between">
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
                        {t<string>(
                          "Select all {{count}} filtered resources (including those not currently loaded).",
                          { count: totalFilteredResourceCount },
                        )}
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
            {totalFilteredResourceCount && totalFilteredResourceCount > 0 ? (
              <div className={"flex items-center gap-1"}>
                <Tooltip content={t<string>("resource.search.loadedResources")}>
                  <Chip color={"success"} size="sm" variant="light">
                    {resourceCount}
                  </Chip>
                </Tooltip>
                /
                <Tooltip content={t<string>("resource.search.allFiltered")}>
                  <Chip color={"secondary"} size="sm" variant="light">
                    {totalFilteredResourceCount}
                  </Chip>
                </Tooltip>
              </div>
            ) : null}
          </div>

          {/* Search Buttons */}
          <div className="flex items-center gap-2">
            <Button
              color={"primary"}
              isLoading={searching}
              size={"sm"}
              className="flex-1"
              onPress={async () => {
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
    </div>
  );
};

FilterPanel.displayName = "FilterPanel";

export default FilterPanel;
