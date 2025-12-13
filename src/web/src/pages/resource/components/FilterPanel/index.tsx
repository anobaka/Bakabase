"use client";

import type { SearchForm } from "@/pages/resource/models";

import { useTranslation } from "react-i18next";
import React, { useEffect, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { SearchOutlined } from "@ant-design/icons";
import { AiOutlineExport, AiOutlineSearch } from "react-icons/ai";
import { MdPlaylistPlay } from "react-icons/md";
import { HistoryOutlined } from "@ant-design/icons";

import styles from "./index.module.scss";
import OrderSelector from "./OrderSelector";

import BApi from "@/sdk/BApi";
import { PlaylistCollection } from "@/components/Playlist";
import { Button, Checkbox, Chip, Popover, Spinner, Tooltip } from "@/components/bakaui";
import ShortcutsButton from "./ShortcutsButton";
import MiscellaneousOptions from "@/pages/resource/components/FilterPanel/MiscellaneousOptions";
import ResourceKeywordAutocomplete from "@/components/ResourceKeywordAutocomplete";
import { ResourceSearchPanel } from "@/components/ResourceFilter";
import { buildLogger, useTraceUpdate } from "@/components/utils.tsx";

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

  const [selectingAllFilteredResources, setSelectingAllFilteredResources] = useState(false);

  useUpdateEffect(() => {
    setSearchForm(propsSearchForm || defaultSearchForm());
  }, [propsSearchForm]);

  

  useUpdateEffect(() => {
    console.log("Search form changed", searchForm);
  }, [searchForm]);

  const search = async (patches: Partial<SearchForm>, newTab: boolean = false) => {
    if (onSearch) {
      setSearching(true);

      // console.log("12345", newTab, onSearch);

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
    <div className={`${styles.filterPanel} flex flex-col gap-2`}>
      <div className={"flex items-center gap-4"}>
        <ResourceKeywordAutocomplete
          isClearable
          className={"w-1/4 min-w-[200px]"}
          placeholder={t<string>("Search everything")}
          startContent={<SearchOutlined className={"text-xl"} />}
          value={searchForm.keyword}
          onKeyDown={(e) => {
            if (e.key == "Enter") {
              search({
                ...searchForm,
                page: 1,
              });
            }
          }}
          onValueChange={(v) => {
            setSearchForm({
              ...searchForm,
              keyword: v,
            });
          }}
        />
        <ResourceSearchPanel
          criteria={{
            group: searchForm.group,
            keyword: searchForm.keyword,
            tags: searchForm.tags,
          }}
          onChange={(criteria) => {
            setSearchForm({
              ...searchForm,
              group: criteria.group,
              keyword: criteria.keyword,
              tags: criteria.tags,
            });
          }}
          showKeyword={false}
          showFilterGroupPreview
        />
      </div>
      <div className={"flex items-center justify-between"}>
        <div className={"flex items-center gap-4"}>
          <Button
            color={"primary"}
            isLoading={searching}
            size={"sm"}
            onPress={async () => {
              await search({
                ...searchForm,
                page: 1,
              });
            }}
          >
            <AiOutlineSearch className={"text-base"} />
            {t<string>("Search")}
          </Button>
          <Button
            // color={"primary"}
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
            {t<string>("View in new tab")}
          </Button>
        </div>
        <div className={"flex items-center gap-2"}>
          <ShortcutsButton />
          <Tooltip
            content={
              <div className={"flex items-center gap-1"}>
                {t<string>("Resources loaded in current page")}
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
                ? t<string>("{{count}} items selected", {
                    count: selectedResourceIds?.length,
                  })
                : t<string>("Select all")}
            </Checkbox>
          </Tooltip>
          {totalFilteredResourceCount && totalFilteredResourceCount > 0 ? (
            <div className={"flex items-center gap-1"}>
              <Tooltip content={t<string>("Loaded resources")}>
                <Chip color={"success"} size="sm" variant="light">
                  {resourceCount}
                </Chip>
              </Tooltip>
              /
              <Tooltip content={t<string>("All filtered resources")}>
                <Chip color={"secondary"} size="sm" variant="light">
                  {totalFilteredResourceCount}
                </Chip>
              </Tooltip>
            </div>
          ) : null}
          <OrderSelector
            className={"mr-2"}
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
          <Popover
            className="min-w-[160px]"
            trigger={
              <Button
                color={"default"}
                size={"sm"}
                startContent={<MdPlaylistPlay className={"text-xl"} />}
              >
                {t<string>("Playlist")}
              </Button>
            }
          >
            <PlaylistCollection />
          </Popover>
          <Tooltip content={t<string>("Recently played")}>
            <Button
              isIconOnly
              color={"default"}
              size={"sm"}
              onPress={onOpenRecentlyPlayed}
            >
              <HistoryOutlined className={"text-base"} />
            </Button>
          </Tooltip>
          <MiscellaneousOptions rearrangeResources={rearrangeResources} />
        </div>
      </div>
    </div>
  );
};

FilterPanel.displayName = "FilterPanel";

export default FilterPanel;
